using AutomationBridge.PLCWorker.Data;
using AutomationBridge.PLCWorker.Settings;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace AutomationBridge.PLCWorker.Logic
{
    public class FrameProcessor
    {
        private readonly ILogger<FrameProcessor> _logger;
        private readonly PLCSettings _plcSettings;
        private readonly DestinationResolver _resolver;
        private readonly Stopwatch _checkAlive = new();

        public FrameProcessor(ILogger<FrameProcessor> logger, IOptions<PLCSettings> options, DestinationResolver resolver)
        {
            _logger = logger;
            _plcSettings = options.Value;
            _resolver = resolver;
            _checkAlive.Start();
        }

        public async Task ProcessFrameAsync(string frame, NetworkStream stream, CancellationToken token)
        {
            if (frame.Contains("SCN"))
            {
                string cmdFrame = await CreateCMDFrame(frame);
                await SendFrameAsync(stream, cmdFrame, token);
                _logger.LogInformation("Sent CMD frame.");
            }
        }

        public async Task KeepAliveAsync(NetworkStream stream, TcpClient client, CancellationToken token)
        {
            int frameNumber = 0;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (_checkAlive.Elapsed >= TimeSpan.FromSeconds(_plcSettings.CheckAliveIntervalSeconds))
                    {
                        frameNumber++;
                        string wdgFrame = CreateWDGFrame(frameNumber.ToString("D3"));
                        await SendFrameAsync(stream, wdgFrame, token);
                        _checkAlive.Restart();
                    }
                    await Task.Delay(100, token);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("KeepAliveAsync error: {Error}", ex.Message);
            }
            finally
            {
                stream?.Dispose();
                client?.Close();
            }
        }

        private string CreateWDGFrame(string frameNumber)
        {
            string startChar = "<";
            string scannerNumber = "A001";
            string frameType = "WDG";
            string padding = new string(' ', 41);
            string endChar = ">";
            return $"{startChar}{scannerNumber}{frameType}{frameNumber}{padding}{endChar}";
        }

        private async Task<string> CreateCMDFrame(string frame)
        {
            string startChar = "<";
            string scannerNumber = frame.Substring(1, 4);
            string frameType = "CMD";
            string frameNumber = frame.Substring(8, 3);
            string dumpOccupancy = frame.Substring(15, 5);
            string packageNumber = frame.Substring(20, 120);
            string destination = await _resolver.ResolveDestination(packageNumber.Trim(), dumpOccupancy, scannerNumber);

            string cmdFrame = $"{startChar}{scannerNumber}{frameType}{frameNumber}{destination}{dumpOccupancy}{packageNumber}   >";
            return cmdFrame;
        }

        private async Task SendFrameAsync(NetworkStream stream, string frame, CancellationToken token)
        {
            byte[] data = Encoding.UTF8.GetBytes(frame);
            await stream.WriteAsync(data, 0, data.Length, token);
            _logger.LogInformation("Sent frame: {Frame}", frame);
        }
    }
}