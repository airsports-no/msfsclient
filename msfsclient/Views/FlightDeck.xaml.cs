using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using msfsclient.Core.App;
using msfsclient.Core.Training;
using msfsclient.Helpers;
using msfsclient.Core.Hud;

namespace msfsclient.Views
{
    public sealed partial class FlightDeck : ShellPart
    {
        private static readonly SolidColorBrush Green = new(Windows.UI.Color.FromArgb(0xFF, 0x2F, 0xCB, 0x84));
        private static readonly SolidColorBrush Red = new(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0x7A, 0x90));

        public FlightDeck()
        {
            this.InitializeComponent();
        }

        /// <summary>Starting needs a simulator tracking id; stopping is always possible.</summary>
        public bool TrackEnabled => Vm.Tracking.Running || Vm.CanStartTracking;

        public Brush TrackBrush => Vm.Tracking.Running ? Red : Green;

        public string TrackLabel => Vm.Tracking.Running ? "Stop tracking" : "Start tracking";

        public GridLength TimerColumn => Vm.ShowTimerTab ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

        public GridLength PhotosColumn => Vm.ShowPhotosTab ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

        public bool GateMissing => Vm.Gate == null;

        public string GateName => Vm.Gate?.Gate ?? "";

        public string GateText => Vm.Gate?.Text ?? "";

        public Brush GateBrush => Vm.Gate?.Timing == GateTiming.OnTime ? Ui.Brush("DockGreenBrush") : Ui.Brush("DockAmberBrush");

        protected override void Refresh()
        {
            var screen = Vm.State.Screen;
            FlightTab.IsChecked = screen == Screen.Home;
            TimerTab.IsChecked = screen == Screen.Timer;
            PhotosTab.IsChecked = screen == Screen.Observations;
        }

        public static FontFamily FontFor(StatusTone tone) => (FontFamily)Application.Current.Resources[tone is StatusTone.Title or StatusTone.Live ? "BarlowBold" : tone is StatusTone.Warning ? "BarlowSemiBold" : "BarlowRegular"];

        public static double SizeFor(StatusTone tone) => tone switch
        {
            StatusTone.Title or StatusTone.Live => 17,
            StatusTone.Small => 13,
            _ => 14,
        };

        public static Brush BrushFor(StatusTone tone) => Ui.Brush(tone switch
        {
            StatusTone.Live => "DockGreenBrush",
            StatusTone.Warning => "DockAmberBrush",
            StatusTone.Error => "DockErrorBrush",
            StatusTone.Title => "DockContentBrush",
            _ => "DockMutedBrush",
        });

        private async void TrackButton_Click(object sender, RoutedEventArgs e)
        {
            if (!Vm.Tracking.Running)
            {
                Vm.StartTracking();
                return;
            }
            // Stopping is confirmed because a stray click in flight would end the pilot's tracking.
            var details = string.Join("\n", new[] { Vm.FlightLabel, Vm.TrackedFor is { } t ? $"tracking for {t}" : null }.Where(s => s != null));
            var content = new StackPanel { Spacing = 8 };
            if (details.Length > 0)
            {
                content.Children.Add(new TextBlock { Text = details, Style = (Style)Application.Current.Resources["CondensedTitle"], FontSize = 22 });
            }
            content.Children.Add(new TextBlock
            {
                Text = "Your position will no longer be shared and your flight will not be scored while tracking is off.",
                Style = (Style)Application.Current.Resources["BodyText"],
            });
            if (await Dialogs.ConfirmAsync(XamlRoot, "Stop tracking?", content, "Stop", "Keep tracking", danger: true)) Vm.StopTracking();
        }

        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            var screen = Vm.State.Screen;
            switch ((sender as FrameworkElement)?.Tag as string)
            {
                case "Flight":
                    if (screen == Screen.Timer) Vm.Controller.CloseTimer();
                    else if (screen == Screen.Observations) Vm.Controller.CloseObservations();
                    break;
                case "Timer":
                    _ = Vm.Controller.OpenTimerAsync();
                    break;
                case "Photos":
                    _ = Vm.Controller.OpenObservationsAsync();
                    break;
            }
            Refresh(); // a toggle button checks itself on click; the screen decides
        }
    }
}
