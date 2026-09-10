using System;
using System.Text.Json.Serialization;

namespace Modbus10kCollector.Models
{
    /// <summary>
    /// 采集结果消息：采集服务生产，UI/存储消费
    /// 使用Channel异步解耦，采集逻辑不阻塞UI
    /// </summary>
    public class CollectResult
    {
        public string DeviceId { get; set; } = string.Empty;
        public string Ip { get; set; } = string.Empty;
        public PlcState PlcState { get; set; }
        public ushort[] Registers { get; set; } = Array.Empty<ushort>();
       
        public DateTime Time { get; set; }
        public string Msg { get; set; } = string.Empty;

        public override string ToString()
        {
            var result = System.Text.Json.JsonSerializer.Serialize(this);
            return result;
        }
    }
}
