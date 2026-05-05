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
        public string FullName
        {
            get
            {
                var name = $"{FirstName} {LastName}".Trim();
                if (string.IsNullOrEmpty(name))
                {
                    return !string.IsNullOrEmpty(Email) ? Email : "User";
                }
                return name;
            }
        }

        [JsonPropertyName("picture")]
        public string? ProfilePictureUrl { get; set; }

        [JsonPropertyName("country_flag_url")]
        public string? CountryFlagUrl { get; set; }

        [JsonPropertyName("country")]
        public string? CountryCode { get; set; }

        [JsonPropertyName("simulator_tracking_id")]
        public string? SimulatorTrackingId { get; set; }

        [JsonPropertyName("biography")]
        public string? Biography { get; set; }

        [JsonPropertyName("validated")]
        public bool Validated { get; set; }
    }
}
