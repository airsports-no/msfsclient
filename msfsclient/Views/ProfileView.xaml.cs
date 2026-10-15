using System.Linq;
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
            Loaded += (_, _) => ShowPanelState();
        }

        public string Email => Vm.State.Person?.Email ?? "";

        public string TrackingId => Vm.State.Person?.SimulatorTrackingId is { Length: > 0 } id ? $"Simulator tracking id {id}" : "";

        /// <summary>Where the flight display is, or why it is not running.</summary>
        public string HudText => Vm.Hud switch
        {
            { Running: true } => $"The tracking state, start timer, training feedback and photos at {Vm.HudUrl}: open it on a second screen, or in the MSFS toolbar panel.",
            { Error: { } error } => $"Not available: {error} Start the client with --hud-port <number> to use another port.",
            _ => "Not running.",
        };

        private void SimMessages_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (SimMessages.IsOn != Vm.SimMessagesEnabled) Vm.SimMessagesEnabled = SimMessages.IsOn;
        }

        private void Hud_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (Vm.HudUrl.Length > 0) Vm.Platform.OpenUri(Vm.HudUrl);
        }

        protected override void Refresh()
        {
            if (SimMessages.IsOn != Vm.SimMessagesEnabled) SimMessages.IsOn = Vm.SimMessagesEnabled;
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

        // ---- Toolbar panel ---------------------------------------------------------------------------------------------

        /// <summary>The panel package that ships with the client (when it was built with it).</summary>
        private static readonly string BundledPanel = System.IO.Path.Combine(System.AppContext.BaseDirectory, "MsfsPanel", Core.Hud.PanelInstaller.PackageName);

        private static bool HasBundledPanel => System.IO.File.Exists(System.IO.Path.Combine(BundledPanel, "layout.json"));

        private System.Collections.Generic.IReadOnlyList<Core.Hud.SimInstall> _sims = [];

        /// <summary>Which simulators have the panel, and what the buttons offer.</summary>
        private void ShowPanelState(string? result = null)
        {
            _sims = Core.Hud.CommunityFolders.FindForCurrentUser();
            var lines = new System.Collections.Generic.List<string>();
            if (result != null) lines.Add(result);
            if (_sims.Count == 0)
            {
                lines.Add("No MSFS 2020 or 2024 installation was found on this PC.");
            }
            foreach (var sim in _sims)
            {
                var version = Core.Hud.PanelInstaller.InstalledVersion(sim.CommunityFolder);
                lines.Add(version == null ? $"{sim.Name}: not installed" : $"{sim.Name}: installed{(version.Length > 0 ? $" ({version})" : "")}");
            }
            var anyInstalled = _sims.Any(s => Core.Hud.PanelInstaller.InstalledVersion(s.CommunityFolder) != null);
            if (!HasBundledPanel && _sims.Count > 0) lines.Add("Download the panel from flightsim.to and copy its folder into the Community folder.");
            PanelText.Text = string.Join("\n", lines);
            PanelInstall.Visibility = _sims.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            PanelInstall.Content = !HasBundledPanel ? "Open the Community folder" : anyInstalled ? "Update the panel" : "Install the panel";
            PanelRemove.Visibility = anyInstalled ? Visibility.Visible : Visibility.Collapsed;
        }

        private void PanelInstall_Click(object sender, RoutedEventArgs e)
        {
            if (!HasBundledPanel)
            {
                foreach (var sim in _sims) Vm.Platform.OpenUri(new System.Uri(sim.CommunityFolder).AbsoluteUri);
                return;
            }
            try
            {
                foreach (var sim in _sims) Core.Hud.PanelInstaller.Install(BundledPanel, sim.CommunityFolder);
                ShowPanelState("Installed. Restart the simulator, then open Airsports from the toolbar at the top of the screen.");
            }
            catch (System.Exception ex)
            {
                // Most often the simulator is running and has the files open.
                ShowPanelState($"Not installed: {ex.Message} Close the simulator and try again.");
            }
        }

        private async void PanelRemove_Click(object sender, RoutedEventArgs e)
        {
            if (!await Dialogs.ConfirmAsync(XamlRoot, "Remove the toolbar panel?", "Airsports is removed from the simulator's toolbar. You can install it again here.", "Remove", "Keep", danger: true)) return;
            try
            {
                foreach (var sim in _sims) Core.Hud.PanelInstaller.Uninstall(sim.CommunityFolder);
                ShowPanelState("Removed.");
            }
            catch (System.Exception ex)
            {
                ShowPanelState($"Not removed: {ex.Message} Close the simulator and try again.");
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
