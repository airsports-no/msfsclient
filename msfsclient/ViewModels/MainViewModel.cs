using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using msfsclient.Contracts.Services;
using msfsclient.Models;

namespace msfsclient.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly IAuthService _authService;
        private readonly IProfileService _profileService;
        private readonly ISimConnectService _simConnectService;

        [ObservableProperty]
        public partial UserProfile? Profile { get; set; }

        [ObservableProperty]
        public partial bool IsBusy { get; set; }

        [ObservableProperty]
        public partial bool IsSimConnected { get; set; }

        [ObservableProperty]
        public partial FlightDataStruct CurrentFlightData { get; set; }

        public System.Collections.ObjectModel.ObservableCollection<string> FlightLogs { get; } = new();

        public MainViewModel(IAuthService authService, IProfileService profileService, ISimConnectService simConnectService)
        {
            _authService = authService;
            _profileService = profileService;
            _simConnectService = simConnectService;

            _simConnectService.ConnectionStatusChanged += (s, e) => IsSimConnected = e;
            _simConnectService.FlightDataReceived += (s, e) => 
            {
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().TryEnqueue(() => 
                {
                    CurrentFlightData = e;
                    
                    var logEntry = $"[{DateTime.Now:HH:mm:ss}] Lat: {e.Latitude:F4}, Lon: {e.Longitude:F4}, Alt: {e.Altitude:F0}ft, Spd: {e.GroundVelocity:F1}kt";
                    FlightLogs.Insert(0, logEntry);
                    if (FlightLogs.Count > 10)
                    {
                        FlightLogs.RemoveAt(10);
                    }
                });
            };
        }

        public async Task InitializeAsync(IntPtr windowHandle)
        {
            await LoadProfileAsync();
            _simConnectService.Connect(windowHandle);
        }

        [RelayCommand]
        private async Task LoadProfileAsync()
        {
            IsBusy = true;
            var profile = await _profileService.GetProfileAsync();
            
            // Ensure the update happens on the UI thread to avoid WinRT ArgumentException
            Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().TryEnqueue(() =>
            {
                Profile = profile;
                IsBusy = false;
            });
        }

        [RelayCommand]
        private void Logout()
        {
            _authService.Logout();
            LogoutRequested?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler? LogoutRequested;
    }
}
