using System;
using System.Collections.Generic;
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
        private readonly IAppConfigService _appConfig;

        public ProfileService(IAuthService authService, HttpClient httpClient, IAppConfigService appConfig)
        {
            _authService = authService;
            _httpClient = httpClient;
            _appConfig = appConfig;
        }

        private void SetAuthorizationHeader(HttpRequestMessage request)
        {
            var token = _authService.JwtToken;
            if (!string.IsNullOrEmpty(token))
            {
                // The API specifically requires the 'JWT' scheme
                request.Headers.Authorization = new AuthenticationHeaderValue("JWT", token);
            }
        }

        public async Task<UserProfile?> GetProfileAsync()
        {
            try
            {
                var url = $"{_appConfig.ApiBaseUrl}retrieve_profile/";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                SetAuthorizationHeader(request);

                System.Diagnostics.Debug.WriteLine($"Fetching profile from: {url}");
                var tokenPreview = request.Headers.Authorization?.Parameter?.Length > 50 
                    ? request.Headers.Authorization.Parameter.Substring(0, 50) + "..." 
                    : request.Headers.Authorization?.Parameter;
                System.Diagnostics.Debug.WriteLine($"Token scheme: {request.Headers.Authorization?.Scheme}, Preview: {tokenPreview}");

                var response = await _httpClient.SendAsync(request);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var profile = System.Text.Json.JsonSerializer.Deserialize<UserProfile>(json);
                    
                    if (profile != null)
                    {
                        var webBase = _appConfig.WebBaseUrl.TrimEnd('/');

                        // Handle Profile Picture
                        if (!string.IsNullOrEmpty(profile.ProfilePictureUrl))
                        {
                            if (profile.ProfilePictureUrl.StartsWith("/"))
                                profile.ProfilePictureUrl = webBase + profile.ProfilePictureUrl;
                            else if (!profile.ProfilePictureUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                                profile.ProfilePictureUrl = webBase + "/" + profile.ProfilePictureUrl;
                        }

                        // Handle Country Flag
                        if (!string.IsNullOrEmpty(profile.CountryFlagUrl))
                        {
                            if (profile.CountryFlagUrl.StartsWith("/"))
                                profile.CountryFlagUrl = webBase + profile.CountryFlagUrl;
                            else if (!profile.CountryFlagUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                                profile.CountryFlagUrl = webBase + "/" + profile.CountryFlagUrl;
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

        public async Task<bool> UpdateProfileAsync(string? firstName = null, string? lastName = null, string? profilePictureUrl = null, string? biography = null, byte[]? profileImageData = null, string? imageFileName = null)
        {
            try
            {
                var url = $"{_appConfig.ApiBaseUrl}update_profile/";
                using var request = new HttpRequestMessage(HttpMethod.Patch, url);
                SetAuthorizationHeader(request);
                
                using var content = new MultipartFormDataContent();
                
                if (firstName != null)
                    content.Add(new StringContent(firstName), "first_name");
                
                if (lastName != null)
                    content.Add(new StringContent(lastName), "last_name");
                
                if (biography != null)
                    content.Add(new StringContent(biography), "biography");
                
                content.Add(new StringContent("true"), "validated");

                if (profileImageData != null && !string.IsNullOrEmpty(imageFileName))
                {
                    var imageContent = new ByteArrayContent(profileImageData);
                    string contentType = "image/png";
                    if (imageFileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || imageFileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                        contentType = "image/jpeg";
                    else if (imageFileName.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                        contentType = "image/gif";

                    imageContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                    content.Add(imageContent, "picture", imageFileName);
                }

                request.Content = content;
                var response = await _httpClient.SendAsync(request);
                
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

        public async System.Threading.Tasks.Task<System.Collections.Generic.List<msfsclient.Models.NavigationTaskResponse>> GetCurrentNavigationTasksAsync()
        {
            try
            {
                var url = $"{_appConfig.ApiBaseUrl}get_current_sim_navigation_task/";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                SetAuthorizationHeader(request);

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"Navigation Task JSON: {json}");
                    return System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<msfsclient.Models.NavigationTaskResponse>>(json) ?? new System.Collections.Generic.List<msfsclient.Models.NavigationTaskResponse>();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Navigation Task API Exception: {ex.Message}");
            }

            return new System.Collections.Generic.List<msfsclient.Models.NavigationTaskResponse>();
        }
    }
}
