using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace msfsclient.Models
{
    public class NavigationTaskResponse
    {
        [JsonPropertyName("navigation_task")]
        public NavigationTaskDetail? NavigationTask { get; set; }

        [JsonPropertyName("active_contestants")]
        public List<ContestantDetail> ActiveContestants { get; set; } = new();
    }

    public class NavigationTaskDetail
    {
        [JsonPropertyName("pk")]
        public int Pk { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("start_time")]
        public string? StartTime { get; set; }

        [JsonPropertyName("finish_time")]
        public string? FinishTime { get; set; }

        [JsonPropertyName("tracking_link")]
        public string? TrackingLink { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }

    public class ContestantDetail
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("gate_times")]
        public Dictionary<string, string> GateTimes { get; set; } = new();

        [JsonPropertyName("adaptive_start")]
        public bool AdaptiveStart { get; set; }

        [JsonPropertyName("takeoff_time")]
        public string? TakeoffTime { get; set; }

        [JsonPropertyName("tracker_start_time")]
        public string? TrackerStartTime { get; set; }

        [JsonPropertyName("finished_by_time")]
        public string? FinishedByTime { get; set; }

        [JsonPropertyName("tracker_id_display")]
        public List<TrackerIdDisplay>? TrackerIdDisplay { get; set; }

        [JsonPropertyName("has_crossed_starting_line")]
        public bool HasCrossedStartingLine { get; set; }

        [JsonIgnore]
        public string IntervalDisplay
        {
            get
            {
                if (string.IsNullOrEmpty(TrackerStartTime) || string.IsNullOrEmpty(FinishedByTime))
                    return "N/A";

                if (DateTimeOffset.TryParse(TrackerStartTime, out var start) && DateTimeOffset.TryParse(FinishedByTime, out var finish))
                {
                    return $"{start.ToLocalTime():HH:mm:ss} - {finish.ToLocalTime():HH:mm:ss}";
                }

                return $"{TrackerStartTime} - {FinishedByTime}";
            }
        }

        [JsonIgnore]
        public string StartMethodDisplay
        {
            get
            {
                if (AdaptiveStart)
                    return "Adaptive Start Enabled";

                if (string.IsNullOrEmpty(TakeoffTime))
                    return "Standard Start";

                if (DateTimeOffset.TryParse(TakeoffTime, out var takeoff))
                {
                    return $"Takeoff: {takeoff.ToLocalTime():HH:mm:ss}";
                }

                return $"Takeoff: {TakeoffTime}";
            }
        }

        [JsonIgnore]
        public string TimeZoneDisplay
        {
            get
            {
                var offset = DateTimeOffset.Now.ToString("zzz");
                return $"UTC{offset}";
            }
        }
    }

    public class TrackerIdDisplay
    {
        [JsonPropertyName("tracker")]
        public string Tracker { get; set; } = string.Empty;

        [JsonPropertyName("has_user")]
        public bool HasUser { get; set; }

        [JsonPropertyName("is_active")]
        public bool IsActive { get; set; }
    }
}
