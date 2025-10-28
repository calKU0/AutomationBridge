using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutomationBridge.PTLWorker.Logic
{
    public class PTLResponseParser
    {
        private readonly ILogger<PTLResponseParser> _logger;

        private readonly Dictionary<string, string> _errorMap = new()
        {
            ["000"] = "OK. Brak błędu.",
            ["001"] = "Błąd techniczny (np. brak komunikacji)",
            ["002"] = "Błąd parowania komunikatu. (niewłaściwa struktura komunikatu)",
            ["003"] = "Nierozpoznany predefiniowany typ operacji (błędna nazwa).",
            ["004"] = "Nierozpoznany kod lokalizacji.",
            ["005"] = "Wartość spoza zakresu (w polach o zdefiniowanych dopuszczalnych wartościach).",
            ["006"] = "Tekst niemożliwy do wyświetlania na danym typie wyświetlacza.",
            ["007"] = "Operacja nie jest obsługiwana przez dany typ wyświetlacza."
        };

        public PTLResponseParser(ILogger<PTLResponseParser> logger)
        {
            _logger = logger;
        }

        public (string MessageType, string Sequence, string? ErrorCode, string? LocationReference, string? Text)? Parse(string response)
        {
            try
            {
                var trimmed = response.Trim('\u0002', '\u0003');
                var parts = trimmed.Split('|');

                if (parts.Length >= 3)
                {
                    var messageType = parts[0];
                    var sequence = parts[1];

                    if (messageType.Contains("C"))
                    {
                        // Confirmation message: parts[2] = LocationReference, parts[3] = optional text
                        var location = parts.Length > 2 ? parts[2] : null;
                        var text = parts.Length > 3 ? parts[3] : null;
                        return (messageType, sequence, null, location, text);
                    }
                    else
                    {
                        // Normal error message: parts[2] = ErrorCode
                        return (messageType, sequence, parts[2], null, null);
                    }
                }

                _logger.LogError("PTL response has unexpected format: {Response}", response);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse PTL response");
                return null;
            }
        }

        public void LogErrorCode(string code)
        {
            if (_errorMap.TryGetValue(code, out var message))
                _logger.LogInformation("PTL Response: {Message}", message);
            else
                _logger.LogError("Unknown PTL error code: {Code}", code);
        }
    }
}