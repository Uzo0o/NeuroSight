using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace NeuroSight.Services
{
    public class VmixTcpService
    {
        private string _host = "127.0.0.1";
        private int _port = 8099;
        private bool _enabled;

        public void Configure(string host, int port, bool enabled)
        {
            _host = host;
            _port = port;
            _enabled = enabled;
        }

        public async Task<bool> SendTextAsync(string inputName, string text)
        {
            if (!_enabled) return false;
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(_host, _port);
                using var stream = client.GetStream();
                using var writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true };

                // vMix TCP API: FUNCTION SetText Input=Timer&Value=12:34
                string cmd = $"FUNCTION SetText Input={Uri.EscapeDataString(inputName)}&Value={Uri.EscapeDataString(text)}\r\n";
                await writer.WriteAsync(cmd);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"vMix send failed: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> SendTitleAsync(string inputName, string fieldName, string text)
        {
            if (!_enabled) return false;
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(_host, _port);
                using var stream = client.GetStream();
                using var writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true };

                // For GT titles: FUNCTION SetText Input=Score&SelectedName=HomeScore&Value=24
                string cmd = $"FUNCTION SetText Input={Uri.EscapeDataString(inputName)}&SelectedName={Uri.EscapeDataString(fieldName)}&Value={Uri.EscapeDataString(text)}\r\n";
                await writer.WriteAsync(cmd);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"vMix title send failed: {ex.Message}");
                return false;
            }
        }
    }
}
