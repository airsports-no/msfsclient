using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Firebase.Auth;
using Firebase.Auth.Providers;
using msfsclient.Core.Auth;

namespace msfsclient.Services
{
    /// <summary>
    /// Firebase sign-in (project airsports-613ce) behind the Core <see cref="IAuthService"/>: email and password, and Google /
    /// Apple through the website (Core DesktopSignIn, finished here with a custom token). The Firebase web API key is public
    /// by design. The signed-in user is kept in the Windows credential store.
    /// </summary>
    public sealed class AuthService : IAuthService, IAuthTokenProvider
    {
        private const string ApiKey = "AIzaSyD_LDULS2RvLvhTtnLyEzARtbs-WgtFOAo";
        private const string AuthDomain = "airsports-613ce.firebaseapp.com";
        private const string IdentityToolkit = "https://identitytoolkit.googleapis.com/v1/accounts:";

        private FirebaseAuthClient _client;
        private readonly HttpClient _http;
        private readonly VaultUserRepository _repository;

        public AuthService(HttpClient http, string? legacyUserFile)
        {
            _http = http;
            _repository = new VaultUserRepository(legacyUserFile);
            _client = NewClient();
        }

        /// <summary>A client that starts from the user in the credential store (signed in when there is one).</summary>
        private FirebaseAuthClient NewClient() => new(new FirebaseAuthConfig
        {
            ApiKey = ApiKey,
            AuthDomain = AuthDomain,
            Providers = [new EmailProvider()],
            UserRepository = _repository,
        });

        public IAuthTokenProvider Tokens => this;

        public bool IsSignedIn() => _client.User != null;

        public bool IsEmailVerified() => _client.User?.Info.IsEmailVerified == true;

        public string? Email() => _client.User?.Info.Email;

        public async Task<string?> IdTokenAsync(bool forceRefresh)
        {
            var user = _client.User;
            if (user == null) return null;
            try
            {
                return await user.GetIdTokenAsync(forceRefresh);
            }
            catch (FirebaseAuthHttpException ex) when (ex.Reason is AuthErrorReason.InvalidIDToken or AuthErrorReason.UserNotFound or AuthErrorReason.UserDisabled or AuthErrorReason.LoginCredentialsTooOld)
            {
                // The refresh token no longer works (password changed, account removed): the pilot has to sign in again.
                SignOut();
                return null;
            }
        }

        public async Task SignInWithPasswordAsync(string email, string password) =>
            await _client.SignInWithEmailAndPasswordAsync(email, password);

        /// <summary>
        /// Google / Apple sign-in, finished: the website's custom token (Core DesktopSignIn) is traded with Firebase for the
        /// user's tokens, and the user is kept like after a password sign-in. FirebaseAuthentication.net has no custom
        /// token sign-in, so it is done over REST and the client is started again from the stored user.
        /// </summary>
        public async Task SignInWithCustomTokenAsync(string customToken)
        {
            string idToken, refreshToken;
            using (var response = await PostAsync("signInWithCustomToken", new { token = customToken, returnSecureToken = true }))
            using (var tokens = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
            {
                idToken = tokens.RootElement.GetProperty("idToken").GetString() ?? throw new InvalidOperationException("The sign-in service did not answer.");
                refreshToken = tokens.RootElement.GetProperty("refreshToken").GetString() ?? throw new InvalidOperationException("The sign-in service did not answer.");
            }
            using var lookup = await PostAsync("lookup", new { idToken });
            using var users = JsonDocument.Parse(await lookup.Content.ReadAsStringAsync());
            var user = users.RootElement.GetProperty("users")[0];
            string? Text(JsonElement element, string name) =>
                element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            var provider = FirebaseProviderType.EmailAndPassword;
            if (user.TryGetProperty("providerUserInfo", out var providers) && providers.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in providers.EnumerateArray())
                {
                    provider = Text(p, "providerId") switch
                    {
                        "google.com" => FirebaseProviderType.Google,
                        "apple.com" => FirebaseProviderType.Apple,
                        _ => provider,
                    };
                }
            }
            var verified = user.TryGetProperty("emailVerified", out var v) && v.ValueKind == JsonValueKind.True;
            _repository.Store(
                Text(user, "localId") ?? throw new InvalidOperationException("The sign-in service did not answer."),
                Text(user, "email"), verified, Text(user, "displayName"), refreshToken, provider);
            _client = NewClient();
        }

        public async Task CreateAccountAsync(string email, string password)
        {
            await _client.CreateUserWithEmailAndPasswordAsync(email, password, null);
            await ResendVerificationAsync();
        }

        public Task SendPasswordResetAsync(string email) =>
            PostAsync("sendOobCode", new { requestType = "PASSWORD_RESET", email });

        public async Task ResendVerificationAsync()
        {
            var token = await IdTokenAsync(forceRefresh: false) ?? throw new InvalidOperationException("Not signed in");
            await PostAsync("sendOobCode", new { requestType = "VERIFY_EMAIL", idToken = token });
        }

        public async Task<bool> CheckVerifiedAsync()
        {
            var user = _client.User;
            if (user == null) return false;
            // A fresh token carries the new email_verified claim; the account lookup says whether it is verified now.
            // Through IdTokenAsync: a refresh token that no longer works signs the pilot out instead of failing every press.
            var token = await IdTokenAsync(forceRefresh: true);
            if (token == null) return false;
            using var response = await PostAsync("lookup", new { idToken = token });
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var verified = doc.RootElement.TryGetProperty("users", out var users) && users.GetArrayLength() > 0
                && users[0].TryGetProperty("emailVerified", out var v) && v.GetBoolean();
            if (verified && !user.Info.IsEmailVerified)
            {
                user.Info.IsEmailVerified = true;
                _repository.SaveUser(user);
            }
            return verified;
        }

        public void SignOut()
        {
            try { _client.SignOut(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Firebase sign-out: {ex.Message}"); }
            _repository.DeleteUser();
        }

        public string DescribeError(Exception e) => e switch
        {
            FirebaseAuthHttpException http => http.Reason switch
            {
                AuthErrorReason.WrongPassword or AuthErrorReason.UnknownEmailAddress or AuthErrorReason.UserNotFound => "The email or password is not right.",
                AuthErrorReason.InvalidEmailAddress => "That is not a valid email address.",
                AuthErrorReason.EmailExists => "An account with this email already exists. Sign in instead.",
                AuthErrorReason.WeakPassword => "Choose a longer password (at least 6 characters).",
                AuthErrorReason.UserDisabled => "This account has been disabled.",
                AuthErrorReason.TooManyAttemptsTryLater => "Too many attempts. Wait a little and try again.",
                _ => $"Sign-in failed ({http.Reason}).",
            },
            FirebaseAuthException auth => $"Sign-in failed ({auth.Reason}).",
            HttpRequestException => "No connection to the sign-in service. Check the internet connection.",
            _ => e.Message,
        };

        private async Task<HttpResponseMessage> PostAsync(string action, object body)
        {
            var response = await _http.PostAsJsonAsync($"{IdentityToolkit}{action}?key={ApiKey}", body);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                response.Dispose();
                throw new InvalidOperationException(FirebaseMessage(error));
            }
            return response;
        }

        /// <summary>The REST error code in words ("EMAIL_NOT_FOUND" and friends).</summary>
        private static string FirebaseMessage(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var code = doc.RootElement.GetProperty("error").GetProperty("message").GetString() ?? "";
                return code switch
                {
                    "EMAIL_NOT_FOUND" => "No account uses this email address.",
                    "TOO_MANY_ATTEMPTS_TRY_LATER" => "Too many attempts. Wait a little and try again.",
                    "INVALID_ID_TOKEN" or "USER_NOT_FOUND" => "Please sign in again.",
                    _ => code,
                };
            }
            catch (Exception)
            {
                return "The sign-in service did not answer.";
            }
        }
    }
}
