namespace msfsclient.Core.Auth;

/// <summary>Firebase ID tokens for the API. Null means "not signed in".</summary>
public interface IAuthTokenProvider
{
    Task<string?> IdTokenAsync(bool forceRefresh);
}

/// <summary>
/// Sign-in (Firebase Auth). Every method either succeeds or throws; use <see cref="DescribeError"/> for user-facing text.
/// The backend links users by verified email, so <see cref="IsEmailVerified"/> gates access to the API.
/// </summary>
public interface IAuthService
{
    IAuthTokenProvider Tokens { get; }

    bool IsSignedIn();
    bool IsEmailVerified();
    string? Email();

    Task SignInWithPasswordAsync(string email, string password);
    /// <summary>Creates the account and sends the verification email.</summary>
    Task CreateAccountAsync(string email, string password);
    Task SendPasswordResetAsync(string email);
    Task ResendVerificationAsync();
    /// <summary>Reloads the user; returns true once the email is verified (and refreshes the token claims).</summary>
    Task<bool> CheckVerifiedAsync();
    void SignOut();

    string DescribeError(Exception e);
}
