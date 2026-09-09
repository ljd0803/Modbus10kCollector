using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NLog;
using Modbus10kCollector.Models;

namespace Modbus10kCollector.Services
{
    public class DeviceConfigService
    {
        private readonly ILogger _logger = LogManager.GetCurrentClassLogger();
        private readonly string _jsonPath;
      
        public DeviceConfigService()
        {
            // 取exe同目录下devices.json
            _jsonPath = Path.Combine(AppContext.BaseDirectory,"Config", "devices.json");
        }
      
        /// <summary>加载json配置，返回设备DTO列表</summary>
        public List<DeviceConfig> LoadDevices()
        {
            try
            {
                if (!File.Exists(_jsonPath))
                {
                    _logger.Warn($"设备配置文件不存在：{_jsonPath}");
                    return new List<DeviceConfig>();
                }

                var jsonText = File.ReadAllText(_jsonPath);
                var list =JsonSerializer.Deserialize<List<DeviceConfig>>(jsonText);
                _logger.Info($"成功读取devices.json，共加载 {list?.Count ?? 0} 台设备");
                return list ?? new List<DeviceConfig>();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "读取devices.json失败");
                return new List<DeviceConfig>();
            }
        }

        /// <summary>保存设备到json（可选，UI增改设备时用）</summary>
        public void SaveDevices(List<DeviceConfig> configs)
        {
            try
            {
                var json = JsonSerializer.Serialize(configs, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_jsonPath, json);
                _logger.Info("已保存设备配置到devices.json");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "保存devices.json失败");
            }
        }
    }
}
