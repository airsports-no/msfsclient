using System;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using msfsclient.ViewModels;

namespace msfsclient.Views
{
    public sealed partial class MainPage : Page
    {
        public MainViewModel ViewModel { get; }

        public MainPage()
        {
            ViewModel = App.GetService<MainViewModel>();
            ViewModel.LogoutRequested += ViewModel_LogoutRequested;
            this.InitializeComponent();
        }

        private void ViewModel_LogoutRequested(object? sender, System.EventArgs e)
        {
            Frame.Navigate(typeof(LoginPage));
        }

        private void StartStopTracking_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            ViewModel.ToggleTrackingCommand.Execute(null);
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            var window = (App.Current as App)?.Window;
            var handle = window != null ? WinRT.Interop.WindowNative.GetWindowHandle(window) : IntPtr.Zero;

            await ViewModel.InitializeAsync(handle);
            
            if (ViewModel.Profile != null)
            {
                System.Diagnostics.Debug.WriteLine($"Profile Loaded: {ViewModel.Profile.FullName}");
                System.Diagnostics.Debug.WriteLine($"Picture URL: {ViewModel.Profile.ProfilePictureUrl ?? "NULL"}");
            }
        }
    }
}
