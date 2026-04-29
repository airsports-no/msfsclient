using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Firebase.Auth;
using Firebase.Auth.Providers;
using msfsclient.Contracts.Services;

namespace msfsclient.Services
{
    public class AuthService : IAuthService
    {
        private readonly FirebaseAuthClient _client;
        private UserCredential? _userCredential;
        private const string ApiKey = "AIzaSyD_LDULS2RvLvhTtnLyEzARtbs-WgtFOAo";
        private const string AuthDomain = "airsports-613ce.firebaseapp.com";

        public bool IsAuthenticated => _userCredential?.User != null;
        public User? CurrentUser => _userCredential?.User;
        public string? JwtToken => _userCredential?.User?.Credential?.IdToken;
        public string? LastErrorMessage { get; private set; }

        public AuthService()
        {
            var config = new FirebaseAuthConfig
            {
                ApiKey = ApiKey,
                AuthDomain = AuthDomain,
                Providers = new FirebaseAuthProvider[]
                {
                    new EmailProvider()
                }
            };

            _client = new FirebaseAuthClient(config);
        }

        public async Task<bool> LoginAsync(string email, string password)
        {
            try
            {
                LastErrorMessage = null;
                var credential = await _client.SignInWithEmailAndPasswordAsync(email, password);
                _userCredential = credential;
                return IsAuthenticated;
            }
            catch (FirebaseAuthHttpException ex)
            {
                LastErrorMessage = $"HTTP Error: {ex.Reason} - {ex.Message}";
                return false;
            }
            catch (FirebaseAuthException ex)
            {
                LastErrorMessage = $"Auth Error: {ex.Reason}";
                return false;
            }
            catch (Exception ex)
            {
                LastErrorMessage = $"General Error: {ex.Message}";
                return false;
            }
        }

        public async Task<bool> RegisterAsync(string email, string password, string displayName)
        {
            try
            {
                LastErrorMessage = null;
                var credential = await _client.CreateUserWithEmailAndPasswordAsync(email, password, displayName);
                _userCredential = credential;
                return IsAuthenticated;
            }
            catch (FirebaseAuthHttpException ex)
            {
                LastErrorMessage = $"HTTP Error: {ex.Reason} - {ex.Message}";
                return false;
            }
            catch (FirebaseAuthException ex)
            {
                LastErrorMessage = $"Auth Error: {ex.Reason}";
                return false;
            }
            catch (Exception ex)
            {
                LastErrorMessage = $"General Error: {ex.Message}";
                return false;
            }
        }

        public async Task SendEmailVerificationAsync()
        {
            if (_userCredential?.User?.Credential?.IdToken == null) return;

            using var httpClient = new HttpClient();
            var url = $"https://identitytoolkit.googleapis.com/v1/accounts:sendOobCode?key={ApiKey}";
            var payload = new
            {
                requestType = "VERIFY_EMAIL",
                idToken = _userCredential.User.Credential.IdToken
            };

            var response = await httpClient.PostAsJsonAsync(url, payload);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to send verification email: {error}");
            }
        }

        public async Task ResetPasswordAsync(string email)
        {
            try
            {
                using var httpClient = new HttpClient();
                var url = $"https://identitytoolkit.googleapis.com/v1/accounts:sendOobCode?key={ApiKey}";
                var payload = new
                {
                    requestType = "PASSWORD_RESET",
                    email = email
                };

                var response = await httpClient.PostAsJsonAsync(url, payload);
                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    LastErrorMessage = $"Reset Password Error: {error}";
                    throw new Exception(error);
                }
            }
            catch (Exception ex)
            {
                LastErrorMessage = $"General Error: {ex.Message}";
                throw;
            }
        }

        public void Logout()
        {
            try
            {
                if (_client.User != null)
                {
                    _client.SignOut();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Firebase SignOut Error: {ex.Message}");
            }
            finally
            {
                _userCredential = null;
            }
        }
    }
}
