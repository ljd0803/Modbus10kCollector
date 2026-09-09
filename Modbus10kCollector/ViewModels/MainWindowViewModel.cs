using System;
using System.Collections.ObjectModel;
using System.Linq;
using Prism.Commands;
using Prism.Mvvm;
using NLog;
using Modbus10kCollector.Models;
using Modbus10kCollector.Services;

namespace Modbus10kCollector.ViewModels
{
    public class MainWindowViewModel : BindableBase
    {
        private readonly ILogger _logger = LogManager.GetCurrentClassLogger();
        private readonly PlcCollectService _collectService;
        private readonly MqttPublishService _mqttService;
        private readonly DeviceConfigService _deviceConfigService;
        #region UI绑定属性
        private string _title = "Modbus采集上位机";
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        private string _statusMessage = "就绪";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private bool _isCollectRunning;
        public bool IsCollectRunning
        {
            get => _isCollectRunning;
            set => SetProperty(ref _isCollectRunning, value);
        }

        /// <summary>PLC设备集合，UI绑定DataGrid</summary>
        public ObservableCollection<PlcDevice> DeviceItems { get; } = new();
        #endregion

        #region 命令
        public DelegateCommand StartCollectCommand { get; }
        public DelegateCommand StopCollectCommand { get; }
        public DelegateCommand<PlcDevice> ResetPlcCommand { get; }
        #endregion

        public MainWindowViewModel(PlcCollectService collectService, MqttPublishService mqttService, DeviceConfigService deviceConfigService)
        {
            _collectService = collectService;
            _mqttService = mqttService;
            _deviceConfigService = deviceConfigService;

            // 初始化命令
            StartCollectCommand = new DelegateCommand(ExecuteStartCollect, CanExecuteStartCollect)
                .ObservesProperty(() => IsCollectRunning);

            StopCollectCommand = new DelegateCommand(ExecuteStopCollect, CanExecuteStopCollect)
                .ObservesProperty(() => IsCollectRunning);

            ResetPlcCommand = new DelegateCommand<PlcDevice>(ExecuteResetPlc);

            // 【示例】加载测试设备，实际项目可从JSON配置文件读取
            LoadDevicesFromConfig();

            _logger.Info("MainWindowViewModel 初始化完成");
        }

        #region 采集启停
        private bool CanExecuteStartCollect() => !IsCollectRunning;
        private bool CanExecuteStopCollect() => IsCollectRunning;

        private async void ExecuteStartCollect()
        {
            try
            {
                StatusMessage = "正在启动采集...";
                // 启动MQTT连接
                await _mqttService.ConnectAsync();
                // 启动PLC采集后台循环
                _collectService.StartCollect();

                IsCollectRunning = true;
                StatusMessage = "采集已运行";
                _logger.Info("采集任务启动成功");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "启动采集失败");
                StatusMessage = $"启动失败：{ex.Message}";
            }
        }

        private async void ExecuteStopCollect()
        {
            try
            {
                StatusMessage = "正在停止采集...";
                await _collectService.StopCollectAsync();
                await _mqttService.DisconnectAsync();

                IsCollectRunning = false;
                StatusMessage = "采集已停止";
                _logger.Info("采集任务已停止");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "停止采集异常");
                StatusMessage = $"停止异常：{ex.Message}";
            }
        }
        #endregion

        #region 单设备手动重置状态
        private async void ExecuteResetPlc(PlcDevice? device)
        {
            if (device == null) return;
            try
            {
                if (device.CanFire(PlcEvent.Reset))
                {
                    await device.FireAsync(PlcEvent.Reset);
                }
                device.DisposeConnection();
                device.ContinuousFailCount = 0;
                device.BreakCoolEndTime = DateTime.MinValue; //清空冷却
                _logger.Info($"PLC[{device.DeviceId}] 手动重置完成，冷却计时已清除");
                StatusMessage = $"设备 {device.DeviceId} 已重置";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"PLC[{device.DeviceId}]重置失败");
                StatusMessage = $"重置失败：{ex.Message}";
            }
        }
        #endregion

        #region 加载设备（Demo，正式版替换成JSON配置读取）
        private void LoadDevicesFromConfig()
        {
            DeviceItems.Clear();
            var configList = _deviceConfigService.LoadDevices();

            foreach (var cfg in configList)
            {
                var plcDev = new PlcDevice
                {
                    DeviceId = cfg.DeviceId,
                    Ip = cfg.Ip,
                    Port = cfg.Port,
                    SlaveId = cfg.SlaveId,
                    StartAddr = cfg.StartAddr,
                    ReadCount = cfg.ReadCount,
                    BreakThreshold = cfg.BreakThreshold,
                    BreakCoolSecond = cfg.BreakCoolSecond
                };
                DeviceItems.Add(plcDev);
            }
            _collectService.SetDeviceList(DeviceItems.ToList());
            _logger.Info($"UI已加载 {DeviceItems.Count} 台PLC设备到界面列表");
        }
        #endregion
    }
}
