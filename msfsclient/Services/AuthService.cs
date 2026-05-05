using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Firebase.Auth;
using Firebase.Auth.Providers;
using msfsclient.Contracts.Services;
using Firebase.Auth.Repository;

namespace msfsclient.Services
{
    public class AuthService : IAuthService
    {
        private readonly FirebaseAuthClient _client;
        private readonly HttpClient _httpClient;
        private UserCredential? _userCredential;
        private const string ApiKey = "AIzaSyD_LDULS2RvLvhTtnLyEzARtbs-WgtFOAo";
        private const string AuthDomain = "airsports-613ce.firebaseapp.com";

        private string? _cachedToken;
        public bool IsAuthenticated => _client.User != null && _client.User.Info.IsEmailVerified;
        public User? CurrentUser => _client.User;
        public string? JwtToken => _cachedToken ?? _client.User?.Credential?.IdToken;
        public string? LastErrorMessage { get; private set; }

        public AuthService(HttpClient httpClient)
        {
            _httpClient = httpClient;
            string localFolder;
            try
            {
                // Try to get the packaged app local folder
                localFolder = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
            }
            catch (Exception)
            {
                // Fallback for unpackaged mode
                localFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "msfsclient");
                if (!Directory.Exists(localFolder))
                {
                    Directory.CreateDirectory(localFolder);
                }
            }

            var userFilePath = Path.Combine(localFolder, "user.json");
            System.Diagnostics.Debug.WriteLine($"AuthService: Using user file path: {userFilePath}");

            var config = new FirebaseAuthConfig
            {
                ApiKey = ApiKey,
                AuthDomain = AuthDomain,
                Providers = new FirebaseAuthProvider[]
                {
                    new EmailProvider()
                },
                UserRepository = new FileUserRepository(userFilePath)
            };

            _client = new FirebaseAuthClient(config);

            if (_client.User != null)
            {
                System.Diagnostics.Debug.WriteLine($"AuthService: Restored user: {_client.User.Info.Email}, Verified: {_client.User.Info.IsEmailVerified}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("AuthService: No user restored from repository.");
            }
        }

        public async Task<bool> LoginAsync(string email, string password)
        {
            try
            {
                LastErrorMessage = null;
                _cachedToken = null; // Clear cached token on new login
                _userCredential = await _client.SignInWithEmailAndPasswordAsync(email, password);
                
                if (_client.User != null)
                {
                    System.Diagnostics.Debug.WriteLine($"AuthService: Login successful for {_client.User.Info.Email}. Verified: {_client.User.Info.IsEmailVerified}");
                    
                    if (!_client.User.Info.IsEmailVerified)
                    {
                        LastErrorMessage = "Your email address has not been verified. Please check your inbox for a verification email.";
                        return false;
                    }
                }
                
                return IsAuthenticated;
            }
            catch (FirebaseAuthHttpException ex)
            {
                LastErrorMessage = ex.Reason switch
                {
                    AuthErrorReason.WrongPassword => "The password you entered is incorrect.",
                    AuthErrorReason.UserNotFound => "No account found with this email address.",
                    AuthErrorReason.UserDisabled => "This account has been disabled.",
                    AuthErrorReason.TooManyAttemptsTryLater => "Too many failed login attempts. Please try again later.",
                    AuthErrorReason.InvalidEmailAddress => "The email address is poorly formatted.",
                    _ => $"Login failed: {ex.Reason}"
                };
                System.Diagnostics.Debug.WriteLine($"AuthService: Login failed: {ex.Reason} - {ex.Message}");
                return false;
            }
            catch (FirebaseAuthException ex)
            {
                LastErrorMessage = $"Auth Error: {ex.Reason}";
                System.Diagnostics.Debug.WriteLine($"AuthService: Login failed: {LastErrorMessage}");
                return false;
            }
            catch (Exception ex)
            {
                LastErrorMessage = $"General Error: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"AuthService: Login failed: {LastErrorMessage}");
                return false;
            }
        }

        public async Task<bool> RegisterAsync(string email, string password, string firstName, string lastName)
        {
            try
            {
                LastErrorMessage = null;
                var displayName = $"{firstName} {lastName}".Trim();
                var credential = await _client.CreateUserWithEmailAndPasswordAsync(email, password, displayName);
                _userCredential = credential;
                System.Diagnostics.Debug.WriteLine($"AuthService: Registration successful for {email}");
                // Success if user was created, even if not yet verified
                return _client.User != null;
            }
            catch (FirebaseAuthHttpException ex)
            {
                LastErrorMessage = $"HTTP Error: {ex.Reason} - {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"AuthService: Registration failed: {LastErrorMessage}");
                return false;
            }
            catch (FirebaseAuthException ex)
            {
                LastErrorMessage = $"Auth Error: {ex.Reason}";
                System.Diagnostics.Debug.WriteLine($"AuthService: Registration failed: {LastErrorMessage}");
                return false;
            }
            catch (Exception ex)
            {
                LastErrorMessage = $"General Error: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"AuthService: Registration failed: {LastErrorMessage}");
                return false;
            }
        }

        public async Task SendEmailVerificationAsync()
        {
            if (_client.User == null) return;

            // In Firebase.Auth 4.x, we can use the client or user object if supported, 
            // but the direct REST API call is also fine if we have the token.
            var token = await _client.User.GetIdTokenAsync();
            if (token == null) return;

            var url = $"https://identitytoolkit.googleapis.com/v1/accounts:sendOobCode?key={ApiKey}";
            var payload = new
            {
                requestType = "VERIFY_EMAIL",
                idToken = token
            };

            var response = await _httpClient.PostAsJsonAsync(url, payload);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"AuthService: Failed to send verification email: {error}");
                throw new Exception($"Failed to send verification email: {error}");
            }
            System.Diagnostics.Debug.WriteLine("AuthService: Verification email sent.");
        }

        public async Task ResetPasswordAsync(string email)
        {
            try
            {
                var url = $"https://identitytoolkit.googleapis.com/v1/accounts:sendOobCode?key={ApiKey}";
                var payload = new
                {
                    requestType = "PASSWORD_RESET",
                    email = email
                };

                var response = await _httpClient.PostAsJsonAsync(url, payload);
                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    LastErrorMessage = $"Reset Password Error: {error}";
                    System.Diagnostics.Debug.WriteLine($"AuthService: Reset password failed: {error}");
                    throw new Exception(error);
                }
                System.Diagnostics.Debug.WriteLine($"AuthService: Password reset email sent to {email}");
            }
            catch (Exception ex)
            {
                LastErrorMessage = $"General Error: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"AuthService: Reset password exception: {ex.Message}");
                throw;
            }
        }

        public async Task ReloadUserAsync()
        {
            if (_client.User != null)
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine("AuthService: Reloading user state...");
                    
                    // Add a timeout to prevent infinite hang if network is problematic
                    var reloadTask = _client.User.GetIdTokenAsync(true);
                    if (await Task.WhenAny(reloadTask, Task.Delay(5000)) == reloadTask)
                    {
                        _cachedToken = await reloadTask;
                        System.Diagnostics.Debug.WriteLine($"AuthService: User state reloaded. Verified: {_client.User.Info.IsEmailVerified}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("AuthService: Reload user state timed out after 5s.");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"AuthService: Failed to reload user state: {ex.Message}");
                }
            }
        }

        public void Logout()
        {
            try
            {
                if (_client.User != null)
                {
                    System.Diagnostics.Debug.WriteLine($"AuthService: Logging out user {_client.User.Info.Email}");
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
                _cachedToken = null;
            }
        }
    }
}
