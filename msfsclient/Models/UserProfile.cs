using System.Text.Json.Serialization;

namespace msfsclient.Models
{
    public class UserProfile
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("first_name")]
        public string FirstName { get; set; } = string.Empty;

        [JsonPropertyName("last_name")]
        public string LastName { get; set; } = string.Empty;

        [JsonPropertyName("full_name")]
        public string FullName => $"{FirstName} {LastName}".Trim();

        [JsonPropertyName("profile_picture")]
        public string? ProfilePictureUrl { get; set; }

        [JsonPropertyName("simulator_tracking_id")]
        public string? SimulatorTrackingId { get; set; }
    }
}
