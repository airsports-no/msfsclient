using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using msfsclient.Core.Api;
using msfsclient.Core.App;
using msfsclient.Core.Tracking;
using msfsclient.Helpers;

namespace msfsclient.Views
{
    public sealed partial class HomeView : ShellPart
    {
        private TrackingTargets? _stripsFor;
        private bool _stripsRunning;
        private int? _stripsCancelling;
        private IReadOnlyList<Announcement>? _announcementsFor;

        public HomeView()
        {
            this.InitializeComponent();
        }

        public IReadOnlyList<StripItem> Strips { get; private set; } = [];

        public IReadOnlyList<AnnouncementItem> Announcements { get; private set; } = [];

        public string PreflightName => $"Pre-flight: {Vm.PreflightSummary}";

        /// <summary>The lists are rebuilt only when what they show changed, so the 1 s refresh does not redraw them.</summary>
        protected override void Refresh()
        {
            var targets = Vm.Targets;
            var state = Vm.State;
            if (!ReferenceEquals(targets, _stripsFor) || _stripsRunning != Vm.Tracking.Running || _stripsCancelling != state.CancellingFlightId)
            {
                _stripsFor = targets;
                _stripsRunning = Vm.Tracking.Running;
                _stripsCancelling = state.CancellingFlightId;
                Strips = targets.Now.Count > 0
                    ? targets.Now.Select(t => Strip(t, live: true)).ToList()
                    : targets.Next is { } next ? [Strip(next, live: false)] : [];
            }
            if (!ReferenceEquals(state.Announcements, _announcementsFor))
            {
                _announcementsFor = state.Announcements;
                Announcements = state.Announcements.Select(AnnouncementItem.From).ToList();
            }
            if (TrainingSwitch.IsOn != state.TrainingMode) TrainingSwitch.IsOn = state.TrainingMode;
            RefreshQuickstartSteps();
        }

        // ---- Practice flight (quickstart) ---------------------------------------------------------------------------

        private Quickstart? Offer => Vm.State.QuickstartOffer;

        public bool QuickstartVisible => Offer != null;

        public bool QuickstartRegistered => Offer?.RegisteredFlight != null;

        public string QuickstartHeading => QuickstartRegistered ? "You are registered for your practice flight" : "New here? Try a practice flight";

        public string QuickstartTitle => Offer?.Title ?? "";

        public string QuickstartDescription => Offer?.Description ?? "";

        public string QuickstartStart => Offer?.StartLocation ?? "";

        public string QuickstartStepsHeading => QuickstartRegistered ? "What happens now" : "When you press Go flying";

        public string GoFlyingLabel => Vm.State.QuickstartRegistering ? "Registering…" : "Go flying";

        public string? QuickstartError => Vm.State.QuickstartError;

        private string? FlightOrderPath => Offer?.RegisteredFlight?.FlightOrderPath;

        public bool QuickstartCanOpenOrder => !string.IsNullOrEmpty(FlightOrderPath);

        private string _stepsKey = "";

        /// <summary>The steps, rebuilt only when their text changed (Refresh runs every second).</summary>
        public IReadOnlyList<QuickstartStep> QuickstartSteps { get; private set; } = [];

        private void RefreshQuickstartSteps()
        {
            var steps = Offer is { } q ? BuildSteps(q) : [];
            var key = string.Join("\n", steps.Select(s => s.Marker + s.Text));
            if (key == _stepsKey) return;
            _stepsKey = key;
            QuickstartSteps = steps;
        }

        /// <summary>
        /// Tells the pilot what happens: before registering, what "Go flying" does for them (the defaults, the flight order
        /// opening by itself, where to start, the adaptive start, training mode); afterwards where they are and what is next.
        /// </summary>
        private List<QuickstartStep> BuildSteps(Quickstart q)
        {
            var next = Ui.Brush("IndigoBrush");
            var done = Ui.Brush("LiveGreenBrush");
            var muted = Ui.Brush("MutedTextBrush");
            var where = string.IsNullOrWhiteSpace(q.StartLocation) ? "the start" : q.StartLocation;
            var organizer = string.IsNullOrWhiteSpace(q.StartInstructions) ? "" : " " + q.StartInstructions.Trim();
            var steps = new List<QuickstartStep>();
            if (q.RegisteredFlight is not { } flight)
            {
                var speed = q.AirSpeed is { } kt ? $"{kt:0} knots" : "a typical cruise speed";
                var team = q.AircraftRegistration is { Length: > 0 } reg && q.ClubName is { Length: > 0 } club
                    ? $", as aircraft {reg} of the club {club}"
                    : "";
                steps.Add(new("1", $"The client registers you for this flight at {speed} with no wind{team}. Nothing to fill in; " +
                    "\"Choose speed and aircraft\" lets you set your own instead.", next));
                steps.Add(new("2", "Your flight order, with the map and the time to be at each gate, opens in your browser by itself " +
                    "after a minute or so. It is e-mailed to you as well. Keep it at hand while you fly.", next));
                steps.Add(new("3", $"In MSFS, start a flight at {where}, or load any flight and let the client move your aircraft there.{organizer}", next));
                steps.Add(new("4", "Press play at the bottom to start tracking, then take off and fly the route. The start is adaptive: " +
                    "your time starts when you cross the starting line, whenever that is in the next two hours.", next));
                steps.Add(new("5", "Training mode is switched on: after each gate the bar at the bottom tells you how many seconds early " +
                    "or late you were, so you can correct as you fly.", next));
                return steps;
            }
            var how = flight.AdaptiveStart == true ? " with an adaptive start" : "";
            var at = flight.AirSpeed is { } s ? $" at {s:0} knots" : "";
            steps.Add(new("✓", $"You are registered{at}{how}.", done));
            if (QuickstartCanOpenOrder)
                steps.Add(new("✓", "Your flight order is ready and was opened in your browser. \"Flight order\" opens it again; it is in your e-mail too.", done));
            else if (Vm.State.AwaitingFlightOrderFor == flight.Id)
                steps.Add(new("…", "Your flight order is being made. It opens in your browser by itself as soon as it is ready, usually within a minute.", next));
            else if (Vm.State.FlightOrderSlow)
                steps.Add(new("!", "The flight order is taking longer than usual. It is e-mailed to you when it is ready, and a Flight order button appears here.", muted));
            else
                steps.Add(new("2", "Your flight order is e-mailed to you when it is ready, and a Flight order button appears here.", next));
            var tracking = Vm.Tracking.Running;
            if (!tracking)
            {
                steps.Add(Vm.SimConnected
                    ? new("3", $"Load a flight at {where}, or use the buttons below to put your aircraft on the runway or in the air before the starting point.{organizer}", next)
                    : new("3", $"Start MSFS and load a flight at {where}, or load any flight and let the client move your aircraft there.{organizer}", next));
            }
            steps.Add(tracking
                ? new("✓", "Tracking is on. Fly the route: your time starts when you cross the starting line.", done)
                : new("4", "Press play at the bottom to start tracking before you take off. Your time starts when you cross the starting line.", next));
            if (Vm.State.TrainingMode)
                steps.Add(new("5", "Training mode is on: after each gate the bar at the bottom shows how many seconds early or late you were.", next));
            return steps;
        }

        /// <summary>Moving the aircraft is offered once registered and while the simulator is connected (and not while tracking).</summary>
        public bool QuickstartCanGround => QuickstartRegistered && Vm.SimConnected && !Vm.Tracking.Running && Offer?.GroundStart != null;

        public bool QuickstartCanAir => QuickstartRegistered && Vm.SimConnected && !Vm.Tracking.Running && Offer?.AirStart != null;

        private void QuickstartGoFlying_Click(object sender, RoutedEventArgs e) => _ = Vm.Controller.RegisterQuickstartAsync();

        /// <summary>Flight orders are a PDF download: the browser handles that better than the embedded page.</summary>
        private void QuickstartFlightOrder_Click(object sender, RoutedEventArgs e)
        {
            if (FlightOrderPath is { } path) Vm.Platform.OpenUri(Urls.SiteUrl(path));
        }

        private void QuickstartRegister_Click(object sender, RoutedEventArgs e) => Vm.Controller.OpenQuickstartRegistration();

        private void QuickstartDismiss_Click(object sender, RoutedEventArgs e) => Vm.Controller.DismissQuickstart();

        private async void QuickstartGround_Click(object sender, RoutedEventArgs e)
        {
            if (Offer?.GroundStart is not { } start) return;
            if (!await Dialogs.ConfirmAsync(XamlRoot, "Move your aircraft to the runway?",
                    $"Your aircraft is placed on the runway at {Offer.StartLocation}, ready for take-off{(start.Heading is { } h ? $" (heading {h:0}°)" : "")}. " +
                    "Do this before you start tracking.", "Move", "Cancel"))
            {
                return;
            }
            ShowQuickstartResult(Vm.MoveToQuickstart(inTheAir: false) ?? "Your aircraft is on the runway. Press play to start tracking, then take off.");
        }

        private async void QuickstartAir_Click(object sender, RoutedEventArgs e)
        {
            if (Offer?.AirStart is not { } start) return;
            if (!await Dialogs.ConfirmAsync(XamlRoot, "Start in the air?",
                    $"Your aircraft is placed 2 NM before the starting point at {start.AltitudeFt ?? 0:0} ft, flying towards it at 100 knots. " +
                    "Take the controls straight away, then press play to start tracking.",
                    "Move", "Cancel"))
            {
                return;
            }
            ShowQuickstartResult(Vm.MoveToQuickstart(inTheAir: true) ?? "Your aircraft is in the air before the starting point. Press play to start tracking.");
        }

        private void ShowQuickstartResult(string text)
        {
            QuickstartResult.Text = text;
            QuickstartResult.Visibility = Visibility.Visible;
        }

        private StripItem Strip(TrackingTarget t, bool live) =>
            new(t, live, Vm.Cancellable(t), Vm.State.CancellingFlightId != null && Vm.State.CancellingFlightId == t.FlightId);

        private static StripItem? Item(object sender) => (sender as FrameworkElement)?.Tag as StripItem;

        private void DismissNotice_Click(object sender, RoutedEventArgs e) => Vm.Controller.DismissHomeNotice();

        private void DismissAnnouncement_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is int id) Vm.Controller.DismissAnnouncement(id);
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) => _ = Vm.Controller.RefreshAsync();

        private void Share_Click(object sender, RoutedEventArgs e)
        {
            if (Item(sender)?.Target.MapLink is not { } link) return;
            var text = $"Follow my flight live: {Urls.SiteUrl(link)}";
            if (!Vm.Platform.ShareText("Follow my flight live", text))
            {
                _ = Dialogs.InfoAsync(XamlRoot, "Link copied", "The live link is on the clipboard. Paste it where you want to share it.");
            }
        }

        private void LiveMap_Click(object sender, RoutedEventArgs e) => Vm.Controller.OpenMap(Item(sender)?.Target.MapLink);

        /// <summary>Flight orders are a PDF download: the browser handles that better than the embedded page.</summary>
        private void FlightOrders_Click(object sender, RoutedEventArgs e)
        {
            if (Item(sender)?.Target.FlightOrderPath is { } path) Vm.Platform.OpenUri(Urls.SiteUrl(path));
        }

        private void Directions_Click(object sender, RoutedEventArgs e)
        {
            if (Item(sender)?.Target.DirectionsUrl is { } url) Vm.Platform.OpenUri(url);
        }

        private void Website_Click(object sender, RoutedEventArgs e)
        {
            if (Item(sender)?.Target.WebsiteUrl is { } url) Vm.Platform.OpenUri(url);
        }

        private async void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (Item(sender)?.Cancellable is not { } flight) return;
            if (await FlightDialogs.ConfirmCancelAsync(XamlRoot, flight, Vm.NowMillis)) _ = Vm.Controller.CancelFlightAsync(flight);
        }

        private void Preflight_Click(object sender, RoutedEventArgs e) => _ = Vm.Controller.OpenPreflightAsync();

        private void Flights_Click(object sender, RoutedEventArgs e) => _ = Vm.Controller.OpenFlightsAsync();

        private void History_Click(object sender, RoutedEventArgs e) => Vm.Controller.OpenHistory();

        private void Today_Click(object sender, RoutedEventArgs e) => Vm.Controller.OpenToday();

        private void Training_Toggled(object sender, RoutedEventArgs e)
        {
            if (TrainingSwitch.IsOn != Vm.State.TrainingMode) Vm.Controller.SetTrainingMode(TrainingSwitch.IsOn);
        }

        private void TrainingHelp_Click(object sender, RoutedEventArgs e) => _ = Dialogs.InfoAsync(XamlRoot, "Training mode",
            "With training mode on, the client tells you how well you kept your timing. Each time you pass a gate, the bar at the bottom " +
            "shows the gate and how many seconds early or late you were against its required time, so you can adjust while you are still flying.\n\n" +
            "It is available only where the organizer allows live feedback, and it is off every time the client starts. " +
            "The result can lag a few seconds behind the gate.");

        private void Dashboard_Click(object sender, RoutedEventArgs e) => Vm.Platform.OpenUri(Urls.DashboardUrl);

        private void Terms_Click(object sender, RoutedEventArgs e) => Vm.Platform.OpenUri(Urls.TermsAndPrivacyUrl);
    }
}
