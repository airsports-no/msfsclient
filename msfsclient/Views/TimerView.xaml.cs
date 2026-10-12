using System;
using System.Linq;
using Microsoft.UI.Xaml;
using msfsclient.Core.App;
using msfsclient.Core.Tracking;
using msfsclient.Core.Ui;
using msfsclient.Helpers;

namespace msfsclient.Views
{
    /// <summary>
    /// Adaptive-start timer. It shows the PC clock (what this client stamps positions with) to the second, so the pilot can see
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
                var contestId = s.ActiveContestants.FirstOrDefault(c => c.AdaptiveStart)?.ContestId;
                return TimeFormat.ZoneOrDevice(contestId is { } id && s.Contests.TryGetValue(id, out var contest) ? contest.TimeZone : null);
            }
        }

        protected override void Refresh()
        {
            var s = Vm.State;
            var running = s.TimerOriginMillis != null;
            ResetButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
            BeforeStart.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
            Running.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
            StartButton.Visibility = running ? Visibility.Collapsed : Visibility.Visible;

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
            "Press Start when you begin your start procedure. The timer counts from the nearest whole minute, the same minute the system uses as your start time. " +
            "Without a buffer the timer starts right at that minute, so you may have only seconds before it is zero.\n\n" +
            "A buffer gives you a lead-in. With 2 minutes, the timer starts at -2:00 and counts down to 0:00, which is still your real start minute. " +
            "Your start time is not changed, only when the timer begins.\n\n" +
            "Because the start is rounded to the nearest whole minute, the first value can be a few seconds away from the full buffer. 0 means no buffer.");
    }
}
