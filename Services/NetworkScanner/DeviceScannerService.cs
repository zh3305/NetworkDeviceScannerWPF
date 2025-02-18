using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using PacketDotNet;
using SharpPcap;
using SharpPcap.LibPcap;
using NetworkDeviceScannerWPF.Models;
using System.Linq;
using System.Net.Sockets;

namespace NetworkDeviceScannerWPF.Services.NetworkScanner
{
    public class DeviceScannerService
    {
        private readonly Dictionary<string, NetworkDevice> _discoveredDevices = new();
        private LibPcapLiveDevice _captureDevice;
        private readonly SnmpScannerService _snmpScanner;
        private readonly MdnsScannerService _mdnsScanner;
        private readonly SsdpScannerService _ssdpScanner;

        public DeviceScannerService()
        {
            _snmpScanner = new SnmpScannerService();
            _mdnsScanner = new MdnsScannerService();
            _ssdpScanner = new SsdpScannerService();
        }

        public async Task<List<NetworkDevice>> ScanNetworkAsync(NetworkInterface networkInterface)
        {
            var interfaceProperties = networkInterface.GetIPProperties();
            var ipv4Address = interfaceProperties.UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address;
            
            if (ipv4Address == null)
                throw new Exception("所选网卡没有IPv4地址");

            var subnet = GetSubnetMask(interfaceProperties);
            if (subnet == null)
                throw new Exception("无法获取子网掩码");

            // 初始化抓包设备
            InitializeCapture(networkInterface);

            try
            {
                // 发送ARP请求
                await ScanArpAsync(ipv4Address, subnet);
                
                // 等待接收响应
                await Task.Delay(2000);

                // SNMP扫描
                foreach (var device in _discoveredDevices.Values)
                {
                    var snmpDevice = await _snmpScanner.ScanDeviceAsync(device.IP);
                    if (snmpDevice != null)
                    {
                        device.Name = snmpDevice.Name;
                        device.Location = snmpDevice.Location;
                        device.DiscoveryMethod += ",SNMP";
                    }
                }

                // mDNS扫描
                var mdnsDevices = await _mdnsScanner.ScanAsync();
                foreach (var mdnsDevice in mdnsDevices)
                {
                    var existingDevice = _discoveredDevices.Values.FirstOrDefault(d => d.IP == mdnsDevice.IP);
                    if (existingDevice != null)
                    {
                        existingDevice.Name = mdnsDevice.Name;
                        existingDevice.DiscoveryMethod += ",mDNS";
                    }
                    else if (!string.IsNullOrEmpty(mdnsDevice.MAC))
                    {
                        _discoveredDevices[mdnsDevice.MAC] = mdnsDevice;
                    }
                }

                // SSDP扫描
                var ssdpDevices = await _ssdpScanner.ScanAsync();
                foreach (var ssdpDevice in ssdpDevices)
                {
                    var existingDevice = _discoveredDevices.Values.FirstOrDefault(d => d.IP == ssdpDevice.IP);
                    if (existingDevice != null)
                    {
                        existingDevice.Name = string.IsNullOrEmpty(existingDevice.Name) ? ssdpDevice.Name : existingDevice.Name;
                        existingDevice.CustomName = string.IsNullOrEmpty(existingDevice.CustomName) ? ssdpDevice.CustomName : existingDevice.CustomName;
                        existingDevice.Location = string.IsNullOrEmpty(existingDevice.Location) ? ssdpDevice.Location : existingDevice.Location;
                        existingDevice.DiscoveryMethod += ",SSDP";
                    }
                    else
                    {
                        _discoveredDevices[ssdpDevice.IP] = ssdpDevice;
                    }
                }

                return _discoveredDevices.Values.ToList();
            }
            finally
            {
                StopCapture();
            }
        }

        private void InitializeCapture(NetworkInterface networkInterface)
        {
            var devices = LibPcapLiveDeviceList.Instance;
            _captureDevice = devices.FirstOrDefault(d => d.Interface.FriendlyName == networkInterface.Name) 
                            ?? throw new Exception("找不到对应的抓包设备");

            _captureDevice.Open(DeviceModes.Promiscuous);
            _captureDevice.Filter = "arp";
            _captureDevice.OnPacketArrival += Device_OnPacketArrival;
            _captureDevice.StartCapture();
        }

        private void StopCapture()
        {
            if (_captureDevice != null && _captureDevice.Opened)
            {
                _captureDevice.StopCapture();
                _captureDevice.Close();
            }
        }

        private void Device_OnPacketArrival(object sender, PacketCapture e)
        {
            var rawPacket = e.GetPacket();
            var packet = Packet.ParsePacket(rawPacket.LinkLayerType, rawPacket.Data);
            var arpPacket = packet.Extract<ArpPacket>();

            if (arpPacket == null || arpPacket.Operation != ArpOperation.Response) 
                return;

            var macAddress = BitConverter.ToString(arpPacket.SenderHardwareAddress.GetAddressBytes()).Replace("-", ":");
            var ipAddress = arpPacket.SenderProtocolAddress.ToString();

            var device = new NetworkDevice
            {
                MAC = macAddress,
                IP = ipAddress,
                IsOnline = true,
                LastSeen = DateTime.Now,
                DiscoveryMethod = "ARP"
            };

            try
            {
                var hostEntry = Dns.GetHostEntry(ipAddress);
                device.Name = hostEntry.HostName;
            }
            catch
            {
                device.Name = $"Unknown Device ({ipAddress})";
            }

            lock (_discoveredDevices)
            {
                if (!_discoveredDevices.ContainsKey(macAddress))
                {
                    _discoveredDevices.Add(macAddress, device);
                }
                else
                {
                    _discoveredDevices[macAddress].IsOnline = true;
                    _discoveredDevices[macAddress].LastSeen = DateTime.Now;
                    _discoveredDevices[macAddress].IP = ipAddress;
                }
            }
        }

        private async Task ScanArpAsync(IPAddress sourceIP, IPAddress subnetMask)
        {
            var network = GetNetworkAddress(sourceIP, subnetMask);
            var broadcast = GetBroadcastAddress(sourceIP, subnetMask);
            var startAddress = network.GetAddressBytes();
            var endAddress = broadcast.GetAddressBytes();

            for (var i = startAddress[3] + 1; i < endAddress[3]; i++)
            {
                var targetIP = new IPAddress(new byte[] { startAddress[0], startAddress[1], startAddress[2], (byte)i });
                await SendArpRequest(targetIP);
                await Task.Delay(10); // 避免发送太快
            }
        }

        private async Task SendArpRequest(IPAddress targetIP)
        {
            var arpPacket = new ArpPacket(
                ArpOperation.Request,
                PhysicalAddress.Parse("000000000000"), // 目标MAC（未知）
                targetIP,
                _captureDevice.MacAddress,
                IPAddress.Parse(_captureDevice.Addresses[0].Addr.ToString()));

            var ethernetPacket = new EthernetPacket(
                _captureDevice.MacAddress,
                PhysicalAddress.Parse("FFFFFFFFFFFF"), // 广播
                EthernetType.Arp)
            {
                PayloadPacket = arpPacket
            };

            await Task.Run(() => _captureDevice.SendPacket(ethernetPacket));
        }

        private static IPAddress GetSubnetMask(IPInterfaceProperties properties)
        {
            return properties.UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                ?.IPv4Mask;
        }

        private static IPAddress GetNetworkAddress(IPAddress address, IPAddress subnetMask)
        {
            var ipBytes = address.GetAddressBytes();
            var maskBytes = subnetMask.GetAddressBytes();
            var networkBytes = new byte[4];

            for (var i = 0; i < 4; i++)
            {
                networkBytes[i] = (byte)(ipBytes[i] & maskBytes[i]);
            }

            return new IPAddress(networkBytes);
        }

        private static IPAddress GetBroadcastAddress(IPAddress address, IPAddress subnetMask)
        {
            var ipBytes = address.GetAddressBytes();
            var maskBytes = subnetMask.GetAddressBytes();
            var broadcastBytes = new byte[4];

            for (var i = 0; i < 4; i++)
            {
                broadcastBytes[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
            }

            return new IPAddress(broadcastBytes);
        }
    }
} 