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

                _mqttOptions = new MqttClientOptionsBuilder()
                    .WithConnectionUri(_currentMqttConfig.BrokerAddress)
                    .WithClientId(_currentMqttConfig.ClientId)
                    .WithCredentials(_currentMqttConfig.Username, _currentMqttConfig.Password)
                    .WithProtocolVersion(MqttProtocolVersion.V500) // 强制MQTT 5.0
                    .WithCleanStart(true) // MQTT5 标准，替代CleanSession
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
