using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using NetworkDeviceScannerWPF.Models;
using System.Net.Http;
using System.Xml;

namespace NetworkDeviceScannerWPF.Services.NetworkScanner
{
    public class SsdpScannerService
    {
        private const string SSDP_ADDR = "239.255.255.250";
        private const int SSDP_PORT = 1900;
        private const string DISCOVERY_MESSAGE = @"M-SEARCH * HTTP/1.1
HOST: 239.255.255.250:1900
MAN: ""ssdp:discover""
MX: 3
ST: ssdp:all

";
        private readonly List<NetworkDevice> _discoveredDevices = new();
        private readonly HttpClient _httpClient;

        public SsdpScannerService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(5);
        }

        public async Task<List<NetworkDevice>> ScanAsync()
        {
            using var udpClient = new UdpClient();
            udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            
            try
            {
                // 发送SSDP发现消息
                var endpoint = new IPEndPoint(IPAddress.Parse(SSDP_ADDR), SSDP_PORT);
                var messageBytes = Encoding.ASCII.GetBytes(DISCOVERY_MESSAGE);
                await udpClient.SendAsync(messageBytes, messageBytes.Length, endpoint);

                // 设置接收超时
                udpClient.Client.ReceiveTimeout = 5000;

                var startTime = DateTime.Now;
                while (DateTime.Now - startTime < TimeSpan.FromSeconds(5))
                {
                    try
                    {
                        var result = await udpClient.ReceiveAsync();
                        var response = Encoding.ASCII.GetString(result.Buffer);
                        await ProcessSsdpResponseAsync(response, result.RemoteEndPoint);
                    }
                    catch (SocketException)
                    {
                        // 超时，继续循环
                        continue;
                    }
                }
            }
            finally
            {
                udpClient.Close();
            }

            return _discoveredDevices;
        }

        private async Task ProcessSsdpResponseAsync(string response, IPEndPoint remoteEndPoint)
        {
            try
            {
                var lines = response.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                string location = null;
                
                foreach (var line in lines)
                {
                    if (line.StartsWith("LOCATION:", StringComparison.OrdinalIgnoreCase))
                    {
                        location = line.Substring("LOCATION:".Length).Trim();
                        break;
                    }
                }

                if (string.IsNullOrEmpty(location))
                    return;

                var deviceInfo = await GetDeviceInfoAsync(location);
                if (deviceInfo != null)
                {
                    lock (_discoveredDevices)
                    {
                        if (!_discoveredDevices.Exists(d => d.IP == remoteEndPoint.Address.ToString()))
                        {
                            _discoveredDevices.Add(deviceInfo);
                        }
                    }
                }
            }
            catch
            {
                // 忽略解析错误
            }
        }

        private async Task<NetworkDevice> GetDeviceInfoAsync(string location)
        {
            try
            {
                var response = await _httpClient.GetStringAsync(location);
                var xml = XDocument.Parse(response);
                var nsManager = new XmlNamespaceManager(new NameTable());
                nsManager.AddNamespace("upnp", "urn:schemas-upnp-org:device-1-0");

                var device = new NetworkDevice
                {
                    IP = new Uri(location).Host,
                    IsOnline = true,
                    LastSeen = DateTime.Now,
                    DiscoveryMethod = "SSDP"
                };

                var deviceElement = xml.Root?.Element("device");
                if (deviceElement != null)
                {
                    device.Name = deviceElement.Element("friendlyName")?.Value;
                    device.CustomName = deviceElement.Element("modelName")?.Value;
                    device.Location = deviceElement.Element("modelDescription")?.Value;
                }

                return device;
            }
            catch
            {
                return null;
            }
        }
    }
} 