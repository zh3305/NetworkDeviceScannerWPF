using System;
using System.Collections.Generic;
using System.Management;
using System.Net.NetworkInformation;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Linq;

namespace NetworkDeviceScannerWPF.Services
{
    public class MacLookupService
    {
        private readonly ILogger<MacLookupService> _logger;
        private readonly Dictionary<string, string> _cache = new();

        public MacLookupService(ILogger<MacLookupService> logger)
        {
            _logger = logger;
            // 预加载本地网卡信息
            LoadLocalAdaptersInfo();
        }

        private void LoadLocalAdaptersInfo()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_NetworkAdapter WHERE PhysicalAdapter=True");
                
                foreach (ManagementObject obj in searcher.Get())
                {
                    var mac = obj["MACAddress"]?.ToString()?.Replace(":", "").Replace("-", "").ToUpper();
                    if (!string.IsNullOrEmpty(mac))
                    {
                        var description = obj["Description"]?.ToString() ?? string.Empty;
                        var manufacturer = obj["Manufacturer"]?.ToString() ?? string.Empty;
                        var adapterType = obj["AdapterType"]?.ToString() ?? string.Empty;
                        var productName = obj["ProductName"]?.ToString() ?? string.Empty;
                        var caption = obj["Caption"]?.ToString() ?? string.Empty;

                        var info = new List<string>();
                        if (!string.IsNullOrEmpty(manufacturer) && !manufacturer.Contains("Microsoft")) 
                            info.Add(manufacturer);
                        if (!string.IsNullOrEmpty(productName)) 
                            info.Add(productName);
                        if (!string.IsNullOrEmpty(adapterType)) 
                            info.Add(adapterType);
                        if (!string.IsNullOrEmpty(description) && 
                            !description.Equals(manufacturer) && 
                            !description.Equals(productName) &&
                            !description.Contains("Microsoft"))
                        {
                            info.Add(description);
                        }

                        var deviceInfo = string.Join(" - ", info.Distinct());
                        if (!string.IsNullOrEmpty(deviceInfo))
                        {
                            _cache[mac] = deviceInfo;
                            _logger.LogDebug($"已缓存设备信息: {mac} -> {deviceInfo}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加载本地网卡信息失败");
            }
        }

        public async Task<string> GetManufacturerAsync(string macAddress)
        {
            if (string.IsNullOrEmpty(macAddress))
                return string.Empty;

            // 标准化MAC地址格式
            macAddress = macAddress.Replace(":", "").Replace("-", "").ToUpper();
            if (macAddress.Length < 12)
                return string.Empty;

            try
            {
                // 检查缓存
                if (_cache.TryGetValue(macAddress, out var cachedInfo))
                {
                    return cachedInfo;
                }

                // 尝试通过不同的WMI类获取设备信息
                var deviceInfo = await Task.Run(() => GetDeviceInfoFromMultipleSources(macAddress));
                if (!string.IsNullOrEmpty(deviceInfo))
                {
                    _cache[macAddress] = deviceInfo;
                    return deviceInfo;
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"获取设备信息失败: {macAddress}");
                return string.Empty;
            }
        }

        private string GetDeviceInfoFromMultipleSources(string macAddress)
        {
            var info = new List<string>();

            try
            {
                // 尝试从Win32_NetworkAdapter获取信息
                using (var searcher = new ManagementObjectSearcher(
                    $"SELECT * FROM Win32_NetworkAdapter WHERE MACAddress = '{FormatMacForWmi(macAddress)}'"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        var description = obj["Description"]?.ToString();
                        var manufacturer = obj["Manufacturer"]?.ToString();
                        var adapterType = obj["AdapterType"]?.ToString();
                        
                        if (!string.IsNullOrEmpty(manufacturer) && !manufacturer.Contains("Microsoft")) 
                            info.Add(manufacturer);
                        if (!string.IsNullOrEmpty(adapterType)) 
                            info.Add(adapterType);
                        if (!string.IsNullOrEmpty(description) && !description.Contains("Microsoft"))
                            info.Add(description);
                    }
                }

                // 尝试从Win32_NetworkAdapterConfiguration获取额外信息
                using (var searcher = new ManagementObjectSearcher(
                    $"SELECT * FROM Win32_NetworkAdapterConfiguration WHERE MACAddress = '{FormatMacForWmi(macAddress)}'"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        var description = obj["Description"]?.ToString();
                        if (!string.IsNullOrEmpty(description) && 
                            !description.Contains("Microsoft") && 
                            !info.Contains(description))
                        {
                            info.Add(description);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WMI查询失败");
            }

            return string.Join(" - ", info.Distinct().Where(i => !string.IsNullOrWhiteSpace(i)));
        }

        private string FormatMacForWmi(string macAddress)
        {
            return string.Join(":", Enumerable.Range(0, 6)
                .Select(i => macAddress.Substring(i * 2, 2)));
        }
    }
} 