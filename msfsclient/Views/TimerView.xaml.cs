using System;
using System.Linq;
using Microsoft.UI.Xaml;
using msfsclient.Core.App;
using msfsclient.Core.Tracking;
using msfsclient.Core.Training;
using msfsclient.Core.Ui;
using msfsclient.Helpers;

namespace msfsclient.Views
{
    /// <summary>
    /// The Timer page: the adaptive-start timer and, in training mode, the countdown to the next gate's required time. With
    /// both, the one the pilot chose is large and the other small above it; clicking the small one swaps them.
    ///
    /// The start timer shows the PC clock (what this client stamps positions with) to the second, so the pilot can see
    /// when to press Start relative to when they plan to cross the starting line; after Start a large m:ss counts down to,
    /// and then up from, the nearest whole minute of the moment Start was pressed (plus the countdown buffer).
    /// </summary>
    public sealed partial class TimerView : ShellPart
    {
        private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(50) };

        public TimerView()
        {
            this.InitializeComponent();
            _clock.Tick += (_, _) => Draw();
            Loaded += (_, _) => _clock.Start();
            Unloaded += (_, _) => _clock.Stop();
        }

        private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        /// <summary>The contest's own time zone (flight orders give times in it); the PC's when unknown.</summary>
        private TimeZoneInfo Zone
        {
            get
            {
                var s = Vm.State;
                var contestId = (s.ActiveContestants.FirstOrDefault(c => c.AdaptiveStart) ?? s.ActiveContestants.FirstOrDefault())?.ContestId;
                return TimeFormat.ZoneOrDevice(contestId is { } id && s.Contests.TryGetValue(id, out var contest) ? contest.TimeZone : null);
            }
        }

        /// <summary>The flight has an adaptive start: the start timer is shown.</summary>
        private bool ShowStartTimer => Vm.State.Tasks.Any(t => t.ActiveContestants.Any(c => c.AdaptiveStart));

        /// <summary>Training mode is on for a flight that supports it: the gate countdown is shown.</summary>
        private bool ShowGateCountdown => Vm.State.TrainingMode && GateFeedback.TrainingAvailable(Vm.State.Tasks);

        /// <summary>The timer shown large: with only one of the two, that one whatever the setting says.</summary>
        private TimerFocus Large => !ShowGateCountdown ? TimerFocus.Start : !ShowStartTimer ? TimerFocus.Gate : Vm.State.TimerFocus;

        /// <summary>The timer shown small, or null when there is only one.</summary>
        private TimerFocus? Small => ShowStartTimer && ShowGateCountdown ? (Large == TimerFocus.Start ? TimerFocus.Gate : TimerFocus.Start) : null;

        protected override void Refresh()
        {
            var s = Vm.State;
            var running = s.TimerOriginMillis != null;
            var startLarge = Large == TimerFocus.Start;
            ResetButton.Visibility = ShowStartTimer && running ? Visibility.Visible : Visibility.Collapsed;
            BeforeStart.Visibility = startLarge && !running ? Visibility.Visible : Visibility.Collapsed;
            Running.Visibility = startLarge && running ? Visibility.Visible : Visibility.Collapsed;
            GateLarge.Visibility = startLarge ? Visibility.Collapsed : Visibility.Visible;
            SmallTimer.Visibility = Small != null ? Visibility.Visible : Visibility.Collapsed;
            StartButton.Visibility = ShowStartTimer && !running ? Visibility.Visible : Visibility.Collapsed;

            switch (Vm.Clock)
            {
                case ClockStatus.Drift d:
                    ClockOk.Visibility = Visibility.Collapsed;
                    ClockDrift.Visibility = Visibility.Visible;
                    DriftTitle.Text = $"Your PC clock is {d.Seconds} s {(d.ClockIsAhead ? "ahead of" : "behind")} network time.";
                    DriftText.Text = $"This PC stamps your positions with its own clock, so your start and gate times would be scored {d.Seconds} s off. " +
                        "Turn on Set time automatically and press Sync now in the Windows date and time settings before you fly.";
                    break;
                case ClockStatus.InSync:
                    ClockOk.Visibility = Visibility.Visible;
                    ClockDrift.Visibility = Visibility.Collapsed;
                    ClockOk.Text = "PC clock matches network time.";
                    break;
                default:
                    ClockOk.Visibility = Visibility.Visible;
                    ClockDrift.Visibility = Visibility.Collapsed;
                    ClockOk.Text = "Clock not verified yet: using PC time until network time is available.";
                    break;
            }

            var buffer = s.TimerBufferMinutes;
            BufferText.Text = buffer == 0 ? "None: the timer starts at the nearest whole minute" : $"The timer starts at -{buffer}:00";
            BufferValue.Text = $"{buffer} min";
            Minus.IsEnabled = buffer > 0;
            Plus.IsEnabled = buffer < AppSettings.MaxTimerBufferMinutes;
            Draw();
        }

        private void Draw()
        {
            var now = Now();
            var zone = Zone;
            var label = $"PC time ({TimeFormat.ZoneLabel(zone)})";
            var origin = Vm.State.TimerOriginMillis;
            var gate = GateCountdown.Next(Vm.State.ActiveContestants);
            if (Small == TimerFocus.Start)
            {
                SmallLabel.Text = "Start timer";
                SmallValue.Text = origin == null ? "Not started" : AdaptiveStartTimer.Format(AdaptiveStartTimer.ElapsedMillis(origin.Value, now));
            }
            else if (Small == TimerFocus.Gate)
            {
                SmallLabel.Text = gate != null ? $"Next gate {gate.Gate}" : "Gate countdown";
                SmallValue.Text = gate != null ? GateCountdown.Format(gate.RemainingMillis(now)) : "Waiting for the next gate";
            }
            if (Large == TimerFocus.Gate)
            {
                DrawGate(gate, now, zone);
                return;
            }
            if (origin == null)
            {
                ClockLabel.Text = label;
                Clock.Text = TimeFormat.FormatClock(now, zone);
                var buffer = Vm.State.TimerBufferMinutes;
                Preview.Text = $"Start now: the timer starts at {TimeFormat.FormatClock(AdaptiveStartTimer.OriginFor(now, buffer), zone)}" + (buffer > 0 ? $" (-{buffer}:00)" : "");
                return;
            }
            var elapsed = AdaptiveStartTimer.ElapsedMillis(origin.Value, now);
            Elapsed.Text = AdaptiveStartTimer.Format(elapsed);
            Elapsed.Foreground = elapsed < 0 ? Ui.Brush("ErrorBrush") : Ui.Brush("TextBrush");
            ElapsedLabel.Text = elapsed < 0 ? "Counting down to your start minute" : "Time since your start minute";
            StartMinute.Text = $"Start minute {TimeFormat.FormatMinute(origin.Value, zone)}";
            ClockLine.Text = $"PC time {TimeFormat.FormatClock(now, zone)}  ({TimeFormat.ZoneLabel(zone)})";
        }

        /// <summary>The countdown to the next gate's required time, large; "+0:04" in red once the required time has passed.</summary>
        private void DrawGate(GateCountdown? gate, long now, TimeZoneInfo zone)
        {
            if (gate == null)
            {
                GateName.Text = "Gate countdown";
                GateRemaining.Visibility = Visibility.Collapsed;
                GateRequired.Visibility = Visibility.Collapsed;
                GateClockLine.Visibility = Visibility.Collapsed;
                GateLabel.Text = "The countdown starts once the next gate's time is known (with an adaptive start: when you cross the starting point).";
                return;
            }
            var remaining = gate.RemainingMillis(now);
            GateRemaining.Visibility = Visibility.Visible;
            GateRequired.Visibility = Visibility.Visible;
            GateClockLine.Visibility = Visibility.Visible;
            GateName.Text = $"Next gate {gate.Gate}";
            GateRemaining.Text = GateCountdown.Format(remaining);
            GateRemaining.Foreground = remaining < 0 ? Ui.Brush("ErrorBrush") : Ui.Brush("TextBrush");
            GateLabel.Text = remaining < 0 ? "Past the gate's required time" : "Counting down to the gate's required time";
            GateRequired.Text = $"Required time {TimeFormat.FormatClock(gate.RequiredMillis, zone)}";
            GateClockLine.Text = $"PC time {TimeFormat.FormatClock(now, zone)}  ({TimeFormat.ZoneLabel(zone)})";
        }

        private void SmallTimer_Click(object sender, RoutedEventArgs e)
        {
            if (Small is { } small) Vm.Controller.SetTimerFocus(small);
        }

        private void Start_Click(object sender, RoutedEventArgs e) => Vm.Controller.StartTimer(Now());

        private async void Reset_Click(object sender, RoutedEventArgs e)
        {
            if (await Dialogs.ConfirmAsync(XamlRoot, "Reset the timer?", "The running timer is cleared and you have to press Start again. Tracking is not affected.",
                    "Reset", "Keep timer", danger: true))
            {
                Vm.Controller.ResetTimer();
            }
        }

        private void Minus_Click(object sender, RoutedEventArgs e) => Vm.Controller.SetTimerBufferMinutes(Vm.State.TimerBufferMinutes - 1);

        private void Plus_Click(object sender, RoutedEventArgs e) => Vm.Controller.SetTimerBufferMinutes(Vm.State.TimerBufferMinutes + 1);

        private void ClockSettings_Click(object sender, RoutedEventArgs e) => Vm.Platform.OpenClockSettings();

        private void BufferHelp_Click(object sender, RoutedEventArgs e) => _ = Dialogs.InfoAsync(XamlRoot, "Timer countdown buffer",
            "Press Start when you begin your start procedure. The timer counts from a whole minute, rounded the same way the system rounds " +
            "your start time. Without a buffer that is the nearest whole minute, so you may have only seconds before the timer reaches zero.\n\n" +
            "A buffer moves that minute later to give you a lead-in. With 2 minutes, pressing Start at 12:00 makes 12:02 your start minute: " +
            "the timer starts at -2:00 and reaches 0:00 at 12:02. Plan to cross the starting line then. With an adaptive start your scored " +
            "start is still the minute you actually cross the line.\n\n" +
            "Because of the rounding, the first value can be a few seconds away from the full buffer. 0 means no buffer.");
    }
}
