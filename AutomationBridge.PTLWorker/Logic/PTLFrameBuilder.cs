using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutomationBridge.PTLWorker.Logic
{
    public class PTLFrameBuilder
    {
        private int _messageCounter = 1;
        private readonly object _lock = new();

        public string GetNextMessageNumber()
        {
            lock (_lock)
            {
                var num = _messageCounter.ToString("D4");
                _messageCounter++;
                if (_messageCounter > 9999) _messageCounter = 1;
                return num;
            }
        }

        public string CreatePTLFrame(string messageNumber, string locationReference, string operationType, string? displayText = null, string? color = null, string? buttonBlink = null)
        {
            if (string.IsNullOrEmpty(messageNumber) || string.IsNullOrEmpty(locationReference) || string.IsNullOrEmpty(operationType))
                return string.Empty;

            // The body used for length calculation (without STX/ETX)
            string body = $"{messageNumber}|{locationReference}|{operationType}|{displayText}|{color}|{buttonBlink}";
            string length = body.Length.ToString("D3");

            // final frame with STX/ETX markers (we'll replace to 0x02 and 0x03 when sending)
            return $"STX{length}{messageNumber}|{locationReference}|{operationType}|{displayText}|{color}|{buttonBlink}ETX";
        }

        public static string ToControlChars(string frame)
        {
            return frame.Replace("STX", "\x02").Replace("ETX", "\x03");
        }
    }
}