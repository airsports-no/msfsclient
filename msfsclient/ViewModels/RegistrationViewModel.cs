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
        [NotifyCanExecuteChangedFor(nameof(RegisterCommand))]
        public partial string Email { get; set; } = string.Empty;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RegisterCommand))]
        public partial string Password { get; set; } = string.Empty;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RegisterCommand))]
        public partial string FirstName { get; set; } = string.Empty;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RegisterCommand))]
        public partial string LastName { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string ErrorMessage { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string SuccessMessage { get; set; } = string.Empty;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RegisterCommand))]
        public partial bool IsBusy { get; set; }

        public RegistrationViewModel(IAuthService authService)
        {
            _authService = authService;
        }

        public event System.EventHandler? RegistrationSuccessful;

        private bool CanRegister()
        {
            return !IsBusy &&
                   !string.IsNullOrWhiteSpace(Email) &&
                   !string.IsNullOrWhiteSpace(Password) &&
                   !string.IsNullOrWhiteSpace(FirstName) &&
                   !string.IsNullOrWhiteSpace(LastName) &&
                   Email.Contains("@") && 
                   Email.Contains(".");
        }

        [RelayCommand(CanExecute = nameof(CanRegister))]
        private async Task RegisterAsync()
        {
            IsBusy = true;
            ErrorMessage = string.Empty;
            SuccessMessage = string.Empty;

            var success = await _authService.RegisterAsync(Email, Password, FirstName, LastName);
            // ... rest of method unchanged

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

                RegistrationSuccessful?.Invoke(this, System.EventArgs.Empty);
            }
            else
            {
                ErrorMessage = _authService.LastErrorMessage ?? "Registration failed.";
            }

            IsBusy = false;
        }
    }
}
