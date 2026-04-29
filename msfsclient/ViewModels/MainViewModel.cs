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
        private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;

        [ObservableProperty]
        public partial UserProfile? Profile { get; set; }

        [ObservableProperty]
        public partial NavigationTask? CurrentNavigationTask { get; set; }

        [ObservableProperty]
        public partial bool IsEditingProfile { get; set; }

        [ObservableProperty]
        public partial string? EditFirstName { get; set; }

        [ObservableProperty]
        public partial string? EditLastName { get; set; }

        [ObservableProperty]
        public partial string? EditProfilePictureUrl { get; set; }

        [ObservableProperty]
        public partial string? EditBiography { get; set; }

        [ObservableProperty]
        public partial string? ProfileErrorMessage { get; set; }

        [ObservableProperty]
        public partial bool IsBusy { get; set; }

        [ObservableProperty]
        public partial bool IsSimConnected { get; set; }

        [ObservableProperty]
        public partial bool LastSimConnectUpdateSuccessful { get; set; } = true;

        [ObservableProperty]
        public partial bool IsSimConnectPulseActive { get; set; }

        [ObservableProperty]
        public partial string? SimConnectErrorMessage { get; set; }

        [ObservableProperty]
        public partial bool IsTracking { get; set; }

        [ObservableProperty]
        public partial bool LastTraccarUpdateSuccessful { get; set; } = true;

        [ObservableProperty]
        public partial bool IsTraccarPulseActive { get; set; }

        [ObservableProperty]
        public partial string? TraccarErrorMessage { get; set; }

        [ObservableProperty]
        public partial FlightDataStruct CurrentFlightData { get; set; }

        public System.Collections.ObjectModel.ObservableCollection<string> FlightLogs { get; } = new();

        public MainViewModel(IAuthService authService, IProfileService profileService, ISimConnectService simConnectService, ITraccarService traccarService)
        {
            _authService = authService;
            _profileService = profileService;
            _simConnectService = simConnectService;
            _traccarService = traccarService;
            _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

            _simConnectService.ConnectionStatusChanged += (s, e) => _dispatcherQueue.TryEnqueue(() => 
            {
                IsSimConnected = e;
                if (e) SimConnectErrorMessage = null;
            });

            _simConnectService.ErrorOccurred += (s, e) => _dispatcherQueue.TryEnqueue(() => 
            {
                SimConnectErrorMessage = e;
                LastSimConnectUpdateSuccessful = false;
            });

            _simConnectService.FlightDataReceived += (s, e) => 
            {
                // UI Update
                _dispatcherQueue.TryEnqueue(async () => 
                {
                    CurrentFlightData = e;
                    LastSimConnectUpdateSuccessful = true;
                    SimConnectErrorMessage = null;
                    
                    var logEntry = $"[{DateTime.Now:HH:mm:ss}] Lat: {e.Latitude:F4}, Lon: {e.Longitude:F4}, Alt: {e.Altitude:F0}ft, Spd: {e.GroundVelocity:F1}kt";
                    FlightLogs.Insert(0, logEntry);
                    if (FlightLogs.Count > 10)
                    {
                        FlightLogs.RemoveAt(10);
                    }

                    // SimConnect Pulse
                    IsSimConnectPulseActive = true;
                    await Task.Delay(400);
                    IsSimConnectPulseActive = false;
                });

                // Traccar Update - Run on background thread
                if (IsTracking && Profile?.SimulatorTrackingId != null)
                {
                    _ = Task.Run(async () => 
                    {
                        var result = await _traccarService.SendDataAsync(Profile.SimulatorTrackingId, e);
                        
                        _dispatcherQueue.TryEnqueue(async () => 
                        {
                            LastTraccarUpdateSuccessful = result.Success;
                            TraccarErrorMessage = result.Success ? null : result.Message;
                            
                            if (!result.Success)
                            {
                                var logEntryErr = $"[{DateTime.Now:HH:mm:ss}] Traccar Error: {result.Message}";
                                FlightLogs.Insert(0, logEntryErr);
                            }

                            // Pulse effect for both success and failure
                            IsTraccarPulseActive = true;
                            await Task.Delay(400); // 400ms pulse for better visibility
                            IsTraccarPulseActive = false;
                        });
                    });
                }
            };
        }

        [RelayCommand]
        public async Task ToggleTracking()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"ToggleTracking called. Current IsTracking: {IsTracking}, ID: {Profile?.SimulatorTrackingId ?? "NULL"}");
                
                if (!IsTracking)
                {
                    if (Profile == null || !Profile.Validated)
                    {
                        var logEntryErr = $"[{DateTime.Now:HH:mm:ss}] Error: Please update profile to enable tracking.";
                        FlightLogs.Insert(0, logEntryErr);
                        return;
                    }

                    if (string.IsNullOrEmpty(Profile?.SimulatorTrackingId))
                    {
                        var logEntryErr = $"[{DateTime.Now:HH:mm:ss}] Error: Simulator Tracking ID is missing in profile.";
                        FlightLogs.Insert(0, logEntryErr);
                        System.Diagnostics.Debug.WriteLine("ToggleTracking: Missing ID, aborting.");
                        return;
                    }

                    await LoadNavigationTaskAsync();
                    
                    IsTracking = true;
                    FlightLogs.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Tracking Started (ID: {Profile.SimulatorTrackingId})");
                }
                else
                {
                    IsTracking = false;
                    FlightLogs.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Tracking Stopped");
                }
                
                System.Diagnostics.Debug.WriteLine($"ToggleTracking: New status: {IsTracking}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ToggleTracking Exception: {ex}");
                FlightLogs.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Error: {ex.Message}");
            }
        }

        public async Task InitializeAsync(IntPtr windowHandle)
        {
            System.Diagnostics.Debug.WriteLine("MainViewModel.InitializeAsync started");
            await Task.WhenAll(LoadProfileAsync(), LoadNavigationTaskAsync());
            System.Diagnostics.Debug.WriteLine($"Profile loaded. ID: {Profile?.SimulatorTrackingId ?? "NULL"}");
            _simConnectService.Connect(windowHandle);
        }

        [RelayCommand]
        private async Task LoadNavigationTaskAsync()
        {
            var task = await _profileService.GetCurrentNavigationTaskAsync();
            _dispatcherQueue.TryEnqueue(() =>
            {
                CurrentNavigationTask = task;
            });
        }

        [RelayCommand]
        private async Task OpenTrackingLink()
        {
            if (CurrentNavigationTask?.TrackingLink != null)
            {
                await Windows.System.Launcher.LaunchUriAsync(new Uri(CurrentNavigationTask.TrackingLink));
            }
        }

        [RelayCommand]
        private void StartEditProfile()
        {
            if (Profile == null) return;

            EditFirstName = Profile.FirstName;
            EditLastName = Profile.LastName;
            EditProfilePictureUrl = Profile.ProfilePictureUrl;
            EditBiography = Profile.Biography;
            ProfileErrorMessage = null;
            IsEditingProfile = true;
        }

        [RelayCommand]
        private void CancelEditProfile()
        {
            IsEditingProfile = false;
            ProfileErrorMessage = null;
        }

        [RelayCommand]
        private async Task SaveProfileAsync()
        {
            if (string.IsNullOrWhiteSpace(EditFirstName) || string.IsNullOrWhiteSpace(EditLastName))
            {
                ProfileErrorMessage = "First and last name are required.";
                return;
            }

            IsBusy = true;
            ProfileErrorMessage = null;

            var success = await _profileService.UpdateProfileAsync(
                EditFirstName, 
                EditLastName, 
                EditProfilePictureUrl, 
                EditBiography);

            if (success)
            {
                await LoadProfileAsync();
                IsEditingProfile = false;
            }
            else
            {
                ProfileErrorMessage = "Failed to update profile. Please try again.";
            }

            IsBusy = false;
        }

        [RelayCommand]
        private async Task LoadProfileAsync()
        {
            IsBusy = true;
            var profile = await _profileService.GetProfileAsync();
            
            // Ensure the update happens on the UI thread to avoid WinRT ArgumentException
            _dispatcherQueue.TryEnqueue(() =>
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
