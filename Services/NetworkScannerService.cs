using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetworkDeviceScannerWPF.Models;
using NetworkDeviceScannerWPF.Services.NetworkScanner;
using NetworkDeviceScannerWPF.Services;

namespace NetworkDeviceScannerWPF.Services
{
    public class NetworkScannerService
    {
        private readonly ArpScannerService _arpScanner;
        private readonly SnmpScannerService _snmpScanner;
        private readonly MdnsScannerService _mdnsScanner;
        private readonly SsdpScannerService _ssdpScanner;
        private readonly ILogger<NetworkScannerService> _logger;

        public NetworkScannerService(
            ILogger<NetworkScannerService> logger,
            ArpScannerService arpScanner,
            SnmpScannerService snmpScanner,
            MdnsScannerService mdnsScanner,
            SsdpScannerService ssdpScanner)
        {
            _logger = logger;
            _arpScanner = arpScanner;
            _snmpScanner = snmpScanner;
            _mdnsScanner = mdnsScanner;
            _ssdpScanner = ssdpScanner;
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

        public async Task<List<NetworkDevice>> ScanNetworkAsync(NetworkInterface networkInterface)
        {
            var devices = new List<NetworkDevice>();
            
            try
            {
                // ARP扫描
                var arpDevices = await _arpScanner.ScanNetworkAsync(networkInterface);
                devices.AddRange(arpDevices);

                // SNMP扫描
                foreach (var device in devices.ToList())
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
                    var existingDevice = devices.Find(d => d.IP == mdnsDevice.IP);
                    if (existingDevice != null)
                    {
                        existingDevice.Name = mdnsDevice.Name;
                        existingDevice.DiscoveryMethod += ",mDNS";
                    }
                    else
                    {
                        devices.Add(mdnsDevice);
                    }
                }

                // SSDP扫描
                var ssdpDevices = await _ssdpScanner.ScanAsync();
                foreach (var ssdpDevice in ssdpDevices)
                {
                    var existingDevice = devices.Find(d => d.IP == ssdpDevice.IP);
                    if (existingDevice != null)
                    {
                        existingDevice.Name = string.IsNullOrEmpty(existingDevice.Name) ? ssdpDevice.Name : existingDevice.Name;
                        existingDevice.CustomName = string.IsNullOrEmpty(existingDevice.CustomName) ? ssdpDevice.CustomName : existingDevice.CustomName;
                        existingDevice.Location = string.IsNullOrEmpty(existingDevice.Location) ? ssdpDevice.Location : existingDevice.Location;
                        existingDevice.DiscoveryMethod += ",SSDP";
                    }
                    else
                    {
                        devices.Add(ssdpDevice);
                    }
                }
            }
            catch (Exception)
            {
                throw;
            }

            return devices;
        }
    }
} 