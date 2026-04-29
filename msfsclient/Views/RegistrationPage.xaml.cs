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
            this.InitializeComponent();
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
