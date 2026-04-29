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
                        // Ensure the URL is absolute
                        if (profile.ProfilePictureUrl.StartsWith("/"))
                        {
                            profile.ProfilePictureUrl = "https://airsports.no" + profile.ProfilePictureUrl;
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

        public async Task<bool> UpdateProfileAsync(string firstName, string lastName)
        {
            try
            {
                SetAuthorizationHeader();
                var data = new { first_name = firstName, last_name = lastName };
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
