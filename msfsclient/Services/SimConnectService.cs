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

        public bool IsConnected { get; private set; }
        public event EventHandler<FlightDataStruct>? FlightDataReceived;
        public event EventHandler<bool>? ConnectionStatusChanged;

        public void Connect(IntPtr windowHandle)
        {
            if (IsConnected) return;

            try
            {
                _simConnect = new SimConnect("Air Sports Client", windowHandle, WM_USER_SIMCONNECT, null, 0);

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
            catch (COMException ex)
            {
                System.Diagnostics.Debug.WriteLine($"SimConnect Connection Error: {ex.Message}");
            }
        }

        private void RegisterDataDefinitions()
        {
            if (_simConnect == null) return;

            _simConnect.AddToDataDefinition(SimConnectDefinitions.FlightData, "PLANE LATITUDE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(SimConnectDefinitions.FlightData, "PLANE LONGITUDE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(SimConnectDefinitions.FlightData, "PLANE ALTITUDE", "feet", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(SimConnectDefinitions.FlightData, "GROUND VELOCITY", "knots", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(SimConnectDefinitions.FlightData, "PLANE HEADING DEGREES TRUE", "radians", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);

            _simConnect.RegisterDataDefineStruct<FlightDataStruct>(SimConnectDefinitions.FlightData);

            // Request data every second (1000ms)
            _simConnect.RequestDataOnSimObject(SimConnectRequests.FlightData, SimConnectDefinitions.FlightData, SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_PERIOD.SECOND, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
        }

        public void Disconnect()
        {
            if (!IsConnected) return;

            _simConnect?.Dispose();
            _simConnect = null;
            IsConnected = false;
            ConnectionStatusChanged?.Invoke(this, false);
        }

        // This method needs to be called by the UI thread periodically or via a hook
        public void ReceiveMessage()
        {
            try
            {
                _simConnect?.ReceiveMessage();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SimConnect ReceiveMessage Error: {ex.Message}");
                Disconnect();
            }
        }
    }
}
