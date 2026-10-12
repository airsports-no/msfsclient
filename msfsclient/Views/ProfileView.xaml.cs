using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using msfsclient.Core.Api;
using msfsclient.Helpers;

namespace msfsclient.Views
{
    public sealed partial class ProfileView : ShellPart
    {
        public ProfileView()
        {
            this.InitializeComponent();
            var p = Vm.State.Person;
            First.Text = p?.FirstName ?? "";
            Last.Text = p?.LastName ?? "";
            Bio.Text = p?.Biography ?? "";
        }

        public string Email => Vm.State.Person?.Email ?? "";

        public string TrackingId => Vm.State.Person?.SimulatorTrackingId is { Length: > 0 } id ? $"Simulator tracking id {id}" : "";

        protected override void Refresh()
        {
            var saving = Vm.State.ProfileSaving;
            Save.Content = saving ? "Saving…" : "Save";
            Save.IsEnabled = !saving && !string.IsNullOrWhiteSpace(First.Text) && !string.IsNullOrWhiteSpace(Last.Text);
        }

        private void Input_Changed(object sender, TextChangedEventArgs e) => Refresh();

        private void Save_Click(object sender, RoutedEventArgs e) => _ = Vm.Controller.UpdateProfileAsync(new PersonUpdate
        {
            FirstName = First.Text.Trim(), LastName = Last.Text.Trim(), Biography = Bio.Text.Trim(),
        });

        private async void Photo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var jpeg = await Vm.Platform.PickProfilePhotoAsync();
                if (jpeg != null) _ = Vm.Controller.UploadPhotoAsync(jpeg);
            }
            catch (System.Exception ex)
            {
                _ = Dialogs.InfoAsync(XamlRoot, "Photo not changed", $"The photo could not be read: {ex.Message}");
            }
        }

        private void Tour_Click(object sender, RoutedEventArgs e) => Vm.Controller.OpenTour();

        private void SignOut_Click(object sender, RoutedEventArgs e) => Vm.SignOut();

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (await Dialogs.ConfirmAsync(XamlRoot, "Delete your account?",
                    "This permanently deletes your Airsports login and profile. Flights you took part in stay in the results, but without your name. This cannot be undone.",
                    "Delete", "Cancel", danger: true))
            {
                await Vm.DeleteAccountAsync();
            }
        }
    }
}
