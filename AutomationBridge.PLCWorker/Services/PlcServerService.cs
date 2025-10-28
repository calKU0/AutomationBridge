using AutomationBridge.PLCWorker.Logic;
using AutomationBridge.PLCWorker.Settings;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace AutomationBridge.PLCWorker.Services
{
    public class PlcServerService : BackgroundService
    {
        private readonly ILogger<PlcServerService> _logger;
        private readonly PLCSettings _plcSettings;
        private readonly FrameProcessor _processor;

        private TcpListener? _listener;

        public PlcServerService(ILogger<PlcServerService> logger, IOptions<PLCSettings> options, FrameProcessor processor)
        {
            _logger = logger;
            _plcSettings = options.Value;
            _processor = processor;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _listener = new TcpListener(IPAddress.Any, _plcSettings.Port);
            _listener.Start();
            _logger.LogInformation("PLC TCP Server started on port {Port}", _plcSettings.Port);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync(stoppingToken);
                    _logger.LogInformation("PLC client connected.");

                    var handler = new PlcClientHandler(client, _processor, _logger);
                    _ = Task.Run(() => handler.HandleClientAsync(stoppingToken), stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("PLC Server stopping...");
            }
            finally
            {
                _listener.Stop();
                _logger.LogInformation("PLC Server stopped.");
            }
        }
    }
}