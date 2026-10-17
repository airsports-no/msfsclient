using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using msfsclient.Contracts.Services;
using msfsclient.Core.Api;
using msfsclient.Core.App;
using msfsclient.Core.History;
using msfsclient.Core.Hud;
using msfsclient.Core.Observations;
using msfsclient.Core.Preflight;
using msfsclient.Core.Tracking;
using msfsclient.Core.Training;
using msfsclient.Core.Ui;
using msfsclient.Services;

namespace msfsclient.ViewModels
{
    /// <summary>
    /// The one view model of the window: a thin wrapper around the Core <see cref="AppController"/> (screens and server data)
    /// and the <see cref="TrackingSession"/> (positions), plus the simulator connection. Every change raises a single
    /// PropertyChanged for all properties; the views bind one way to the read-only properties below.
    /// </summary>
    public sealed partial class ShellViewModel : ObservableObject
    {
        private readonly AppController _controller;
        private readonly ISimConnectService _simConnect;
        private readonly SimPositionSource _sim;
        private readonly PositionQueue _queue;
        private readonly TraccarUploader _uploader;
        private readonly TrackLog _trackLog;
        private readonly DispatcherQueue _dispatcher;
        private readonly DispatcherTimer _pump;
        private readonly DispatcherTimer _tick;

        private UiState _state;
        private TrackingSession? _session;
        private TrackingState _tracking = new();
        private long _nowMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        private long _lastPollMillis;
        private long _lastFlightOrderPollMillis;
        private long _lastFlightNotificationPollMillis;
        private long _lastConnectAttemptMillis;
        private long _lastAnnouncementCheckMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        private readonly NotificationService _notifications;
        private readonly SimMessageTracker _simMessages = new();
        private HudServer? _hud;
        private bool _autoStopHandled;
        private IntPtr _windowHandle;

        public ShellViewModel(
            AppController controller, ISimConnectService simConnect, SimPositionSource sim, PositionQueue queue, TraccarUploader uploader,
            TrackLog trackLog, PlatformServices platform, IAppConfigService config, NotificationService notifications)
        {
            _notifications = notifications;
            _controller = controller;
            _simConnect = simConnect;
            _sim = sim;
            _queue = queue;
            _uploader = uploader;
            _trackLog = trackLog;
            Platform = platform;
            Config = config;
            _dispatcher = DispatcherQueue.GetForCurrentThread();
            _state = controller.State;
            _tracking = new TrackingState { Backlog = queue.Size() };

            // The newest state, read on the UI thread: changes made at the same time on different threads can reach the UI
            // thread out of order, and an older one arriving last would stay on screen (a photo mark not shown, for example).
            controller.StateChanged += carried => _dispatcher.TryEnqueue(() => { _state = controller.State; EnsureSession(); Changed(); });
            simConnect.ConnectionStatusChanged += (_, _) => _dispatcher.TryEnqueue(Changed);
            // An announcement that arrives while the pilot is in another window (MSFS) gets a Windows notification; in front, Home shows it.
            controller.NewAnnouncement += a => _dispatcher.TryEnqueue(() => { if (!WindowActive) _notifications.ShowAnnouncement(a); });
            notifications.Activated += () => _dispatcher.TryEnqueue(() => BringToFront?.Invoke());
            // Flight notifications (what the phones get by push): a Windows notification, and a line in the simulator.
            controller.FlightNotification += n => _dispatcher.TryEnqueue(() =>
            {
                _notifications.ShowFlightNotification(n);
                if (SimMessagesEnabled && _simConnect.IsConnected)
                {
                    var kind = n.Kind switch
                    {
                        "not_tracking" or "still_tracking" => Core.Hud.SimMessageKind.Warning,
                        "scoring_started" or "flight_scored" => Core.Hud.SimMessageKind.Good,
                        _ => Core.Hud.SimMessageKind.Info,
                    };
                    _simConnect.ShowText($"Air Sports: {n.Title}. {n.Body}", kind, 15);
                }
            });
            // "Go flying" starts tracking at once (immediate tracking): the pilot does not need to find the play button.
            controller.StartTrackingRequested += () => _dispatcher.TryEnqueue(() =>
            {
                if (!_tracking.Running && CanStartTracking) StartTracking();
            });
            // The practice flight's flight order opens in the browser (a PDF download) as soon as the server has made it.
            controller.FlightOrderReady += url => _dispatcher.TryEnqueue(() =>
            {
                Platform.OpenUri(url);
                if (SimMessagesEnabled && _simConnect.IsConnected)
                    _simConnect.ShowText("Air Sports: your flight order is open in your browser", Core.Hud.SimMessageKind.Info, 10);
            });

            // SimConnect delivers its messages through a pump on the UI thread (see GEMINI.md).
            _pump = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _pump.Tick += (_, _) => _simConnect.ReceiveMessage();
            _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _tick.Tick += (_, _) => OnTick();
        }

        public PlatformServices Platform { get; }
        public IAppConfigService Config { get; }
        public AppController Controller => _controller;
        public UiState State => _state;
        public TrackingState Tracking => _tracking;
        public TrackLog TrackLog => _trackLog;
        public SimPositionSource Sim => _sim;
        public long NowMillis => _nowMillis;

        /// <summary>Raised when the screen changes, so the window can swap the page and move focus.</summary>
        public event Action<Screen>? ScreenChanged;

        /// <summary>Raised for a stop that the pilot did not ask for (the 12 hour limit).</summary>
        public event Action? AutoStopped;

        // ---- In-sim display and simulator messages --------------------------------------------------------------------

        /// <summary>Settings kept on this PC (set by the app; null in tests).</summary>
        public AppSettings? Settings { get; set; }

        public bool SimMessagesEnabled
        {
            get => Settings?.SimMessages ?? true;
            set
            {
                if (Settings != null) Settings.SimMessages = value;
                Changed();
            }
        }

        /// <summary>The last line printed on the simulator's screen (debug view).</summary>
        public string? LastSimMessage { get; private set; }

        /// <summary>The local server for in-sim displays and a second screen; started by the app.</summary>
        public HudServer? Hud => _hud;

        public string HudUrl => _hud is { Running: true } hud ? $"http://localhost:{hud.Port}/" : "";

        public void AttachHud(HudServer hud) => _hud = hud;

        /// <summary>
        /// The flight status as of now, built fresh (no shared cache), so the display server can call it from its own threads;
        /// the state records it reads are immutable.
        /// </summary>
        public FlightStatus FreshStatus() =>
            new(_state, _tracking, _simConnect.IsConnected, _sim.Last?.TimestampMillis, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        /// <summary>Runs display commands on the UI thread, where tracking and SimConnect live.</summary>
        public sealed class UiThreadHudHost(DispatcherQueue dispatcher, IHudHost inner) : IHudHost
        {
            public HudSnapshot Snapshot() => inner.Snapshot();

            public Task<HudResult> ExecuteAsync(HudCommand command)
            {
                var done = new TaskCompletionSource<HudResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                var queued = dispatcher.TryEnqueue(async () =>
                {
                    try { done.SetResult(await inner.ExecuteAsync(command)); }
                    catch (Exception e) { done.SetResult(new HudResult(false, e.Message)); }
                });
                if (!queued) done.SetResult(new HudResult(false, "The client is closing."));
                return done.Task;
            }
        }

        // ---- Practice flight (quickstart) -----------------------------------------------------------------------------

        public bool SimConnected => _simConnect.IsConnected;

        /// <summary>
        /// Puts the aircraft at the practice flight's start: on the runway, or in the air before the starting point. Returns why
        /// it did not, or null when it did. Not while tracking: the jump would end up in the recorded and scored flight.
        /// </summary>
        public string? MoveToQuickstart(bool inTheAir)
        {
            var start = inTheAir ? _state.Quickstart?.AirStart : _state.Quickstart?.GroundStart;
            if (start == null) return "This practice flight has no such start.";
            if (!_simConnect.IsConnected) return "Start MSFS and load a flight first; then the client can move your aircraft.";
            // Tracking already runs after "Go flying": pause it around the move, so no position is sent from the moment the
            // aircraft jumps, and start it again once it is settled at the start.
            var resume = _tracking.Running;
            if (resume) _session?.Stop();
            var moved = _simConnect.MoveAircraft(start.Latitude, start.Longitude, start.AltitudeFt ?? 0, start.Heading ?? 0,
                onGround: !inTheAir, airspeedKnots: inTheAir ? 100u : 0u);
            if (resume) _ = ResumeTrackingAfterMoveAsync(++_trackingChanges);
            return moved ? null : "The simulator did not move the aircraft. Choose the start in the world map instead.";
        }

        /// <summary>
        /// Counts the pilot's own tracking changes (start, stop, sign-out). The resume after moving the aircraft only happens
        /// when nothing changed in between: a pilot who stops tracking, or signs out, during the pause keeps it that way.
        /// </summary>
        private int _trackingChanges;

        private async Task ResumeTrackingAfterMoveAsync(int changes)
        {
            await Task.Delay(TimeSpan.FromSeconds(3)); // continues on the UI thread
            if (changes == _trackingChanges && !_tracking.Running && CanStartTracking) StartTracking();
        }

        /// <summary>The pilot clicked a notification: the window should come to the front.</summary>
        public event Action? BringToFront;

        /// <summary>The window is in front (set by the window); notifications are only for when it is not.</summary>
        public bool WindowActive { get; set; } = true;

        public void Start(IntPtr windowHandle)
        {
            _windowHandle = windowHandle;
            _trackLog.CloseAbandonedSessions(); // a session left open by a crash ends at its last fix
            _pump.Start();
            _tick.Start();
            ConnectSimulator();
            EnsureSession();
            _ = _controller.Start();
        }

        public void Stop()
        {
            _hud?.Dispose();
            _notifications.Unregister();
            _session?.Stop();
            _pump.Stop();
            _tick.Stop();
            _simConnect.Disconnect();
        }

        private Screen _lastScreen = Screen.Loading;

        private void Changed()
        {
            OnPropertyChanged(string.Empty);
            _hud?.Publish();
            if (_state.Screen != _lastScreen)
            {
                _lastScreen = _state.Screen;
                _historySummary = null;
                ScreenChanged?.Invoke(_state.Screen);
            }
        }

        /// <summary>One tracking session per simulator tracking id (the profile's <c>simulator_tracking_id</c>).</summary>
        private void EnsureSession()
        {
            var deviceId = _state.Person?.SimulatorTrackingId;
            if (string.IsNullOrWhiteSpace(deviceId) || _session?.DeviceId == deviceId) return;
            _session?.Stop();
            var session = new TrackingSession(_sim, _queue, _uploader, deviceId, trackLog: _trackLog);
            session.StateChanged += carried => _dispatcher.TryEnqueue(() =>
            {
                if (!ReferenceEquals(session, _session)) return;
                // The newest state, not the one this change carried (see the controller's StateChanged above).
                var s = session.State;
                _tracking = s;
                if (s.AutoStopped && !_autoStopHandled)
                {
                    _autoStopHandled = true;
                    AutoStopped?.Invoke();
                    _ = _controller.TrackingAutoStoppedAsync();
                }
                Changed();
            });
            _session = session;
            _tracking = session.State;
        }

        private void OnTick()
        {
            _nowMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            // Reconnect to the simulator every 10 s while it is not running (it may be started after the client).
            if (!_simConnect.IsConnected && _nowMillis - _lastConnectAttemptMillis >= 10_000) ConnectSimulator();
            // The in-sim display lists the photos while the client shows something else.
            if (_hud is { ClientCount: > 0 }) _ = _controller.EnsureObservationsAsync();
            // Lines on the simulator's screen for what just happened (tracking on/off, reception, gates, the start minute).
            var messages = _simMessages.Next(Status);
            if (SimMessagesEnabled && _simConnect.IsConnected)
            {
                foreach (var m in messages)
                {
                    _simConnect.ShowText(m.Text, m.Kind, m.Seconds);
                    LastSimMessage = m.Text;
                }
            }
            // No push on the desktop: look for new announcements every 5 minutes while signed in.
            if (!_state.IsOutside && _nowMillis - _lastAnnouncementCheckMillis >= 5 * 60_000)
            {
                _lastAnnouncementCheckMillis = _nowMillis;
                _ = _controller.RefreshAnnouncementsAsync();
            }
            // While tracking, poll the server so the pilot sees whether positions arrive: every 10 s, 5 s in training mode on a
            // flight that offers it (the gate feedback comes with the poll).
            var interval = _state.TrainingMode && GateFeedback.TrainingAvailable(_state.Tasks) ? 5_000 : 10_000;
            if (_tracking.Running && _state.Screen is Screen.Home or Screen.Timer or Screen.Observations && _nowMillis - _lastPollMillis >= interval)
            {
                _lastPollMillis = _nowMillis;
                _ = _controller.RefreshTasksAsync();
            }
            // No push on the desktop: look for new flight notifications every minute while signed in.
            if (!_state.IsOutside && _nowMillis - _lastFlightNotificationPollMillis >= 60_000)
            {
                _lastFlightNotificationPollMillis = _nowMillis;
                _ = _controller.PollFlightNotificationsAsync();
            }
            // Just registered for the practice flight: look for its flight order every 5 s until it is made.
            if (_state.AwaitingFlightOrderFor != null && _nowMillis - _lastFlightOrderPollMillis >= 5_000)
            {
                _lastFlightOrderPollMillis = _nowMillis;
                _ = _controller.PollFlightOrderAsync();
            }
            Changed();
        }

        public void ConnectSimulator()
        {
            _lastConnectAttemptMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (_windowHandle == IntPtr.Zero || _simConnect.IsConnected) return;
            try { _simConnect.Connect(_windowHandle); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"SimConnect connect failed: {ex.Message}"); }
        }

        // ---- Tracking ----------------------------------------------------------------------------------------------

        public bool CanStartTracking => Status.CanStartTracking;

        public void StartTracking()
        {
            _trackingChanges++;
            EnsureSession();
            if (_session == null) return;
            _autoStopHandled = false;
            _lastPollMillis = 0;
            if (!_simConnect.IsConnected) ConnectSimulator();
            _session.Start();
        }

        /// <summary>The pilot confirmed Stop: stop, offer the flight just recorded, and leave the now pointless timer.</summary>
        public void StopTracking()
        {
            _trackingChanges++;
            _session?.Stop();
            _ = _controller.TrackingStoppedAsync();
            if (_state.Screen == Screen.Timer) _controller.CloseTimer();
        }

        /// <summary>Signing out or deleting the account stops tracking first.</summary>
        public void SignOut()
        {
            _trackingChanges++;
            _session?.Stop();
            _controller.SignOut();
        }

        public async Task DeleteAccountAsync()
        {
            _trackingChanges++;
            _session?.Stop();
            await _controller.DeleteAccountAsync();
        }

        /// <summary>The flight named in the stop confirmation ("Contest - Task").</summary>
        public string? FlightLabel => Status.FlightLabel;

        public string? TrackedFor => _tracking.StartedAtMillis is { } started ? TimeFormat.TrackedFor(_nowMillis - started) : null;

        // ---- Clock ---------------------------------------------------------------------------------------------------

        /// <summary>The PC clock against network time (positions are stamped with the PC clock).</summary>
        public ClockStatus Clock => _state.NetworkClockOffsetMillis is { } offset
            ? ClockCheck.Status(offset, ClockReference.Network, _state.NetworkClockUncertaintyMillis)
            : new ClockStatus.Unknown();

        public SimStatus SimStatus => Status.Sim;

        // ---- Top bar ---------------------------------------------------------------------------------------------------

        public bool ShowAvatar => _state.Person != null && !_state.IsOutside;

        public bool ShowBack => _state.Screen is Screen.Map or Screen.Flights or Screen.Profile or Screen.Timer or Screen.Observations
            or Screen.Preflight or Screen.History or Screen.Debug;

        /// <summary>Back from wherever the pilot is (the window's Back button, Escape, the mouse back button).</summary>
        public void Back()
        {
            switch (_state.Screen)
            {
                case Screen.Map: _controller.CloseMap(); break;
                case Screen.Flights: _controller.CloseFlights(); break;
                case Screen.Profile: _controller.CloseProfile(); break;
                case Screen.Timer: _controller.CloseTimer(); break;
                case Screen.Observations: _controller.CloseObservations(); break;
                case Screen.Preflight: _controller.ClosePreflight(); break;
                case Screen.History: _controller.CloseHistory(); break;
                case Screen.Debug: _controller.CloseDebug(); break;
                case Screen.Tour: _controller.FinishTour(); break;
            }
        }

        public string Initials
        {
            get
            {
                var p = _state.Person;
                var initials = string.Concat(new[] { p?.FirstName, p?.LastName }.Where(s => !string.IsNullOrEmpty(s)).Select(s => char.ToUpperInvariant(s![0])));
                return initials.Length > 0 ? initials : p?.Email is { Length: > 0 } e ? char.ToUpperInvariant(e[0]).ToString() : "?";
            }
        }

        public string? PictureUrl => Urls.AbsoluteUrl(_state.Person?.Picture);

        public string VersionLine => $"Airsports {_controller.AppVersionLabel}";

        // ---- Dock ------------------------------------------------------------------------------------------------------

        public bool ShowDock => _state.Person != null && _state.Screen is Screen.Home or Screen.Timer or Screen.Observations;

        public bool DockCompact => _state.Screen != Screen.Home;

        public bool ShowTimerTab => _state.ActiveContestants.Any(c => c.AdaptiveStart);

        public bool ShowPhotosTab => _state.ActiveContestants.Any(c => c.ObservationRegistration == true);

        public bool ShowDeckSwitch => ShowTimerTab || ShowPhotosTab;

        public string PhotosTabLabel => _state.Observations.Counter is { } counter ? $"Photos  {counter}" : "Photos";

        /// <summary>The flight in progress as the dock, the HUD and the simulator messages see it; worked out once per change.</summary>
        public FlightStatus Status
        {
            get
            {
                if (_status == null || !ReferenceEquals(_statusState, _state) || !ReferenceEquals(_statusTracking, _tracking) || _statusNow != _nowMillis)
                {
                    _status = new FlightStatus(_state, _tracking, _simConnect.IsConnected, _sim.Last?.TimestampMillis, _nowMillis);
                    (_statusState, _statusTracking, _statusNow) = (_state, _tracking, _nowMillis);
                }
                return _status;
            }
        }

        private FlightStatus? _status;
        private UiState? _statusState;
        private TrackingState? _statusTracking;
        private long _statusNow;

        public bool TrainingOn => Status.TrainingOn;

        public bool ShowGateBanner => TrainingOn && _tracking.Running;

        public GateFeedback? Gate => Status.Gate;

        /// <summary>The status lines under the play/stop button, in order, with how each is shown.</summary>
        public IReadOnlyList<StatusLine> DockLines => Status.Lines(DockCompact);

        // ---- Home ------------------------------------------------------------------------------------------------------

        public string Greeting => $"Hello, {new[] { _state.Person?.FirstName, _state.Person?.Email }.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "pilot"}";

        private UiState? _targetsFor;
        private TrackingTargets? _targets;

        /// <summary>What the flight strips show; worked out once per state.</summary>
        public TrackingTargets Targets
        {
            get
            {
                if (_targets == null || !ReferenceEquals(_targetsFor, _state))
                {
                    _targets = TrackingTargets.Of(_state.Tasks, _state.Upcoming, _state.Contests, t => TimeFormat.FormatTimeLocal(t), t => TimeFormat.FormatLocal(t));
                    _targetsFor = _state;
                }
                return _targets;
            }
        }

        public string StripHeader
        {
            get
            {
                var t = Targets;
                if (t.Now.Count > 0) return _tracking.Running ? "Tracking for" : "You will track";
                if (t.Next != null) return "Next flight, not active yet";
                return _state.HomeLoaded ? "No flight scheduled" : "Checking your flights…";
            }
        }

        public bool ShowLiveDot => _tracking.Running && Targets.Now.Count > 0;

        public bool ShowNoFlightNote => _state.HomeLoaded && Targets.Now.Count == 0 && Targets.Next == null;

        /// <summary>The same rule as on My flights: the server's verdict (or the task's flag) says whether the pilot may cancel.</summary>
        public MyFlight? Cancellable(TrackingTarget t) => _state.Upcoming.FirstOrDefault(f => f.Id == t.FlightId && f.CanCancel());

        public IReadOnlyList<PreflightCheck> Checks => PreflightChecks.Of(
            _state.Tasks.Count > 0 || _state.Upcoming.Count > 0, Platform.Online(), Clock, SimStatus);

        public string PreflightSummary => PreflightChecks.Summary(Checks);

        private string? _historySummary;

        public string HistorySummary
        {
            get
            {
                if (_historySummary == null)
                {
                    var n = _trackLog.Sessions().Count;
                    _historySummary = n == 0 ? "Nothing recorded yet" : $"{n} recorded";
                }
                return _historySummary;
            }
        }

        public bool TrainingAvailable => GateFeedback.TrainingAvailable(_state.Tasks);

        // ---- Observations ----------------------------------------------------------------------------------------------

        /// <summary>The latest simulator position, when it is fresh enough to stamp a mark with.</summary>
        public ObservationFix? FreshFix
        {
            get
            {
                if (!_tracking.Running || _tracking.LastFixMillis is not { } time || _tracking.LastLatitude is not { } lat || _tracking.LastLongitude is not { } lon) return null;
                return _nowMillis - time <= ObservationRules.FixMaxAgeMillis ? new ObservationFix(lat, lon, time) : null;
            }
        }

        public string? ObservationBlocker => ObservationRules.Blocker(_state.Observations, _tracking.Running, FreshFix != null, _nowMillis);

        public void MarkObservation(ObservationPhoto photo)
        {
            if (FreshFix is { } fix) _ = _controller.MarkObservationAsync(photo, fix);
        }
    }
}
