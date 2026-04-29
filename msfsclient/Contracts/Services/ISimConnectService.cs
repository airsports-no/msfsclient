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
    }
}
