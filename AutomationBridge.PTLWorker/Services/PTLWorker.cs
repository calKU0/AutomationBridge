using AutomationBridge.PTLWorker.Data;
using AutomationBridge.PTLWorker.Logic;
using AutomationBridge.PTLWorker.Settings;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Diagnostics;

namespace AutomationBridge.PTLWorker.Services
{
    public class PTLWorker : BackgroundService
    {
        private readonly PTLSettings _settings;
        private readonly PTLRepository _repo;
        private readonly PTLFrameBuilder _frameBuilder;
        private readonly PTLResponseParser _parser;
        private readonly ILogger<PTLWorker> _logger;

        // connection state
        private TcpClient? _client;

        private NetworkStream? _networkStream;
        private readonly object _connectionLock = new();
        private readonly ConcurrentDictionary<string, string> _locationColors = new();

        private readonly Stopwatch _clearCacheStopwatch = Stopwatch.StartNew();
        private int _ptlMessageNumber = 0;

        public PTLWorker(IOptions<PTLSettings> settings, PTLRepository repo, PTLFrameBuilder frameBuilder, PTLResponseParser parser, ILogger<PTLWorker> logger)
        {
            _settings = settings.Value;
            _repo = repo;
            _frameBuilder = frameBuilder;
            _parser = parser;
            _logger = logger;
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("PTLWorker starting");
            return base.StartAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // start connection maintenance loop
            _ = Task.Run(() => MaintainConnectionWithPTLServer(stoppingToken), stoppingToken);

            // start periodic tasks loops
            _ = Task.Run(() => PeriodicCheckJlStatus(stoppingToken), stoppingToken);
            _ = Task.Run(() => PeriodicUpdateJlStatus(stoppingToken), stoppingToken);

            // keep the worker alive until cancellation
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }

        private async Task MaintainConnectionWithPTLServer(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    lock (_connectionLock)
                    {
                        if (_client != null && _client.Connected)
                        {
                            // already connected
                        }
                    }

                    if (_client == null || !_client.Connected)
                    {
                        DisposeConnection();

                        _client = new TcpClient();
                        var connectTask = _client.ConnectAsync(_settings.Ip, _settings.Port);
                        var timeout = Task.Delay(TimeSpan.FromSeconds(10), token);

                        var completed = await Task.WhenAny(connectTask, timeout);
                        if (completed != connectTask)
                        {
                            _logger.LogWarning("Timeout connecting to PTL server at {Ip}:{Port}", _settings.Ip, _settings.Port);
                            _client.Dispose();
                            _client = null;
                        }
                        else
                        {
                            _networkStream = _client.GetStream();
                            _logger.LogInformation("Connected to PTL server at {Ip}:{Port}", _settings.Ip, _settings.Port);
                            // start listener
                            _ = Task.Run(() => ListenToServer(token), token);
                        }
                    }

                    await Task.Delay(TimeSpan.FromSeconds(1), token);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error connecting to PTL server");
                    await Task.Delay(TimeSpan.FromSeconds(_settings.ReconnectIntervalSeconds), token);
                }
            }
        }

        private async Task ListenToServer(CancellationToken token)
        {
            try
            {
                var buffer = new byte[4096];

                while (!token.IsCancellationRequested && _client != null && _client.Connected)
                {
                    try
                    {
                        var bytesRead = await _networkStream!.ReadAsync(buffer.AsMemory(0, buffer.Length), token);
                        if (bytesRead > 0)
                        {
                            var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                            _logger.LogInformation("Received from PTL: {Response}", response);
                            await ParseServerResponse(response);
                        }
                        else
                        {
                            _logger.LogError("Server closed the connection. Exiting listener loop.");
                            break;
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error reading from PTL server");
                        break;
                    }
                }
            }
            finally
            {
                _logger.LogInformation("Stopped listening to server responses.");
                DisposeConnection();
            }
        }

        private void DisposeConnection()
        {
            try
            {
                _networkStream?.Dispose();
                _networkStream = null;
                _client?.Dispose();
                _client = null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing PTL connection");
            }
        }

        private async Task PeriodicCheckJlStatus(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // clear cache periodically
                    if (_clearCacheStopwatch.Elapsed.TotalSeconds >= _settings.ClearCacheIntervalSeconds)
                    {
                        _locationColors.Clear();
                        _clearCacheStopwatch.Restart();
                        _logger.LogInformation("Cleared PTL location color cache.");
                    }

                    await CheckJlStatus();
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in PeriodicCheckJlStatus");
                }

                await Task.Delay(TimeSpan.FromSeconds(_settings.CheckStatusIntervalSeconds), token);
            }
        }

        private async Task PeriodicUpdateJlStatus(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await UpdateReadyToPack();
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in PeriodicUpdateJlStatus");
                }

                await Task.Delay(TimeSpan.FromSeconds(_settings.UpdateStatusIntervalSeconds), token);
            }
        }

        private async Task UpdateReadyToPack()
        {
            var rows = await _repo.GetJlsToPackAsync();
            foreach (var row in rows)
            {
                try
                {
                    if (row.Status is int status && status == 1)
                    {
                        // JL might be returned as KuwetaId -> translate with GetJlId if needed
                        var jlIdValue = row.KuwetaId?.ToString();
                        await UpdateStatus(jlIdValue, "GotowaDoPakowania", "Sorter_Gotowa", row.Kurier?.ToString());
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling KuwetyDoSpakowania row");
                }
            }

            var redRows = await _repo.GetRedJlsToPack();
            foreach (var row in redRows)
            {
                try
                {
                    if (row.Status is int status && status != 1)
                    {
                        var jlIdValue = row.KuwetaId?.ToString();
                        await UpdateStatus(jlIdValue, "NieGotowaDoPakowania", "Sorter_Pusta");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling KuwetyDoSpakowaniaCzerwone row");
                }
            }
        }

        private async Task CheckJlStatus()
        {
            var rows = await _repo.GetPTLStatusRowsAsync();
            foreach (var r in rows)
            {
                string jlCode = r.ptl_jlCode?.ToString() ?? string.Empty;
                string? operation = r.ptl_operation?.ToString();
                string? color = r.ptl_color?.ToString();
                string? text = r.ptl_text?.ToString();

                if (string.IsNullOrEmpty(jlCode) || string.IsNullOrEmpty(operation))
                    continue;

                // create messageNumber & locationReference as old code did
                string messageNumber = _frameBuilder.GetNextMessageNumber();
                // locationReference is original code: "1" + GetNumbersFromText(jlCode).Replace("0", "")
                string locationReference = "1" + GetNumbersFromText(jlCode).Replace("0", "");

                if (!_locationColors.TryGetValue(jlCode, out var currentOperation) || currentOperation != operation)
                {
                    string frame = _frameBuilder.CreatePTLFrame(messageNumber, locationReference, operation, text, color);
                    await SendFrameToPTLServer(frame);

                    _locationColors.AddOrUpdate(jlCode, operation, (_, __) => operation);
                    _logger.LogInformation("Sent PTL frame for {JlCode} operation {Op}", jlCode, operation);
                    await Task.Delay(100); // slight delay to avoid overwhelming the server
                }
            }
        }

        private async Task UpdateStatus(string jlIdOrCode, string value, string plcStatus, string? text = null)
        {
            if (string.IsNullOrEmpty(jlIdOrCode))
            {
                _logger.LogWarning("UpdateStatus called with null or empty jlIdOrCode");
                return;
            }

            try
            {
                string jlIdStr = jlIdOrCode;
                if (jlIdOrCode.Length > 4)
                {
                    var maybe = await _repo.GetJlIdByCodeAsync(jlIdOrCode);
                    if (!string.IsNullOrEmpty(maybe))
                        jlIdStr = maybe;
                }

                if (!int.TryParse(jlIdStr, out int jlId))
                {
                    _logger.LogWarning("Cannot parse jlId {JlIdStr}", jlIdStr);
                    return;
                }

                bool ok = await _repo.UpdateStatusAsync(jlId, value, text, plcStatus);
                if (!ok)
                    _logger.LogWarning("No rows affected after trying to update attribute on jlId: {JlId}", jlId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in UpdateStatus for {JlIdOrCode}", jlIdOrCode);
            }
        }

        private async Task SendFrameToPTLServer(string frame)
        {
            if (string.IsNullOrEmpty(frame))
            {
                _logger.LogWarning("Cannot send empty frame.");
                return;
            }

            try
            {
                if (_client != null && _client.Connected && _networkStream != null)
                {
                    string frameWithControls = PTLFrameBuilder.ToControlChars(frame);
                    byte[] data = Encoding.ASCII.GetBytes(frameWithControls);
                    await _networkStream.WriteAsync(data, 0, data.Length);
                    _logger.LogInformation("Sent frame to PTL server: {Frame}", frame);
                }
                else
                {
                    _logger.LogWarning("Cannot send frame: Not connected to PTL server.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while sending frame to PTL server");
            }
        }

        private async Task ParseServerResponse(string response)
        {
            try
            {
                var parsed = _parser.Parse(response);
                if (parsed == null)
                    return;

                string messageType = parsed.Value.MessageType;
                string sequence = parsed.Value.Sequence;

                if (messageType.Contains("C")) // Confirmation
                {
                    string locationReference = parsed.Value.LocationReference ?? "Unknown";
                    string text = parsed.Value.Text ?? string.Empty;
                    await HandleConfirmation(locationReference);
                }
                else
                {
                    string errorCode = parsed.Value.ErrorCode ?? "Unknown";
                    _parser.LogErrorCode(errorCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing server response");
            }
        }

        private async Task HandleConfirmation(string locationNumber)
        {
            try
            {
                // Convert incoming location to JL code using your original pattern:
                string jl = GenerateJlFromLocation(locationNumber);
                _logger.LogInformation("Handling confirmation for location: {Location}, JL: {Jl}", locationNumber, jl);
                string operation = await _repo.GetJlStatusAsync(jl) ?? string.Empty;
                string messageNumber = _frameBuilder.GetNextMessageNumber();
                string locationReference = locationNumber;
                string jlPLCStatus = string.Empty;
                string jlStatus = string.Empty;

                switch (operation)
                {
                    case "GotowaDoPakowania":
                        jlStatus = "Pakowana";
                        jlPLCStatus = "Pakowanie";
                        break;

                    case "PustaNieprzypisana":
                        jlStatus = "PustaPrzypisana";
                        jlPLCStatus = "Sorter_Pusta";
                        break;

                    case "Rozsortowywana":
                        jlStatus = "PustaPrzypisana";
                        jlPLCStatus = "Sorter_Pusta";
                        break;

                    default:
                        jlStatus = string.Empty;
                        break;
                }

                if (!string.IsNullOrEmpty(jlStatus))
                {
                    string frame = _frameBuilder.CreatePTLFrame(messageNumber, locationReference, jlStatus);
                    // update DB and send frame to PTL
                    await UpdateStatus(jl, jlStatus, jlPLCStatus);
                    await SendFrameToPTLServer(frame);

                    _locationColors.AddOrUpdate(jl, jlStatus, (_, __) => jlStatus);
                    _logger.LogInformation("Confirmed jl: {Jl}", jl);
                }
                else
                {
                    _logger.LogWarning("Got a confirmation response, but JL doesn't have attribute 'Sorter_Gotowa' or 'Sorter'. Current JL attribute: {Operation}", operation);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in HandleConfirmation");
            }
        }

        private static string GetNumbersFromText(string input)
        {
            return new string(input.Where(char.IsDigit).ToArray());
        }

        private void IncrementFrameNumber()
        {
            _ptlMessageNumber++;
            if (_ptlMessageNumber > 9999) _ptlMessageNumber = 1;
        }

        private string GenerateJlFromLocation(string jl)
        {
            if (string.IsNullOrEmpty(jl) || jl.Length < 4) return jl;
            jl = jl.Substring(1);
            return $"KS-00{jl.Substring(0, 1)}-00{jl.Substring(1, 1)}-00{jl.Substring(2, 1)}";
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("PTLWorker stopping...");
            DisposeConnection();
            return base.StopAsync(cancellationToken);
        }
    }
}