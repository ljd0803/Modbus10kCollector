using System.Collections.Generic;

namespace Modbus10kCollector.Models
{
    public class DeviceConfig
    {
        public string DeviceId { get; set; } = string.Empty;
        public string Ip { get; set; } = string.Empty;
        public int Port { get; set; }
        public byte SlaveId { get; set; }
        public ushort StartAddr { get; set; }
        public ushort ReadCount { get; set; }
        /// <summary>连续失败阈值，达到该次数自动断开</summary>
        public int BreakThreshold { get; set; } = 5;
        /// <summary>断开后冷却秒数，冷却期内不尝试重连</summary>
        public int BreakCoolSecond { get; set; } = 60;
    }
    /// <summary>JSON根配置实体</summary>
    public class DeviceRootConfig
    {
        public List<PlcDeviceConfig> PlcList { get; set; } = new();
    }

    /// <summary>单个PLC JSON配置项</summary>
    public class PlcDeviceConfig
    {
        public string DeviceId { get; set; } = string.Empty;
        public string Ip { get; set; } = string.Empty;
        public int Port { get; set; } = 502;
        public byte SlaveId { get; set; } = 1;
        public int PollIntervalMs { get; set; } = 1000;
        public ushort StartAddr { get; set; } = 0;
        public ushort ReadLength { get; set; } = 10;
        public int BreakThreshold { get; set; } = 5;
        public int BreakCoolSecond { get; set; } = 60;
    }
}
