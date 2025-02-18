using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using NetworkDeviceScannerWPF.Models;
using NetworkDeviceScannerWPF.Services;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using NetworkDeviceScannerWPF.Commands;

namespace NetworkDeviceScannerWPF.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly NetworkScannerService _scannerService;
        private readonly ILogger<MainViewModel> _logger;
        private ObservableCollection<NetworkDevice> _devices;
        private ObservableCollection<NetworkInterface> _networkInterfaces;
        private NetworkInterface _selectedInterface;
        private bool _isScanning;
        private string _statusText;
        private readonly string _csvFilePath = "devices.csv";

        public MainViewModel(NetworkScannerService scannerService, ILogger<MainViewModel> logger)
        {
            _scannerService = scannerService;
            _logger = logger;
            Devices = new ObservableCollection<NetworkDevice>();
            NetworkInterfaces = new ObservableCollection<NetworkInterface>();
            ScanCommand = new AsyncRelayCommand(StartScanAsync, () => !IsScanning);
            SaveCommand = new RelayCommand(SaveDevices);
            LoadNetworkInterfaces();
            LoadDevicesFromCsv();
        }

        public ObservableCollection<NetworkDevice> Devices
        {
            get => _devices;
            set
            {
                _devices = value;
                OnPropertyChanged(nameof(Devices));
            }
        }

        public ObservableCollection<NetworkInterface> NetworkInterfaces
        {
            get => _networkInterfaces;
            set
            {
                _networkInterfaces = value;
                OnPropertyChanged(nameof(NetworkInterfaces));
            }
        }

        public NetworkInterface SelectedInterface
        {
            get => _selectedInterface;
            set
            {
                _selectedInterface = value;
                OnPropertyChanged(nameof(SelectedInterface));
            }
        }

        public bool IsScanning
        {
            get => _isScanning;
            set
            {
                _isScanning = value;
                OnPropertyChanged(nameof(IsScanning));
                OnPropertyChanged(nameof(ScanButtonText));
                (ScanCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public string StatusText
        {
            get => _statusText;
            set
            {
                _statusText = value;
                OnPropertyChanged(nameof(StatusText));
            }
        }

        public string ScanButtonText => IsScanning ? "扫描中..." : "开始扫描";

        public ICommand ScanCommand { get; }
        public ICommand SaveCommand { get; }

        private void LoadNetworkInterfaces()
        {
            NetworkInterfaces.Clear();
            var interfaces = _scannerService.GetNetworkInterfaces();
            foreach (var networkInterface in interfaces)
            {
                NetworkInterfaces.Add(networkInterface);
            }
            if (NetworkInterfaces.Count > 0)
            {
                SelectedInterface = NetworkInterfaces[0];
            }
        }

        private async Task StartScanAsync()
        {
            if (SelectedInterface == null)
            {
                StatusText = "请选择网络接口";
                return;
            }

            StatusText = "正在扫描...";

            try
            {
                var newDevices = await _scannerService.ScanNetworkAsync(SelectedInterface);

                Devices.Clear(); // 清除旧设备
                foreach (var device in newDevices)
                {
                    Devices.Add(device);
                }

                StatusText = $"扫描完成，发现 {newDevices.Count} 个设备";
                _logger.LogInformation($"扫描完成，发现 {newDevices.Count} 个设备");
            }
            catch (Exception ex)
            {
                StatusText = "扫描失败";
                _logger.LogError(ex, "扫描失败");
            }
        }

        private void SaveDevices()
        {
            try
            {
                var lines = new List<string>
                {
                    "Name,IP,MAC,IsOnline,CustomName,Location,LastSeen,DiscoveryMethod"
                };

                foreach (var device in Devices)
                {
                    lines.Add($"{device.Name},{device.IP},{device.MAC},{device.IsOnline}," +
                             $"{device.CustomName},{device.Location},{device.LastSeen}," +
                             $"{device.DiscoveryMethod}");
                }

                File.WriteAllLines(_csvFilePath, lines);
                StatusText = "设备信息已保存到CSV文件";
                _logger.LogInformation($"已保存 {Devices.Count} 个设备到 {_csvFilePath}");
            }
            catch (Exception ex)
            {
                StatusText = "保存失败";
                _logger.LogError(ex, "保存设备信息失败");
            }
        }

        private void LoadDevicesFromCsv()
        {
            if (!File.Exists(_csvFilePath)) return;

            try
            {
                var lines = File.ReadAllLines(_csvFilePath);
                foreach (var line in lines.Skip(1)) // Skip header
                {
                    var values = line.Split(',');
                    if (values.Length >= 8)
                    {
                        Devices.Add(new NetworkDevice
                        {
                            Name = values[0],
                            IP = values[1],
                            MAC = values[2],
                            IsOnline = bool.Parse(values[3]),
                            CustomName = values[4],
                            Location = values[5],
                            LastSeen = DateTime.Parse(values[6]),
                            DiscoveryMethod = values[7]
                        });
                    }
                }
                StatusText = $"已加载 {Devices.Count} 个设备";
                _logger.LogInformation($"已从 {_csvFilePath} 加载 {Devices.Count} 个设备");
            }
            catch (Exception ex)
            {
                StatusText = "加载失败";
                _logger.LogError(ex, "加载设备信息失败");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
} 