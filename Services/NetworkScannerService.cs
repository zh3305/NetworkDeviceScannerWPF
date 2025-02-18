using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetworkDeviceScannerWPF.Models;
using NetworkDeviceScannerWPF.Services.NetworkScanner;
using NetworkDeviceScannerWPF.Services;
using System.Threading;
using System.Linq;

namespace NetworkDeviceScannerWPF.Services
{
    public class NetworkScannerService
    {
        private readonly ArpScannerService _arpScanner;
        private readonly SnmpScannerService _snmpScanner;
        private readonly MdnsScannerService _mdnsScanner;
        private readonly SsdpScannerService _ssdpScanner;
        private readonly MacLookupService _macLookupService;
        private readonly ILogger<NetworkScannerService> _logger;
        private const int MAX_CONCURRENT_TASKS = 20;

        public NetworkScannerService(
            ILogger<NetworkScannerService> logger,
            ArpScannerService arpScanner,
            SnmpScannerService snmpScanner,
            MdnsScannerService mdnsScanner,
            SsdpScannerService ssdpScanner,
            MacLookupService macLookupService)
        {
            _logger = logger;
            _arpScanner = arpScanner;
            _snmpScanner = snmpScanner;
            _mdnsScanner = mdnsScanner;
            _ssdpScanner = ssdpScanner;
            _macLookupService = macLookupService;
        }

        public List<NetworkInterface> GetNetworkInterfaces()
        {
            var interfaces = new List<NetworkInterface>();
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus == OperationalStatus.Up &&
                    (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                     nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet))
                {
                    interfaces.Add(nic);
                }
            }
            return interfaces;
        }

        public async Task ScanNetworkAsync(NetworkInterface networkInterface, Action<NetworkDevice> onDeviceFound, CancellationToken cancellationToken = default)
        {
            try
            {
                // ARP扫描
                _logger.LogInformation("开始ARP扫描");
                cancellationToken.ThrowIfCancellationRequested();
                var arpDevices = await _arpScanner.ScanNetworkAsync(networkInterface, cancellationToken);
                foreach (var device in arpDevices)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!string.IsNullOrEmpty(device.MAC))
                    {
                        device.Manufacturer = await _macLookupService.GetManufacturerAsync(device.MAC);
                    }
                    onDeviceFound(device);
                }

                // 并行执行SNMP扫描
                _logger.LogInformation("开始SNMP扫描");
                cancellationToken.ThrowIfCancellationRequested();
                using (var semaphore = new SemaphoreSlim(MAX_CONCURRENT_TASKS))
                {
                    var snmpTasks = arpDevices.Select(async device =>
                    {
                        try
                        {
                            await semaphore.WaitAsync(cancellationToken);
                            _logger.LogDebug($"正在SNMP扫描 {device.IP}");
                            cancellationToken.ThrowIfCancellationRequested();
                            var snmpDevice = await _snmpScanner.ScanDeviceAsync(device.IP);
                            if (snmpDevice != null)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                device.Name = snmpDevice.Name;
                                device.Location = snmpDevice.Location;
                                device.DiscoveryMethod = string.Join(",", 
                                    new HashSet<string>(
                                        (device.DiscoveryMethod + ",SNMP")
                                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                                    )
                                );
                                onDeviceFound(device);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    });

                    try
                    {
                        await Task.WhenAll(snmpTasks);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                }

                // 并行执行mDNS和SSDP扫描
                _logger.LogInformation("开始mDNS和SSDP扫描");
                var mdnsTask = Task.Run(async () =>
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var mdnsDevices = await _mdnsScanner.ScanAsync();
                        foreach (var device in mdnsDevices)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            _logger.LogDebug($"发现mDNS设备 {device.IP}");
                            onDeviceFound(device);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "mDNS扫描失败");
                    }
                }, cancellationToken);

                var ssdpTask = Task.Run(async () =>
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var ssdpDevices = await _ssdpScanner.ScanAsync();
                        foreach (var device in ssdpDevices)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            _logger.LogDebug($"发现SSDP设备 {device.IP}");
                            onDeviceFound(device);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "SSDP扫描失败");
                    }
                }, cancellationToken);

                // 等待所有扫描完成
                await Task.WhenAll(mdnsTask, ssdpTask);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("扫描已取消");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "扫描过程中发生错误");
                throw;
            }
        }
    }
} 