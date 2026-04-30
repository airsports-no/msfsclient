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
        private readonly ISettingsService _settingsService;
        private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;
        private readonly Microsoft.UI.Xaml.DispatcherTimer _watchdogTimer;
        private readonly Microsoft.UI.Xaml.DispatcherTimer _taskRefreshTimer;
        private IntPtr _windowHandle;

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
        public partial byte[]? PendingProfileImageData { get; set; }

        [ObservableProperty]
        public partial string? PendingProfileImageFileName { get; set; }

        [ObservableProperty]
        public partial string? ProfileErrorMessage { get; set; }

        [ObservableProperty]
        public partial string? ProfileStatusMessage { get; set; }

        [ObservableProperty]
        public partial bool IsBusy { get; set; }

        [ObservableProperty]
        public partial bool ShowFlightLogs { get; set; }

        partial void OnShowFlightLogsChanged(bool value)
        {
            _settingsService.SetValue("ShowFlightLogs", value);
        }

        [ObservableProperty]
        public partial bool IsSimConnected { get; set; }

        [ObservableProperty]
        public partial bool LastSimConnectUpdateSuccessful { get; set; } = true;

        [ObservableProperty]
        public partial bool IsSimConnectPulseActive { get; set; }

        [ObservableProperty]
        public partial string? SimConnectErrorMessage { get; set; }

        [ObservableProperty]
        public partial string? LastSimConnectUpdateTime { get; set; }

        [ObservableProperty]
        public partial bool IsTracking { get; set; }

        [ObservableProperty]
        public partial bool LastTraccarUpdateSuccessful { get; set; } = true;

        [ObservableProperty]
        public partial bool IsTraccarPulseActive { get; set; }

        [ObservableProperty]
        public partial string? TraccarErrorMessage { get; set; }

        [ObservableProperty]
        public partial string? LastTraccarUpdateTime { get; set; }

        [ObservableProperty]
        public partial FlightDataStruct CurrentFlightData { get; set; }

        public System.Collections.ObjectModel.ObservableCollection<string> FlightLogs { get; } = new();

        public MainViewModel(IAuthService authService, IProfileService profileService, ISimConnectService simConnectService, ITraccarService traccarService, ISettingsService settingsService)
        {
            _authService = authService;
            _profileService = profileService;
            _simConnectService = simConnectService;
            _traccarService = traccarService;
            _settingsService = settingsService;
            _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

            // Initialize Watchdog Timer (checks every 10 seconds)
            _watchdogTimer = new Microsoft.UI.Xaml.DispatcherTimer();
            _watchdogTimer.Interval = TimeSpan.FromSeconds(10);
            _watchdogTimer.Tick += WatchdogTimer_Tick;
            _watchdogTimer.Start();

            // Initialize Task Refresh Timer (checks every 5 minutes)
            _taskRefreshTimer = new Microsoft.UI.Xaml.DispatcherTimer();
            _taskRefreshTimer.Interval = TimeSpan.FromMinutes(5);
            _taskRefreshTimer.Tick += TaskRefreshTimer_Tick;

            // Load settings safely
            ShowFlightLogs = _settingsService.GetValue("ShowFlightLogs", false);

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
                var now = DateTime.Now.ToString("HH:mm:ss");

                // UI Update
                _dispatcherQueue.TryEnqueue(async () => 
                {
                    CurrentFlightData = e;
                    LastSimConnectUpdateSuccessful = true;
                    SimConnectErrorMessage = null;
                    LastSimConnectUpdateTime = now;
                    
                    var logEntry = $"[{now}] Lat: {e.Latitude:F4}, Lon: {e.Longitude:F4}, Alt: {e.Altitude:F0}ft, Spd: {e.GroundVelocity:F1}kt";
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
                            
                            if (result.Success)
                            {
                                LastTraccarUpdateTime = now;
                            }
                            else
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
                    
                    // If not connected to SimConnect, try to connect again in the background
                    if (!IsSimConnected)
                    {
                        _ = Task.Run(() => 
                        {
                            try { _simConnectService.Connect(IntPtr.Zero); } catch { }
                        });
                    }

                    IsTracking = true;
                    FlightLogs.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Tracking Started");
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
            _windowHandle = windowHandle;
            try
            {
                System.Diagnostics.Debug.WriteLine("MainViewModel.InitializeAsync started");
                await Task.WhenAll(LoadProfileAsync(), LoadNavigationTaskAsync());
                System.Diagnostics.Debug.WriteLine($"Profile loaded. ID: {Profile?.SimulatorTrackingId ?? "NULL"}");
                
                // Start periodic task refresh
                _taskRefreshTimer.Start();

                // Run SimConnect connection in a background thread to prevent UI hang on startup
                // if the simulator is not running or slow to respond.
                _ = Task.Run(() => 
                {
                    try
                    {
                        _simConnectService.Connect(_windowHandle);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Background SimConnect Connect failed: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"InitializeAsync Exception: {ex.Message}");
                _dispatcherQueue.TryEnqueue(() => 
                {
                    FlightLogs.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Error during initialization: {ex.Message}");
                });
            }
        }

        private async void TaskRefreshTimer_Tick(object? sender, object e)
        {
            System.Diagnostics.Debug.WriteLine("Periodic Task Refresh: Reloading navigation task...");
            await LoadNavigationTaskAsync();
        }

        private void WatchdogTimer_Tick(object? sender, object e)
        {
            // If tracking is enabled but SimConnect is NOT connected, try to reconnect
            if (IsTracking && !IsSimConnected)
            {
                System.Diagnostics.Debug.WriteLine("Watchdog: Reconnection attempt...");
                _ = Task.Run(() => 
                {
                    try 
                    { 
                        _simConnectService.Connect(_windowHandle); 
                    } 
                    catch (Exception ex) 
                    {
                        System.Diagnostics.Debug.WriteLine($"Watchdog reconnect failed: {ex.Message}");
                    }
                });
            }
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
        private async Task OpenTaskDetails()
        {
            System.Diagnostics.Debug.WriteLine($"OpenTaskDetails called. URL: {CurrentNavigationTask?.Url ?? "NULL"}");
            if (CurrentNavigationTask?.Url != null)
            {
                var link = CurrentNavigationTask.Url;
                if (!link.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    if (link.StartsWith("/"))
                    {
                        link = "https://app.airsports.no" + link;
                    }
                    else
                    {
                        link = "https://app.airsports.no/" + link;
                    }
                }

                try
                {
                    await Windows.System.Launcher.LaunchUriAsync(new Uri(link));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to open task details link: {ex.Message}");
                }
            }
        }

        [RelayCommand]
        private async Task OpenTrackingLink()
        {
            if (CurrentNavigationTask?.TrackingLink != null)
            {
                var link = CurrentNavigationTask.TrackingLink;
                if (!link.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    if (link.StartsWith("/"))
                    {
                        link = "https://app.airsports.no" + link;
                    }
                    else
                    {
                        link = "https://app.airsports.no/" + link;
                    }
                }

                try
                {
                    await Windows.System.Launcher.LaunchUriAsync(new Uri(link));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to open tracking link: {ex.Message}");
                    _dispatcherQueue.TryEnqueue(() => 
                    {
                        FlightLogs.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Error: Could not open tracking link.");
                    });
                }
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
            ProfileStatusMessage = null;
            IsEditingProfile = true;
        }

        [RelayCommand]
        private void CancelEditProfile()
        {
            IsEditingProfile = false;
            ProfileErrorMessage = null;
            ProfileStatusMessage = null;
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
            
            if (PendingProfileImageData != null)
            {
                ProfileStatusMessage = "Uploading image and updating profile...";
            }
            else
            {
                ProfileStatusMessage = "Updating profile details...";
            }

            var success = await _profileService.UpdateProfileAsync(
                EditFirstName, 
                EditLastName, 
                EditProfilePictureUrl, 
                EditBiography,
                PendingProfileImageData,
                PendingProfileImageFileName);

            if (success)
            {
                ProfileStatusMessage = "Profile updated successfully!";
                PendingProfileImageData = null;
                PendingProfileImageFileName = null;
                await LoadProfileAsync();
                
                // Keep the success message visible for a moment
                await Task.Delay(1500);
                ProfileStatusMessage = null;
                IsEditingProfile = false;
            }
            else
            {
                ProfileErrorMessage = "Failed to update profile. Please try again.";
                ProfileStatusMessage = null;
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
            _watchdogTimer.Stop();
            _taskRefreshTimer.Stop();
            _authService.Logout();
            LogoutRequested?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler? LogoutRequested;
    }
}
