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
                    var profile = await response.Content.ReadFromJsonAsync<UserProfile>();
                    
                    if (profile != null && !string.IsNullOrEmpty(profile.ProfilePictureUrl))
                    {
                        System.Diagnostics.Debug.WriteLine($"Raw ProfilePictureUrl: {profile.ProfilePictureUrl}");
                        
                        // Handle relative paths from the API
                        if (profile.ProfilePictureUrl.StartsWith("/"))
                        {
                            profile.ProfilePictureUrl = "https://airsports.no" + profile.ProfilePictureUrl;
                        }
                        else if (!profile.ProfilePictureUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        {
                            // If it's a relative path without a leading slash
                            profile.ProfilePictureUrl = "https://airsports.no/" + profile.ProfilePictureUrl;
                        }
                        
                        System.Diagnostics.Debug.WriteLine($"Final ProfilePictureUrl: {profile.ProfilePictureUrl}");
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

        public async Task<bool> UpdateProfileAsync(string firstName, string lastName, string? profilePictureUrl, string? biography)
        {
            try
            {
                SetAuthorizationHeader();
                var data = new 
                { 
                    first_name = firstName, 
                    last_name = lastName,
                    profile_picture = profilePictureUrl,
                    biography = biography
                };
                var response = await _httpClient.PutAsJsonAsync($"{BaseUrl}update_profile/", data);
                
                return response.IsSuccessStatusCode;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
