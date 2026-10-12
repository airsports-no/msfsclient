using System.Collections.Generic;
using System.Globalization;
using msfsclient.Core.Tracking;

namespace msfsclient.Views
{
    public sealed partial class DebugView : ShellPart
    {
        private long _startedTicks = System.Environment.TickCount64;
        private long _startCount = -1;

        public DebugView()
        {
            this.InitializeComponent();
        }

        public IReadOnlyList<DebugRow> Rows { get; private set; } = [];

        protected override void Refresh()
        {
            var t = Vm.Tracking;
            var sim = Vm.Sim;
            var now = Vm.NowMillis;
            if (_startCount < 0) _startCount = sim.Count;
            var seconds = (System.Environment.TickCount64 - _startedTicks) / 1000.0;
            var rate = seconds >= 5 ? ((sim.Count - _startCount) / seconds).ToString("0.0", CultureInfo.InvariantCulture) + " per second" : "measuring…";
            var last = sim.Last;
            Rows =
            [
                new("Simulator", sim.Connected ? "connected" : "not connected"),
                new("Simulator status", Vm.SimStatus.Text),
                new("Position rate", rate),
                new("Latest position", last == null ? "none" : $"{System.Math.Max(0, (now - last.TimestampMillis) / 1000)} s ago"),
                new("Position", last == null ? "-" : string.Create(CultureInfo.InvariantCulture, $"{last.Latitude:F5}, {last.Longitude:F5}")),
                new("Ground speed", last == null ? "-" : $"{System.Math.Round(last.SpeedMetersPerSecond * TrackPoint.MetersPerSecondToKnots)} kt"),
                new("Altitude", last?.Altitude is { } alt ? $"{System.Math.Round(alt / TrackPoint.FeetToMeters)} ft" : "-"),
                new("Tracking", t.Running ? "on" : "off"),
                new("Positions this session", t.FixCount.ToString(CultureInfo.InvariantCulture)),
                new("Queued positions", t.Backlog.ToString(CultureInfo.InvariantCulture)),
                new("Last upload", t.LastUploadMillis is { } up ? $"{System.Math.Max(0, (now - up) / 1000)} s ago" : "none"),
                new("Upload", t.LastError ?? "ok"),
                new("PC clock vs network time", Vm.State.NetworkClockOffsetMillis is { } o
                    ? string.Create(CultureInfo.InvariantCulture, $"{o / 1000.0:0.000} s (±{Vm.State.NetworkClockUncertaintyMillis / 1000.0:0.000} s)") : "unknown"),
                new("Simulator tracking id", Vm.State.Person?.SimulatorTrackingId ?? "-"),
                new("App version", Vm.Controller.AppVersionLabel),
            ];
        }
    }
}
