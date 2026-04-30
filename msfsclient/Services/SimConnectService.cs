using System;
using Microsoft.FlightSimulator.SimConnect;
using msfsclient.Contracts.Services;
using msfsclient.Models;
using System.Runtime.InteropServices;

namespace msfsclient.Services
{
    public class SimConnectService : ISimConnectService
    {
        private SimConnect? _simConnect;
        private const int WM_USER_SIMCONNECT = 0x0402;
        private IntPtr _lastWindowHandle;

        public bool IsConnected { get; private set; }
        public event EventHandler<FlightDataStruct>? FlightDataReceived;
        public event EventHandler<bool>? ConnectionStatusChanged;
        public event EventHandler<string>? ErrorOccurred;

        public void Connect(IntPtr windowHandle)
        {
            if (IsConnected) return;
            if (windowHandle != IntPtr.Zero) _lastWindowHandle = windowHandle;
            if (_lastWindowHandle == IntPtr.Zero) return;

            try
            {
                _simConnect = new SimConnect("Air Sports Client", _lastWindowHandle, WM_USER_SIMCONNECT, null, 0);

                _simConnect.OnRecvOpen += (SimConnect sender, SIMCONNECT_RECV_OPEN data) =>
                {
                    IsConnected = true;
                    ConnectionStatusChanged?.Invoke(this, true);
                    RegisterDataDefinitions();
                };

                _simConnect.OnRecvQuit += (SimConnect sender, SIMCONNECT_RECV data) =>
                {
                    Disconnect();
                };

                _simConnect.OnRecvException += (SimConnect sender, SIMCONNECT_RECV_EXCEPTION data) =>
                {
                    ErrorOccurred?.Invoke(this, $"SimConnect Exception: {(SIMCONNECT_EXCEPTION)data.dwException}");
                };

                _simConnect.OnRecvSimobjectDataBytype += (SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA_BYTYPE data) =>
                {
                    if (data.dwRequestID == (uint)SimConnectRequests.FlightData)
                    {
                        var flightData = (FlightDataStruct)data.dwData[0];
                        FlightDataReceived?.Invoke(this, flightData);
                    }
                };

                _simConnect.OnRecvSimobjectData += (SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA data) =>
                {
                    if (data.dwRequestID == (uint)SimConnectRequests.FlightData)
                    {
                        var flightData = (FlightDataStruct)data.dwData[0];
                        FlightDataReceived?.Invoke(this, flightData);
                    }
                };

                // In WinUI 3, we don't have a standard WndProc. 
                // We'll need to handle the message loop or use a timer to poll ReceiveMessage if needed.
                // However, the managed wrapper usually handles the callback if the window handle is valid.
            }
            catch (Exception ex)
            {
                var errorMsg = $"SimConnect Connection Error: {ex.Message}";
                System.Diagnostics.Debug.WriteLine(errorMsg);
                ErrorOccurred?.Invoke(this, errorMsg);
            }
        }

        private void RegisterDataDefinitions()
        {
            if (_simConnect == null) return;

            try
            {
                _simConnect.AddToDataDefinition(SimConnectDefinitions.FlightData, "PLANE LATITUDE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                _simConnect.AddToDataDefinition(SimConnectDefinitions.FlightData, "PLANE LONGITUDE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                _simConnect.AddToDataDefinition(SimConnectDefinitions.FlightData, "PLANE ALTITUDE", "feet", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                _simConnect.AddToDataDefinition(SimConnectDefinitions.FlightData, "GROUND VELOCITY", "knots", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                _simConnect.AddToDataDefinition(SimConnectDefinitions.FlightData, "PLANE HEADING DEGREES TRUE", "radians", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);

                _simConnect.RegisterDataDefineStruct<FlightDataStruct>(SimConnectDefinitions.FlightData);

                // Request data every second (1000ms)
                _simConnect.RequestDataOnSimObject(SimConnectRequests.FlightData, SimConnectDefinitions.FlightData, SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_PERIOD.SECOND, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Data Definition Error: {ex.Message}");
            }
        }

        public void Disconnect()
        {
            if (_simConnect == null) return;

            try
            {
                var sim = _simConnect;
                _simConnect = null;
                sim?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during Disconnect: {ex.Message}");
            }
            finally
            {
                IsConnected = false;
                ConnectionStatusChanged?.Invoke(this, false);
            }
        }

        // This method needs to be called by the UI thread periodically or via a hook
        public void ReceiveMessage()
        {
            var sim = _simConnect;
            if (sim == null) return;

            try
            {
                sim.ReceiveMessage();
            }
            catch (Exception ex)
            {
                var errorMsg = $"SimConnect Receive Error: {ex.Message}";
                System.Diagnostics.Debug.WriteLine(errorMsg);
                // Don't invoke error event here to avoid spamming the UI on every poll
                // but do disconnect if the connection is dead
                Disconnect();
            }
        }
    }
}
