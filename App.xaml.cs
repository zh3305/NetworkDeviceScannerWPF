using System.Configuration;
using System.Data;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetworkDeviceScannerWPF.Services;
using NetworkDeviceScannerWPF.Services.NetworkScanner;
using NetworkDeviceScannerWPF.ViewModels;

namespace NetworkDeviceScannerWPF
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private ServiceProvider serviceProvider;

        public App()
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            serviceProvider = services.BuildServiceProvider();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // 添加日志服务
            services.AddLogging(builder =>
            {
                builder.AddConsole()
                       .SetMinimumLevel(LogLevel.Debug);
            });

            // 添加其他服务
            services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
            services.AddSingleton<ArpScannerService>();
            services.AddSingleton<SnmpScannerService>();
            services.AddSingleton<MdnsScannerService>();
            services.AddSingleton<SsdpScannerService>();
            services.AddSingleton<NetworkScannerService>();
            services.AddSingleton<MainViewModel>();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var mainWindow = new MainWindow();
            mainWindow.Initialize(serviceProvider.GetRequiredService<MainViewModel>());
            mainWindow.Show();
        }
    }
}
