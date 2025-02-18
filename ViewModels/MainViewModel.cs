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
using System.Windows;
using System.Threading;
using System.Threading.Tasks;

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
        private CancellationTokenSource _cancellationTokenSource;

        public MainViewModel(NetworkScannerService scannerService, ILogger<MainViewModel> logger)
        {
            _scannerService = scannerService;
            _logger = logger;
            Devices = new ObservableCollection<NetworkDevice>();
            NetworkInterfaces = new ObservableCollection<NetworkInterface>();
            ScanCommand = new AsyncRelayCommand(StartScanAsync, () => CanStartScan);
            StopCommand = new RelayCommand(StopScan, () => IsScanning);
            SaveCommand = new RelayCommand(SaveDevices);
            LoadNetworkInterfaces();
            LoadDevicesFromCsv();
            Devices.CollectionChanged += (s, e) => SaveDevices();
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
                OnPropertyChanged(nameof(CanStartScan));
                (ScanCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                (StopCommand as RelayCommand)?.RaiseCanExecuteChanged();
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

        public bool CanStartScan => SelectedInterface != null && !IsScanning;

        public ICommand ScanCommand { get; }
        public ICommand StopCommand { get; }
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

        private void StopScan()
        {
            if (_cancellationTokenSource == null || _cancellationTokenSource.IsCancellationRequested)
            {
                return;
            }

            _cancellationTokenSource?.Cancel();
            _logger.LogInformation("用户请求停止扫描");
            StatusText = "正在停止扫描...";
            (StopCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private async Task StartScanAsync()
        {
            if (SelectedInterface == null)
            {
                StatusText = "请选择网络接口";
                return;
            }

            IsScanning = true;
            StatusText = "正在扫描...";
            _cancellationTokenSource = new CancellationTokenSource();

            // 先将所有设备标记为离线
            foreach (var device in Devices)
            {
                device.IsOnline = false;
            }

            var discoveredCount = 0;

            try
            {
                // 在后台线程执行扫描
                await Task.Run(async () => 
                {
                    try
                    {
                        await _scannerService.ScanNetworkAsync(
                            SelectedInterface,
                            device => 
                            {
                                _cancellationTokenSource.Token.ThrowIfCancellationRequested();
                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    var existingDevice = Devices.FirstOrDefault(d => 
                                        !string.IsNullOrEmpty(d.MAC) && 
                                        !string.IsNullOrEmpty(device.MAC) && 
                                        d.MAC.Equals(device.MAC, StringComparison.OrdinalIgnoreCase));

                                    if (existingDevice != null)
                                    {
                                        existingDevice.IP = device.IP;
                                        if (!string.IsNullOrEmpty(device.Name))
                                        {
                                            existingDevice.Name = device.Name;
                                        }
                                        existingDevice.IsOnline = true;
                                        existingDevice.LastSeen = device.LastSeen;
                                        if (!string.IsNullOrEmpty(device.Location))
                                        {
                                            existingDevice.Location = device.Location;
                                        }
                                        existingDevice.DiscoveryMethod = string.Join(",", 
                                            new HashSet<string>(
                                                (existingDevice.DiscoveryMethod + "," + device.DiscoveryMethod)
                                                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                                            )
                                        );
                                    }
                                    else
                                    {
                                        device.IsOnline = true;
                                        Devices.Add(device);
                                    }
                                    discoveredCount++;
                                    StatusText = $"正在扫描...已发现 {discoveredCount} 个设备";
                                });
                            },
                            _cancellationTokenSource.Token
                        );
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                });

                StatusText = $"扫描完成，共发现 {discoveredCount} 个设备";
                _logger.LogInformation($"扫描完成，发现 {discoveredCount} 个设备");
            }
            catch (OperationCanceledException)
            {
                StatusText = "扫描已取消";
                _logger.LogInformation("扫描已取消");
            }
            catch (Exception ex)
            {
                StatusText = "扫描失败";
                _logger.LogError(ex, "扫描失败");
            }
            finally
            {
                IsScanning = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private void SaveDevices()
        {
            try
            {
                // 确保目录存在
                var directory = Path.GetDirectoryName(_csvFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var lines = new List<string>
                {
                    "Name,IP,MAC,IsOnline,CustomName,Location,LastSeen,DiscoveryMethod"
                };

                foreach (var device in Devices)
                {
                    // 处理CSV中的特殊字符
                    var name = device.Name?.Replace(",", "，");
                    var customName = device.CustomName?.Replace(",", "，");
                    var location = device.Location?.Replace(",", "，");
                    var discoveryMethod = device.DiscoveryMethod?.Replace(",", "；");

                    lines.Add(
                        $"{name},{device.IP},{device.MAC},{device.IsOnline}," +
                        $"{customName},{location},{device.LastSeen}," +
                        $"{discoveryMethod}"
                    );
                }

                File.WriteAllLines(_csvFilePath, lines);
                _logger.LogInformation($"已保存 {Devices.Count} 个设备到 {_csvFilePath}");
                StatusText = $"已保存 {Devices.Count} 个设备到 {_csvFilePath}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存设备信息失败");
            }
        }

        private void LoadDevicesFromCsv()
        {
            if (!File.Exists(_csvFilePath)) 
            {
                _logger.LogInformation("CSV文件不存在，将在首次扫描时创建");
                return;
            }

            try
            {
                var lines = File.ReadAllLines(_csvFilePath);
                foreach (var line in lines.Skip(1)) // Skip header
                {
                    var values = line.Split(',');
                    if (values.Length >= 8)
                    {
                        // 检查MAC地址是否已存在
                        var mac = values[2];
                        if (!string.IsNullOrEmpty(mac) && 
                            !Devices.Any(d => d.MAC?.Equals(mac, StringComparison.OrdinalIgnoreCase) == true))
                        {
                            Devices.Add(new NetworkDevice
                            {
                                Name = values[0],
                                IP = values[1],
                                MAC = values[2],
                                IsOnline = false, // 从文件加载时默认为离线
                                CustomName = values[4],
                                Location = values[5],
                                LastSeen = DateTime.Parse(values[6]),
                                DiscoveryMethod = values[7]
                            });
                        }
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