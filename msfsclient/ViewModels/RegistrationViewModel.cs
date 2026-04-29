using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using msfsclient.Contracts.Services;

namespace msfsclient.ViewModels
{
    public partial class RegistrationViewModel : ObservableObject
    {
        private readonly IAuthService _authService;

        [ObservableProperty]
        public partial string Email { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string Password { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string DisplayName { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string ErrorMessage { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string SuccessMessage { get; set; } = string.Empty;

        [ObservableProperty]
        public partial bool IsBusy { get; set; }

        public RegistrationViewModel(IAuthService authService)
        {
            _authService = authService;
        }

        [RelayCommand]
        private async Task RegisterAsync()
        {
            if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password) || string.IsNullOrWhiteSpace(DisplayName))
            {
                ErrorMessage = "Please fill in all fields.";
                return;
            }

            IsBusy = true;
            ErrorMessage = string.Empty;
            SuccessMessage = string.Empty;

            var success = await _authService.RegisterAsync(Email, Password, DisplayName);

            if (success)
            {
                try
                {
                    await _authService.SendEmailVerificationAsync();
                    SuccessMessage = "Registration successful! Please check your email for verification.";
                }
                catch
                {
                    SuccessMessage = "Registration successful, but failed to send verification email. You can still log in.";
                }
            }
            else
            {
                ErrorMessage = _authService.LastErrorMessage ?? "Registration failed.";
            }

            IsBusy = false;
        }
    }
}
