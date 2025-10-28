using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutomationBridge.PTLWorker.Settings
{
    public class PTLSettings
    {
        public string Ip { get; set; } = string.Empty;
        public int Port { get; set; }
        public int CheckStatusIntervalSeconds { get; set; }
        public int UpdateStatusIntervalSeconds { get; set; }
        public int ReconnectIntervalSeconds { get; set; }
        public int ClearCacheIntervalSeconds { get; set; }
    }
}