using System;
using System.ComponentModel;

namespace NetworkDeviceScannerWPF.Models
{
    public class NetworkDevice : INotifyPropertyChanged
    {
        private string _name;
        private string _ip;
        private string _mac;
        private bool _isOnline;
        private string _customName;
        private string _location;
        private DateTime _lastSeen;
        private string _discoveryMethod;

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged(nameof(Name));
            }
        }

        public string IP
        {
            get => _ip;
            set
            {
                _ip = value;
                OnPropertyChanged(nameof(IP));
            }
        }

        public string MAC
        {
            get => _mac;
            set
            {
                _mac = value;
                OnPropertyChanged(nameof(MAC));
            }
        }

        public bool IsOnline
        {
            get => _isOnline;
            set
            {
                _isOnline = value;
                OnPropertyChanged(nameof(IsOnline));
            }
        }

        public string CustomName
        {
            get => _customName;
            set
            {
                _customName = value;
                OnPropertyChanged(nameof(CustomName));
            }
        }

        public string Location
        {
            get => _location;
            set
            {
                _location = value;
                OnPropertyChanged(nameof(Location));
            }
        }

        public DateTime LastSeen
        {
            get => _lastSeen;
            set
            {
                _lastSeen = value;
                OnPropertyChanged(nameof(LastSeen));
            }
        }

        public string DiscoveryMethod
        {
            get => _discoveryMethod;
            set
            {
                _discoveryMethod = value;
                OnPropertyChanged(nameof(DiscoveryMethod));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
} 