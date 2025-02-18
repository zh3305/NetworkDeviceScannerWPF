using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using NetworkDeviceScannerWPF.Models;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace NetworkDeviceScannerWPF.Services.NetworkScanner
{
    public class ArpScannerService
    {
        private readonly ConcurrentDictionary<string, NetworkDevice> _discoveredDevices = new();
        private readonly ILogger<ArpScannerService> _logger;
        private const int MAX_CONCURRENT_TASKS = 100;

        public ArpScannerService(ILogger<ArpScannerService> logger)
        {
            _logger = logger;
        }

        public async Task<List<NetworkDevice>> ScanNetworkAsync(NetworkInterface networkInterface, CancellationToken cancellationToken = default)
        {
            var interfaceProperties = networkInterface.GetIPProperties();
            var ipv4Address = interfaceProperties.UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.Address;
            
            if (ipv4Address == null)
                throw new Exception("所选网卡没有IPv4地址");

            var subnet = GetSubnetMask(interfaceProperties);
            if (subnet == null)
                throw new Exception("无法获取子网掩码");

            _logger.LogInformation($"开始扫描网络: {ipv4Address}/{subnet}");

            var network = GetNetworkAddress(ipv4Address, subnet);
            var broadcast = GetBroadcastAddress(ipv4Address, subnet);
            var startAddress = network.GetAddressBytes();
            var endAddress = broadcast.GetAddressBytes();

            // 计算IP范围
            var ipRange = new List<IPAddress>();
            for (var i = startAddress[3] + 1; i < endAddress[3]; i++)
            {
                ipRange.Add(new IPAddress(new byte[] { startAddress[0], startAddress[1], startAddress[2], (byte)i }));
            }

            _logger.LogInformation($"需要扫描的IP数量: {ipRange.Count}");

            // 使用SemaphoreSlim控制并发数
            using var semaphore = new SemaphoreSlim(MAX_CONCURRENT_TASKS);
            var tasks = ipRange.Select(async ip =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await PingHostAsync(ip, cancellationToken);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("ARP扫描已取消");
                throw;
            }

            _logger.LogInformation("Ping扫描完成，开始获取ARP缓存");

            // 获取ARP缓存
            var arpEntries = GetArpEntries();
            foreach (var entry in arpEntries)
            {
                if (_discoveredDevices.TryGetValue(entry.Key, out var device))
                {
                    device.MAC = entry.Value;
                }
                else
                {
                    _discoveredDevices.TryAdd(entry.Key, new NetworkDevice
                    {
                        IP = entry.Key,
                        MAC = entry.Value,
                        IsOnline = true,
                        LastSeen = DateTime.Now,
                        DiscoveryMethod = "ARP"
                    });
                }
            }

            _logger.LogInformation("开始解析主机名");

            // 并行解析主机名
            var devices = _discoveredDevices.Values.ToList();
            var nameResolutionTasks = devices.Select(async device =>
            {
                try
                {
                    var hostName = await GetDeviceNameAsync(device.IP);
                    device.Name = hostName;
                    _logger.LogDebug($"解析主机名成功: {device.IP} -> {device.Name}");
                }
                catch
                {
                    device.Name = $"Unknown Device ({device.IP})";
                    _logger.LogDebug($"解析主机名失败: {device.IP}");
                }
            });

            await Task.WhenAll(nameResolutionTasks);

            _logger.LogInformation($"扫描完成，发现 {devices.Count} 个设备");
            return devices;
        }

        private async Task<string> GetDeviceNameAsync(string ipAddress)
        {
            try
            {
                // 1. 尝试DNS反向解析
                var hostEntry = await Dns.GetHostEntryAsync(ipAddress);
                if (!string.IsNullOrEmpty(hostEntry.HostName))
                {
                    return hostEntry.HostName;
                }

                // 2. 尝试NetBIOS名称解析
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "nbtstat",
                        Arguments = $"-A {ipAddress}",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                var match = System.Text.RegularExpressions.Regex.Match(output, @"(\S+)\s+<00>");
                if (match.Success)
                {
                    return match.Groups[1].Value.Trim();
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"获取设备名称失败: {ipAddress}, 原因: {ex.Message}");
            }

            return string.Empty;
        }

        private async Task PingHostAsync(IPAddress address, CancellationToken cancellationToken)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(address, 1000);
                if (reply.Status == IPStatus.Success)
                {
                    _logger.LogDebug($"Ping成功: {address}");
                    var deviceName = await GetDeviceNameAsync(address.ToString());
                    _discoveredDevices.TryAdd(address.ToString(), new NetworkDevice
                    {
                        IP = address.ToString(),
                        Name = deviceName,
                        IsOnline = true,
                        LastSeen = DateTime.Now,
                        DiscoveryMethod = "ARP"
                    });
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug($"Ping失败: {address}, 原因: {ex.Message}");
            }
        }

        private Dictionary<string, string> GetArpEntries()
        {
            var entries = new Dictionary<string, string>();
            try
            {
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "arp",
                        Arguments = "-a",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                var lines = output.Split('\n');
                foreach (var line in lines)
                {
                    var parts = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && IPAddress.TryParse(parts[0], out _))
                    {
                        entries[parts[0]] = parts[1].Replace("-", ":");
                        _logger.LogDebug($"ARP缓存: {parts[0]} -> {parts[1]}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("获取ARP缓存失败", ex);
            }
            return entries;
        }

        private static IPAddress GetSubnetMask(IPInterfaceProperties properties)
        {
            return properties.UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
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