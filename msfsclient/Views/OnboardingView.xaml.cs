using System.Linq;
using Microsoft.UI.Xaml.Controls;
using msfsclient.Core.Api;

namespace msfsclient.Views
{
    /// <summary>First-start profile completion when the profile is not validated yet (port of OnboardingScreen).</summary>
    public sealed partial class OnboardingView : ShellPart
    {
        public OnboardingView()
        {
            this.InitializeComponent();
            var p = Vm.State.Person;
            First.Text = p?.FirstName ?? "";
            Last.Text = p?.LastName ?? "";
            Country.Text = p?.Country ?? "";
            Prefix.Text = p?.PhoneCountryPrefix ?? "";
            Phone.Text = p?.PhoneNationalNumber ?? "";
        }

        private bool Valid => !string.IsNullOrWhiteSpace(First.Text) && !string.IsNullOrWhiteSpace(Last.Text) && Country.Text.Trim() is { Length: 2 } code && code.All(char.IsAsciiLetter)
            && !string.IsNullOrWhiteSpace(Phone.Text);

        protected override void Refresh()
        {
            Save.Content = Vm.State.Busy ? "Saving…" : "Save";
            Save.IsEnabled = !Vm.State.Busy && Valid;
        }

        private void Input_Changed(object sender, TextChangedEventArgs e) => Refresh();

        private void Save_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => _ = Vm.Controller.SaveProfileAsync(new PersonUpdate
        {
            FirstName = First.Text.Trim(), LastName = Last.Text.Trim(), Country = Country.Text.Trim().ToUpperInvariant(),
            PhoneNationalNumber = Phone.Text.Trim(), PhoneCountryPrefix = Prefix.Text.Trim(), Validated = true,
        });
    }
}
