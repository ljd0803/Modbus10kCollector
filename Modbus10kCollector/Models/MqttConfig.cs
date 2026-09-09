using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Modbus10kCollector.Models
{
    public class MqttConfig
    {
        public string BrokerAddress { get; set; } = "tcp://localhost:1883";
        public string ClientId { get; set; } = "DefaultClient";
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Topic { get; set; } = "test/topic";
    }
}
