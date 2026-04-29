using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using msfsclient.Contracts.Services;

namespace msfsclient.ViewModels
{
    public partial class LoginViewModel : ObservableObject
    {
        private readonly IAuthService _authService;

        [ObservableProperty]
        public partial string Email { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string Password { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string ErrorMessage { get; set; } = string.Empty;

        [ObservableProperty]
        public partial bool IsBusy { get; set; }

        public LoginViewModel(IAuthService authService)
        {
            _authService = authService;
        }

        [RelayCommand]
        private async Task LoginAsync()
        {
            if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
            {
                ErrorMessage = "Please enter email and password.";
                return;
            }

            IsBusy = true;
            ErrorMessage = string.Empty;

            var success = await _authService.LoginAsync(Email, Password);

            if (success)
            {
                // App.GetService can be used if we don't have a navigation service yet
                // However, the View usually handles navigation or we use a messaging system
                // For this simple case, we'll notify the view or handle it in the code-behind
            }
            else
            {
                ErrorMessage = _authService.LastErrorMessage ?? "Invalid email or password.";
            }

            IsBusy = false;
        }

        [RelayCommand]
        private async Task ResetPasswordAsync()
        {
            if (string.IsNullOrWhiteSpace(Email))
            {
                ErrorMessage = "Please enter your email to reset password.";
                return;
            }

            IsBusy = true;
            try
            {
                await _authService.ResetPasswordAsync(Email);
                ErrorMessage = "Password reset email sent.";
            }
            catch
            {
                ErrorMessage = "Error sending password reset email.";
            }
            IsBusy = false;
        }

        [RelayCommand]
        private async Task ResendVerificationEmailAsync()
        {
            IsBusy = true;
            try
            {
                await _authService.SendEmailVerificationAsync();
                ErrorMessage = "Verification email resent. Please check your inbox.";
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error: {ex.Message}";
            }
            IsBusy = false;
        }
    }
}
