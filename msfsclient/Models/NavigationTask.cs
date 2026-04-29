using System.Text.Json.Serialization;

namespace msfsclient.Models
{
    public class NavigationTask
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("start_time")]
        public string? StartTime { get; set; }

        [JsonPropertyName("finish_time")]
        public string? FinishTime { get; set; }

        [JsonPropertyName("tracking_link")]
        public string? TrackingLink { get; set; }
    }
}
