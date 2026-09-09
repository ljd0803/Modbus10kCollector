using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NModbus;
using Modbus10kCollector.Models;

namespace Modbus10kCollector.Services
{
    public class PlcCollectService
    {
        private readonly ILogger _logger = LogManager.GetCurrentClassLogger();
        private  List<PlcDevice> _deviceList;
        private readonly MqttPublishService _mqttPublishService;
        private readonly CancellationTokenSource _cts = new();
        private Task? _collectTask;
        // 全局并发限制：同一时间最多多少PLC并发采集，工控建议控制并发数，防止爆连接
        private readonly SemaphoreSlim _semaphore = new(8, 8);
        private const int PollIntervalMs = 1000; // 轮询间隔，可在json配置读取

        public PlcCollectService( MqttPublishService mqttPublishService)
        {
            _mqttPublishService = mqttPublishService;
        }
        public void SetDeviceList(List<PlcDevice> devices)
        {
            _deviceList = devices;
        }
        /// <summary>启动全部PLC后台采集</summary>
        public void StartCollect()
        {
            if (_collectTask != null) return;
            _collectTask = Task.Run(RunCollectLoop, _cts.Token);
            _logger.Info("PLC采集服务已启动");
        }

        /// <summary>停止采集</summary>
        public async Task StopCollectAsync()
        {
            _cts.Cancel();
            if (_collectTask != null)
            {
                await _collectTask;
                _collectTask = null;
            }
            _semaphore.Dispose();
            _logger.Info("PLC采集服务已停止");
        }

        /// <summary>主采集循环（顶层try-catch兜底，防止整个采集线程挂死）</summary>
        private async Task RunCollectLoop()
        {
            try
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    // 并行发起所有设备采集任务（带并发限流）
                    var collectTasks = new List<Task>();
                    foreach (var plc in _deviceList)
                    {
                        collectTasks.Add(SinglePlcCollectTask(plc, _cts.Token));
                    }
                    await Task.WhenAll(collectTasks);

                    await Task.Delay(PollIntervalMs, _cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.Info("采集主循环收到停止信号，正常退出");
            }
            catch (Exception ex)
            {
                // 主循环异常兜底（理论很难进入，作为最后防线）
                _logger.Fatal(ex, "采集主循环发生致命异常，采集停止！");
            }
        }

        /// <summary>单台PLC采集任务，独立try-catch，单台故障不影响其他设备</summary>
        private async Task SinglePlcCollectTask(PlcDevice plc, CancellationToken token)
        {
            // 冷却判断：断开状态并且冷却未结束，直接跳过本轮采集
            if (plc.CurrentState == PlcState.Disconnected && plc.IsInBreakCool())
            {
                return;
            }

            if (!await _semaphore.WaitAsync(100, token))
            {
                return;
            }
            try
            {
                // 连接不存在，先建立TCP连接
                if (plc.Master == null || plc.Transport == null)
                {
                    if (!plc.CanFire(PlcEvent.Connect))
                    {
                        _logger.Warn($"PLC[{plc.DeviceId}] 当前状态 {plc.CurrentState}，无法发起连接");
                        return;
                    }
                    await plc.FireAsync(PlcEvent.Connect);

                    var factory = new ModbusFactory();
                    var tcpClient = new System.Net.Sockets.TcpClient();
                    tcpClient.ReceiveTimeout = 1000;
                    tcpClient.SendTimeout = 1000;
                    var connectTask = tcpClient.ConnectAsync(plc.Ip, plc.Port);
                    await connectTask.WaitAsync(TimeSpan.FromMilliseconds(1000), token);

                    plc.Master = factory.CreateMaster(tcpClient);
                    plc.Transport = plc.Master.Transport;
                    plc.Transport.ReadTimeout = 1000;
                    plc.Transport.WriteTimeout = 1000;
                    _logger.Info($"PLC[{plc.DeviceId}] TCP连接成功 {plc.Ip}:{plc.Port}");

                    await plc.FireAsync(PlcEvent.ConnectSuccess);
                }

                // 开始采集
                if (!plc.CanFire(PlcEvent.StartCollect))
                {
                    _logger.Warn($"PLC[{plc.DeviceId}] 当前状态 {plc.CurrentState}，不能发起采集");
                    return;
                }
                await plc.FireAsync(PlcEvent.StartCollect);

                // 读取保持寄存器
                var regData = await plc.Master!.ReadHoldingRegistersAsync(plc.SlaveId, plc.StartAddr, plc.ReadCount);
                plc.ContinuousFailCount = 0;
                plc.LastReadValue = regData;
                plc.LastSuccessTime = DateTime.Now;

                await plc.FireAsync(PlcEvent.ReadSuccess);

                // MQTT5上报正常数据
                var result = new CollectResult
                {
                    DeviceId = plc.DeviceId,
                    Ip = plc.Ip,
                    PlcState = plc.CurrentState,
                    Registers = regData,
                    Time = DateTime.Now
                };
                await _mqttPublishService.PublishResult(result);
            }
            catch (Exception ex)
            {
                plc.ContinuousFailCount++;
                _logger.Error(ex, $"PLC[{plc.DeviceId}]采集异常，连续失败次数:{plc.ContinuousFailCount}");

                // 判断是【连接阶段异常】还是【采集读写异常】
                if (plc.Master == null || plc.Transport == null)
                {
                    // TCP连接失败
                    if (plc.CanFire(PlcEvent.ConnectFail))
                    {
                        await plc.FireAsync(PlcEvent.ConnectFail);
                    }
                }
                else
                {
                    // TCP连接存在，但读写失败 → Fault故障
                    if (plc.CanFire(PlcEvent.ReadFail))
                    {
                        await plc.FireAsync(PlcEvent.ReadFail);
                    }
                }

                // ========== 新增阈值判断：达到阈值，触发断开+冷却 ==========
                if (plc.ContinuousFailCount >= plc.BreakThreshold)
                {
                    _logger.Warn($"PLC[{plc.DeviceId}] 连续失败达到阈值 {plc.BreakThreshold}，进入冷却 {plc.BreakCoolSecond}s");
                    if (plc.CanFire(PlcEvent.Disconnect))
                    {
                        await plc.FireAsync(PlcEvent.Disconnect);
                    }
                    plc.StartBreakCool();
                }

                // 销毁连接，下一轮重新建立
                plc.DisposeConnection();

                // MQTT上报故障报文
                var failResult = new CollectResult
                {
                    DeviceId = plc.DeviceId,
                    Ip = plc.Ip,
                    PlcState = plc.CurrentState,
                    Msg = ex.Message,
                    Time = DateTime.Now
                };
                await _mqttPublishService.PublishResult(failResult);
            }
            finally
            {
                _semaphore.Release();
            }
        }

    }
}
