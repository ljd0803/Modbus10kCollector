using Modbus10kCollector.Models;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using NLog;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Modbus10kCollector.Services
{
    /// <summary>
    /// MQTT5发布服务，MQTTnet 5.2.0.163
    /// </summary>
    public class MqttPublishService
    {
        private readonly ILogger _logger = LogManager.GetCurrentClassLogger();
        private IMqttClient? _mqttClient;
        private MqttClientOptions? _mqttOptions;
        private readonly CancellationTokenSource _cts = new();
        private MqttConfig _currentMqttConfig;

       

        public MqttPublishService(MqttConfigService configService)
        {
            _currentMqttConfig = configService.GetConfig();
            // 断线事件订阅
            _ = Task.Run(BackgroundReconnectLoop, _cts.Token);
        }
       
        /// <summary>连接MQTT Broker，强制MQTT5协议</summary>
        public async Task ConnectAsync()
        {
            try
            {
                var factory = new MqttClientFactory();
                _mqttClient = factory.CreateMqttClient();
                var willResult = new CollectResult
                {
                    DeviceId = _currentMqttConfig.ClientId,
                    Ip = _currentMqttConfig.BrokerAddress,
                    PlcState = PlcState.Disconnected,
                    Msg = "mqtt_will",
                    Time = DateTime.Now
                };
                _mqttOptions = new MqttClientOptionsBuilder()
                    .WithConnectionUri(_currentMqttConfig.BrokerAddress)
                    .WithClientId(_currentMqttConfig.ClientId)
                    .WithCredentials(_currentMqttConfig.Username, _currentMqttConfig.Password)
                    .WithProtocolVersion(MqttProtocolVersion.V500) // 强制MQTT 5.0
                    .WithCleanStart(false) // MQTT5 标准，替代CleanSession。false客户端断线重连，Broker 缓存该客户端的订阅、未送达消息，重连后补发
                    .WithSessionExpiryInterval(60) // MQTT5独有：会话过期时间，单位秒，0表示立即过期，默认0

                    // ========== 遗嘱消息【新版分开配置】==========主要检测代理点轮询设备todo:代理点服务上线后要发上线消息
                    .WithWillTopic($"Mqtt/Will/{_currentMqttConfig.ClientId}")
                    .WithWillPayload(willResult.ToString())
                    .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .WithWillRetain(true)
                    .WithWillDelayInterval(10) 
                    // MQTT5独有：遗嘱延迟10秒（uint）
                    // 下面是MQTT5遗嘱额外属性（可选）
                    // .WithWillContentType("application/json")
                    // .WithWillMessageExpiryInterval(60)
                    // ============================================
                    .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
                    .Build();

                var connectResult = await _mqttClient.ConnectAsync(_mqttOptions, CancellationToken.None);
                if (connectResult.ResultCode == MqttClientConnectResultCode.Success)
                {
                    _logger.Info($"MQTT5连接成功 {_currentMqttConfig.BrokerAddress}");
                }
                else
                {
                    _logger.Warn($"MQTT5连接失败，结果码：{connectResult.ResultCode}");
                }

                // 订阅客户端断线回调
                _mqttClient.DisconnectedAsync += OnClientDisconnected;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "MQTT5连接异常");
            }
        }

        /// <summary>推送单条采集结果到MQTT</summary>
        public async Task PublishResult(CollectResult result)
        {
            if (_mqttClient == null || !_mqttClient.IsConnected)
                return;

            try
            {
                string topic = $"{_currentMqttConfig.Topic}{result.DeviceId}";
                string jsonPayload = System.Text.Json.JsonSerializer.Serialize(result);
                var payloadBytes = Encoding.UTF8.GetBytes(jsonPayload);

                var mqttMsg = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(payloadBytes)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build();

                await _mqttClient.PublishAsync(mqttMsg, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"MQTT发布失败，设备:{result.DeviceId}");
            }
        }

        /// <summary>断开MQTT</summary>
        public async Task DisconnectAsync()
        {
            _cts.Cancel();
            if (_mqttClient != null && _mqttClient.IsConnected)
            {
                await _mqttClient.DisconnectAsync();
                _logger.Info("MQTT断开连接");
            }
        }

        // 断线回调
        private Task OnClientDisconnected(MqttClientDisconnectedEventArgs args)
        {
            _logger.Warn($"MQTT5客户端断开，原因:{args.Reason}，3秒后尝试重连");
            return Task.CompletedTask;
        }

        // 后台循环自动重连
        private async Task BackgroundReconnectLoop()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    if (_mqttClient != null && !_mqttClient.IsConnected && _mqttOptions != null)
                    {
                        _logger.Info("尝试MQTT5自动重连...");
                        var factory = new MqttClientFactory();
                        _mqttClient = factory.CreateMqttClient();
                        var result = await _mqttClient.ConnectAsync(_mqttOptions, _cts.Token);
                        if (result.ResultCode == MqttClientConnectResultCode.Success)
                        {
                            _logger.Info("MQTT5自动重连成功");
                            _mqttClient.DisconnectedAsync += OnClientDisconnected;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "MQTT5自动重连失败");
                }
                await Task.Delay(3000, _cts.Token);
            }
        }
    }
}
