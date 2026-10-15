using System;
using msfsclient.Models;

namespace msfsclient.Contracts.Services
{
    public interface ISimConnectService
    {
        bool IsConnected { get; }
        event EventHandler<FlightDataStruct>? FlightDataReceived;
        event EventHandler<bool>? ConnectionStatusChanged;
        event EventHandler<string>? ErrorOccurred;

        void Connect(IntPtr windowHandle);
        void Disconnect();
        void ReceiveMessage();

        /// <summary>Prints a line of text on the simulator's screen (SimConnect text); does nothing while not connected.</summary>
        void ShowText(string text, msfsclient.Core.Hud.SimMessageKind kind, float seconds);

        /// <summary>
        /// Places the user aircraft at a position (SimConnect initial position): on the ground (<paramref name="onGround"/>, the
        /// simulator puts it on the surface) or in the air at <paramref name="altitudeFt"/> and <paramref name="airspeedKnots"/>.
        /// Returns false when not connected or the simulator refused.
        /// </summary>
        bool MoveAircraft(double latitude, double longitude, double altitudeFt, double headingDegrees, bool onGround, uint airspeedKnots);
    }
}
