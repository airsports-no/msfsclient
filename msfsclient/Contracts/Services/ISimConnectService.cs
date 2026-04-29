using System;
using msfsclient.Models;

namespace msfsclient.Contracts.Services
{
    public interface ISimConnectService
    {
        bool IsConnected { get; }
        event EventHandler<FlightDataStruct>? FlightDataReceived;
        event EventHandler<bool>? ConnectionStatusChanged;

        void Connect(IntPtr windowHandle);
        void Disconnect();
    }
}
