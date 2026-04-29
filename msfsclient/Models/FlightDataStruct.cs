using System;
using System.Runtime.InteropServices;

namespace msfsclient.Models
{
    public enum SimConnectRequests
    {
        FlightData = 1
    }

    public enum SimConnectDefinitions
    {
        FlightData = 1
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct FlightDataStruct
    {
        public double Latitude;
        public double Longitude;
        public double Altitude;
        public double GroundVelocity;
        public double HeadingRadians;

        public double HeadingDegrees => HeadingRadians * (180.0 / Math.PI);
    }
}
