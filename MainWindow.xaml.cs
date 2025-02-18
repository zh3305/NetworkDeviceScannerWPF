using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Net.NetworkInformation;
using NetworkDeviceScannerWPF.Models;
using NetworkDeviceScannerWPF.Services;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using NetworkDeviceScannerWPF.ViewModels;

namespace NetworkDeviceScannerWPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        public void Initialize(MainViewModel viewModel)
        {
            DataContext = viewModel;
        }
    }
}