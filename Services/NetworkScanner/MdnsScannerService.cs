using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using NetworkDeviceScannerWPF.Models;
using System.Linq;

namespace NetworkDeviceScannerWPF.Services.NetworkScanner
{
    public class MdnsScannerService
    {
        private const int MDNS_PORT = 5353;
        private const string MDNS_ADDRESS = "224.0.0.251";
        private readonly List<NetworkDevice> _discoveredDevices = new();
        private readonly byte[] _queryPacket;

        public MdnsScannerService()
        {
            // 构建标准的mDNS查询包
            _queryPacket = new byte[]
            {
                0x00, 0x00, // Transaction ID
                0x00, 0x00, // Flags
                0x00, 0x01, // Questions
                0x00, 0x00, // Answer RRs
                0x00, 0x00, // Authority RRs
                0x00, 0x00, // Additional RRs
                // Query
                0x09, (byte)'_', (byte)'s', (byte)'e', (byte)'r', (byte)'v', (byte)'i', (byte)'c', (byte)'e', (byte)'s',
                0x07, (byte)'_', (byte)'d', (byte)'n', (byte)'s', (byte)'-', (byte)'s', (byte)'d',
                0x04, (byte)'_', (byte)'u', (byte)'d', (byte)'p',
                0x05, (byte)'l', (byte)'o', (byte)'c', (byte)'a', (byte)'l',
                0x00, // null terminator
                0x00, 0x0c, // Type PTR
                0x00, 0x01  // Class IN
            };
        }

        public async Task<List<NetworkDevice>> ScanAsync()
        {
            using var udpClient = new UdpClient();
            udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            try
            {
                // 绑定到任意端口
                udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

                // 加入多播组
                udpClient.JoinMulticastGroup(IPAddress.Parse(MDNS_ADDRESS));

                // 发送mDNS查询
                var endpoint = new IPEndPoint(IPAddress.Parse(MDNS_ADDRESS), MDNS_PORT);
                await udpClient.SendAsync(_queryPacket, _queryPacket.Length, endpoint);

                // 设置接收超时
                var startTime = DateTime.Now;
                udpClient.Client.ReceiveTimeout = 1000; // 1秒超时

                while (DateTime.Now - startTime < TimeSpan.FromSeconds(5))
                {
                    try
                    {
                        var result = await udpClient.ReceiveAsync();
                        ProcessMdnsResponse(result.Buffer, result.RemoteEndPoint);
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
                try
                {
                    udpClient.DropMulticastGroup(IPAddress.Parse(MDNS_ADDRESS));
                }
                catch
                {
                    // 忽略清理错误
                }
            }

            return _discoveredDevices;
        }

        private void ProcessMdnsResponse(byte[] response, IPEndPoint remoteEndPoint)
        {
            try
            {
                // 简单处理：如果收到响应，就认为设备支持mDNS
                var device = new NetworkDevice
                {
                    IP = remoteEndPoint.Address.ToString(),
                    IsOnline = true,
                    LastSeen = DateTime.Now,
                    DiscoveryMethod = "mDNS",
                    Name = $"mDNS Device ({remoteEndPoint.Address})"
                };

                try
                {
                    // 尝试解析主机名
                    var hostEntry = Dns.GetHostEntry(remoteEndPoint.Address);
                    if (!string.IsNullOrEmpty(hostEntry.HostName))
                    {
                        device.Name = hostEntry.HostName;
                    }
                }
                catch
                {
                    // 忽略DNS解析错误
                }

                lock (_discoveredDevices)
                {
                    if (!_discoveredDevices.Exists(d => d.IP == device.IP))
                    {
                        _discoveredDevices.Add(device);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"处理mDNS响应时出错: {ex.Message}");
            }
        }
    }
} 