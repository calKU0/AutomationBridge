using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutomationBridge.PLCWorker.Settings
{
    public class PLCSettings
    {
        public string Ip { get; set; } = string.Empty;
        public int Port { get; set; }
        public int CheckAliveIntervalSeconds { get; set; }
    }
}