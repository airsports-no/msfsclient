using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using msfsclient.Core.Api;

namespace msfsclient.Views
{
    /// <summary>Email and password sign-in, account creation and password reset (port of LoginScreen).</summary>
    public sealed partial class LoginView : ShellPart
    {
        private bool _creating;

        public LoginView()
        {
            this.InitializeComponent();
            Loaded += (_, _) => Email.Focus(FocusState.Programmatic);
        }

        private bool Valid => Email.Text.Contains('@') && Password.Password.Length >= 6;

        protected override void Refresh()
        {
            var busy = Vm.State.Busy;
            Submit.Content = busy ? "Please wait…" : _creating ? "Create account" : "Sign in";
            Submit.IsEnabled = !busy && Valid;
            ModeButton.Content = _creating ? "I already have an account" : "Create an account";
            ResetButton.Visibility = _creating ? Visibility.Collapsed : Visibility.Visible;
            ResetButton.IsEnabled = !busy && Email.Text.Contains('@');
        }

        private void Email_Changed(object sender, TextChangedEventArgs e) => Refresh();

        private void Password_Changed(object sender, RoutedEventArgs e) => Refresh();

        private void Submit_Click(object sender, RoutedEventArgs e)
        {
            if (!Valid || Vm.State.Busy) return;
            var email = Email.Text.Trim();
            _ = _creating ? Vm.Controller.CreateAccountAsync(email, Password.Password) : Vm.Controller.SignInWithPasswordAsync(email, Password.Password);
        }

        private void Password_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter) Submit_Click(sender, e);
        }

        private void Mode_Click(object sender, RoutedEventArgs e)
        {
            _creating = !_creating;
            Refresh();
        }

        private void Reset_Click(object sender, RoutedEventArgs e) => _ = Vm.Controller.ResetPasswordAsync(Email.Text.Trim());

        private void Terms_Click(object sender, RoutedEventArgs e) => Vm.Platform.OpenUri(Urls.TermsAndPrivacyUrl);
    }
}
