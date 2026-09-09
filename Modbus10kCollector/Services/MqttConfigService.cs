using Modbus10kCollector.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Modbus10kCollector.Services
{
    public class MqttConfigService
    {
        private readonly string _configFilePath;
        private MqttConfig _cachedConfig;

        public MqttConfigService()
        {
            // 配置文件放在应用程序目录下的 mqttsettings.json（你可以改名为你希望的）
            _configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Config", "mqtt.json");
            LoadConfig();
        }

        // 读取配置（从文件或缓存）
        private void LoadConfig()
        {
            if (File.Exists(_configFilePath))
            {
                var json = File.ReadAllText(_configFilePath);
                _cachedConfig = JsonSerializer.Deserialize<MqttConfig>(json)
                                ?? new MqttConfig();
            }
            else
            {
                // 如果文件不存在，使用默认配置，并自动创建文件
                _cachedConfig = new MqttConfig();
                SaveConfig();
            }
        }

        // 获取当前配置（只读）
        public MqttConfig GetConfig()
        {
            return _cachedConfig;
        }

        // 保存配置（写入文件）
        public void SaveConfig(MqttConfig config = null)
        {
            if (config != null)
            {
                _cachedConfig = config;
            }

            var json = JsonSerializer.Serialize(_cachedConfig, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configFilePath, json);
        }

        // 如果需要，可以添加热重载或变更事件
    }
}
