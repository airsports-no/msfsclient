using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using msfsclient.ViewModels;

namespace msfsclient.Views
{
    public sealed partial class RegistrationPage : Page
    {
        public RegistrationViewModel ViewModel { get; }

        public RegistrationPage()
        {
            ViewModel = App.GetService<RegistrationViewModel>();
            ViewModel.RegistrationSuccessful += ViewModel_RegistrationSuccessful;
            this.InitializeComponent();
        }

        private async void ViewModel_RegistrationSuccessful(object? sender, System.EventArgs e)
        {
            // Give the user a moment to see the success message
            await System.Threading.Tasks.Task.Delay(3000);
            
            // Navigate to login and pass the email so it's ready for them
            Frame.Navigate(typeof(LoginPage), ViewModel.Email);
        }

        private void BackToLogin_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
            else
            {
                Frame.Navigate(typeof(LoginPage));
            }
        }
    }
}
