using System;
using msfsclient.Contracts.Services;
using msfsclient.Core.App;
using msfsclient.Core.Tracking;
using msfsclient.Models;

namespace msfsclient.Services
{
    /// <summary>
    /// Turns SimConnect's once-a-second aircraft data into positions for tracking. The simulator has no clock of its own
    /// that matches real time, so each position is stamped with the PC clock (UTC) when it arrives; the pre-flight check
    /// compares that clock with network time.
    /// </summary>
    public sealed class SimPositionSource : IPositionSource, ICurrentLocation
    {
        private readonly ISimConnectService _sim;
        private readonly Func<long> _now;
        private TrackPoint? _last;

        public SimPositionSource(ISimConnectService sim, Func<long>? now = null)
        {
            _sim = sim;
            _now = now ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            _sim.FlightDataReceived += (_, data) => OnData(data);
            _sim.ConnectionStatusChanged += (_, connected) => { if (!connected) _last = null; };
        }

        public event Action<TrackPoint>? PositionReceived;

        public bool Connected => _sim.IsConnected;

        /// <summary>The latest position from the simulator (whether or not tracking is on); null before the first one.</summary>
        public TrackPoint? Last => _last;

        /// <summary>Positions received since the app started, for the debug view's rate.</summary>
        public long Count { get; private set; }

        public Coordinates? Current() => _last is { } p ? new Coordinates(p.Latitude, p.Longitude) : null;

        private void OnData(FlightDataStruct data)
        {
            // Lat/lon 0,0 is what SimConnect reports in the menus before a flight is loaded.
            if (double.IsNaN(data.Latitude) || double.IsNaN(data.Longitude) || (data.Latitude == 0 && data.Longitude == 0)) return;
            var heading = data.HeadingDegrees;
            var point = new TrackPoint(
                TimestampMillis: _now(),
                Latitude: data.Latitude,
                Longitude: data.Longitude,
                SpeedMetersPerSecond: data.GroundVelocity * TrackPoint.KnotsToMetersPerSecond,
                Bearing: heading < 0 ? heading + 360 : heading,
                // Traccar stores metres (the server converts to feet), as the phone apps send them.
                Altitude: data.Altitude * TrackPoint.FeetToMeters);
            _last = point;
            Count++;
            PositionReceived?.Invoke(point);
        }
    }
}
