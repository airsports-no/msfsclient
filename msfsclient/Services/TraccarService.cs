using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using msfsclient.Contracts.Services;
using msfsclient.Models;

namespace msfsclient.Services
{
    public class TraccarService : ITraccarService
    {
        private readonly HttpClient _httpClient;
        private const string BaseUrl = "https://traccar.airsports.no/";

        public TraccarService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<(bool Success, string Message)> SendDataAsync(string trackingId, FlightDataStruct data)
        {
            try
            {
                var query = HttpUtility.ParseQueryString(string.Empty);
                query["id"] = trackingId;
                query["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
                query["lat"] = data.Latitude.ToString("F6");
                query["lon"] = data.Longitude.ToString("F6");
                query["speed"] = data.GroundVelocity.ToString("F2");
                query["altitude"] = data.Altitude.ToString("F0");
                query["heading"] = data.HeadingDegrees.ToString("F2");

                var url = $"{BaseUrl}?{query}";
                
                // The python example uses POST but parameters are in the query string
                var response = await _httpClient.PostAsync(url, null);
                
                if (response.IsSuccessStatusCode)
                {
                    return (true, "OK");
                }
                
                var content = await response.Content.ReadAsStringAsync();
                var errorMsg = $"HTTP {response.StatusCode}: {content}";
                System.Diagnostics.Debug.WriteLine($"Traccar Error: {errorMsg}");
                return (false, errorMsg);
            }
            catch (Exception ex)
            {
                var errorMsg = ex.Message;
                System.Diagnostics.Debug.WriteLine($"Traccar Exception: {errorMsg}");
                return (false, errorMsg);
            }
        }
    }
}
