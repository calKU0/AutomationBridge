using AutomationBridge.PLCWorker.Logic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace AutomationBridge.PLCWorker.Services
{
    public class PlcClientHandler
    {
        private readonly TcpClient _client;
        private readonly FrameProcessor _processor;
        private readonly ILogger _logger;

        public PlcClientHandler(TcpClient client, FrameProcessor processor, ILogger logger)
        {
            _client = client;
            _processor = processor;
            _logger = logger;
        }

        public async Task HandleClientAsync(CancellationToken token)
        {
            using (_client)
            {
                NetworkStream stream = _client.GetStream();
                _logger.LogInformation("Handling new PLC client...");

                var keepAliveTask = _processor.KeepAliveAsync(stream, _client, token);
                var receiveTask = ProcessIncomingFramesAsync(stream, token);

                await Task.WhenAny(keepAliveTask, receiveTask);
                _logger.LogInformation("Client handler finished.");
            }
        }

        private async Task ProcessIncomingFramesAsync(NetworkStream stream, CancellationToken token)
        {
            byte[] buffer = new byte[256];
            while (!token.IsCancellationRequested)
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token);
                if (bytesRead == 0) break;

                string frame = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                _logger.LogInformation("Received frame: {Frame}", frame);

                await _processor.ProcessFrameAsync(frame, stream, token);
            }
        }
    }
}