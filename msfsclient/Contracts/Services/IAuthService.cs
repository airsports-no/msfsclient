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
        Task<bool> RegisterAsync(string email, string password, string firstName, string lastName);
        Task SendEmailVerificationAsync();
        Task ResetPasswordAsync(string email);
        Task ReloadUserAsync();
        void Logout();
    }
}
