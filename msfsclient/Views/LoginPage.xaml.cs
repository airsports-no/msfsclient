using Microsoft.UI.Xaml.Controls;
using msfsclient.ViewModels;

namespace msfsclient.Views
{
    public sealed partial class LoginPage : Page
    {
        public LoginViewModel ViewModel { get; }

        public LoginPage()
        {
            ViewModel = App.GetService<LoginViewModel>();
            this.InitializeComponent();
        }

        private async void LoginButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            await ViewModel.LoginCommand.ExecuteAsync(null);
            if (!ViewModel.IsBusy && string.IsNullOrEmpty(ViewModel.ErrorMessage))
            {
                Frame.Navigate(typeof(MainPage));
            }
        }

        private void SignUp_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            Frame.Navigate(typeof(RegistrationPage));
        }
    }
}
