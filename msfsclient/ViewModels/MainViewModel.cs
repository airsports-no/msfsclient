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
        private readonly ITraccarService _traccarService;

        [ObservableProperty]
        public partial UserProfile? Profile { get; set; }

        [ObservableProperty]
        public partial bool IsBusy { get; set; }

        [ObservableProperty]
        public partial bool IsSimConnected { get; set; }

        [ObservableProperty]
        public partial bool IsTracking { get; set; }

        [ObservableProperty]
        public partial bool LastTraccarUpdateSuccessful { get; set; } = true;

        [ObservableProperty]
        public partial bool IsTraccarPulseActive { get; set; }

        [ObservableProperty]
        public partial FlightDataStruct CurrentFlightData { get; set; }

        public System.Collections.ObjectModel.ObservableCollection<string> FlightLogs { get; } = new();

        public MainViewModel(IAuthService authService, IProfileService profileService, ISimConnectService simConnectService, ITraccarService traccarService)
        {
            _authService = authService;
            _profileService = profileService;
            _simConnectService = simConnectService;
            _traccarService = traccarService;

            _simConnectService.ConnectionStatusChanged += (s, e) => IsSimConnected = e;
            _simConnectService.FlightDataReceived += (s, e) => 
            {
                // UI Update
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

                // Traccar Update - Run on background thread
                if (IsTracking && Profile?.SimulatorTrackingId != null)
                {
                    _ = Task.Run(async () => 
                    {
                        var success = await _traccarService.SendDataAsync(Profile.SimulatorTrackingId, e);
                        
                        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().TryEnqueue(async () => 
                        {
                            LastTraccarUpdateSuccessful = success;
                            if (success)
                            {
                                // Pulse effect
                                IsTraccarPulseActive = true;
                                await Task.Delay(200);
                                IsTraccarPulseActive = false;
                            }
                        });
                    });
                }
            };
        }

        [RelayCommand]
        public void ToggleTracking()
        {
            System.Diagnostics.Debug.WriteLine($"ToggleTracking called. Current IsTracking: {IsTracking}, ID: {Profile?.SimulatorTrackingId ?? "NULL"}");
            
            if (!IsTracking && string.IsNullOrEmpty(Profile?.SimulatorTrackingId))
            {
                var logEntryErr = $"[{DateTime.Now:HH:mm:ss}] Error: Simulator Tracking ID is missing.";
                FlightLogs.Insert(0, logEntryErr);
                System.Diagnostics.Debug.WriteLine("ToggleTracking: Missing ID, aborting.");
                return;
            }

            IsTracking = !IsTracking;
            var status = IsTracking ? "Started" : "Stopped";
            var logEntryStatus = $"[{DateTime.Now:HH:mm:ss}] Tracking {status}";
            FlightLogs.Insert(0, logEntryStatus);
            System.Diagnostics.Debug.WriteLine($"ToggleTracking: New status: {status}");
        }

        public async Task InitializeAsync(IntPtr windowHandle)
        {
            System.Diagnostics.Debug.WriteLine("MainViewModel.InitializeAsync started");
            await LoadProfileAsync();
            System.Diagnostics.Debug.WriteLine($"Profile loaded. ID: {Profile?.SimulatorTrackingId ?? "NULL"}");
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
