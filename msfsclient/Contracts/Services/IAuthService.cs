using System.Threading.Tasks;
using Firebase.Auth;

namespace msfsclient.Contracts.Services
{
    public interface IAuthService
    {
        bool IsAuthenticated { get; }
        User? CurrentUser { get; }
        string? JwtToken { get; }
        string? LastErrorMessage { get; }

        Task<bool> LoginAsync(string email, string password);
        Task<bool> RegisterAsync(string email, string password, string displayName);
        Task SendEmailVerificationAsync();
        Task ResetPasswordAsync(string email);
        void Logout();
    }
}
