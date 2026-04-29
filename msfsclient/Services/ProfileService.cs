using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using msfsclient.Contracts.Services;
using msfsclient.Models;

namespace msfsclient.Services
{
    public class ProfileService : IProfileService
    {
        private readonly HttpClient _httpClient;
        private readonly IAuthService _authService;
        private const string BaseUrl = "https://airsports.no/api/v1/userprofile/";

        public ProfileService(IAuthService authService)
        {
            _authService = authService;
            _httpClient = new HttpClient();
        }

        private void SetAuthorizationHeader()
        {
            var token = _authService.JwtToken;
            if (!string.IsNullOrEmpty(token))
            {
                // The API specifically requires the 'JWT' scheme
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("JWT", token);
            }
        }

        public async Task<UserProfile?> GetProfileAsync()
        {
            try
            {
                SetAuthorizationHeader();
                System.Diagnostics.Debug.WriteLine($"Fetching profile with token: {_httpClient.DefaultRequestHeaders.Authorization?.Parameter?[..10]}...");
                var response = await _httpClient.GetAsync($"{BaseUrl}retrieve_profile/");
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"Raw Profile JSON: {json}");
                    
                    var profile = System.Text.Json.JsonSerializer.Deserialize<UserProfile>(json);
                    
                    if (profile != null)
                    {
                        // Handle Profile Picture
                        if (!string.IsNullOrEmpty(profile.ProfilePictureUrl))
                        {
                            if (profile.ProfilePictureUrl.StartsWith("/"))
                                profile.ProfilePictureUrl = "https://airsports.no" + profile.ProfilePictureUrl;
                            else if (!profile.ProfilePictureUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                                profile.ProfilePictureUrl = "https://airsports.no/" + profile.ProfilePictureUrl;
                        }

                        // Handle Country Flag
                        if (!string.IsNullOrEmpty(profile.CountryFlagUrl))
                        {
                            if (profile.CountryFlagUrl.StartsWith("/"))
                                profile.CountryFlagUrl = "https://airsports.no" + profile.CountryFlagUrl;
                            else if (!profile.CountryFlagUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                                profile.CountryFlagUrl = "https://airsports.no/" + profile.CountryFlagUrl;
                        }
                    }
                    
                    return profile;
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"Profile API Error: {response.StatusCode} - {error}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Profile API Exception: {ex.Message}");
            }

            return null;
        }

        public async Task<bool> UpdateProfileAsync(string firstName, string lastName, string? profilePictureUrl, string? biography, byte[]? profileImageData = null, string? imageFileName = null)
        {
            try
            {
                SetAuthorizationHeader();
                
                using var content = new MultipartFormDataContent();
                
                content.Add(new StringContent(firstName), "first_name");
                content.Add(new StringContent(lastName), "last_name");
                
                if (biography != null)
                    content.Add(new StringContent(biography), "biography");
                
                content.Add(new StringContent("true"), "validated");

                if (profileImageData != null && !string.IsNullOrEmpty(imageFileName))
                {
                    var imageContent = new ByteArrayContent(profileImageData);
                    // Determine content type based on extension
                    string contentType = "image/png";
                    if (imageFileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || imageFileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                        contentType = "image/jpeg";
                    else if (imageFileName.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                        contentType = "image/gif";

                    imageContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                    content.Add(imageContent, "picture", imageFileName);
                }
                else if (!string.IsNullOrEmpty(profilePictureUrl))
                {
                    // If we only have a URL, we don't send it back as a file
                    // Most DRF ImageFields will ignore a string URL during an update if it hasn't changed
                }

                var response = await _httpClient.PutAsync($"{BaseUrl}update_profile/", content);
                
                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"Profile Update Error: {response.StatusCode} - {error}");
                }

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Profile Update Exception: {ex.Message}");
                return false;
            }
        }

        public async Task<NavigationTask?> GetCurrentNavigationTaskAsync()
        {
            try
            {
                SetAuthorizationHeader();
                var response = await _httpClient.GetAsync($"{BaseUrl}get_current_sim_navigation_task/");

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<NavigationTask>();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Navigation Task API Exception: {ex.Message}");
            }

            return null;
        }
    }
}
