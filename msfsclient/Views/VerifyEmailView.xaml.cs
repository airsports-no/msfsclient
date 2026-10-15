using Microsoft.UI.Xaml;

namespace msfsclient.Views
{
    /// <summary>The email verification gate (the backend links accounts by verified email).</summary>
    public sealed partial class VerifyEmailView : ShellPart
    {
        public VerifyEmailView()
        {
            this.InitializeComponent();
        }

        public string Message => $"We sent a verification link to {Vm.State.Email}. Open it, then come back and press the button below.";

        private void Check_Click(object sender, RoutedEventArgs e) => _ = Vm.Controller.CheckVerifiedAsync();

        private void Resend_Click(object sender, RoutedEventArgs e) => _ = Vm.Controller.ResendVerificationAsync();

        private void SignOut_Click(object sender, RoutedEventArgs e) => Vm.SignOut();
    }
}
