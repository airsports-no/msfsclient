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

        [ObservableProperty]
        public partial UserProfile? Profile { get; set; }

        [ObservableProperty]
        public partial bool IsBusy { get; set; }

        public MainViewModel(IAuthService authService, IProfileService profileService)
        {
            _authService = authService;
            _profileService = profileService;
        }

        public async Task InitializeAsync()
        {
            await LoadProfileAsync();
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
            // Navigation back to login should be handled by a navigation service or the view
        }
    }
}
