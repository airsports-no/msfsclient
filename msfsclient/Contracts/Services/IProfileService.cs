using System.Threading.Tasks;
using msfsclient.Models;

namespace msfsclient.Contracts.Services
{
    public interface IProfileService
    {
        Task<UserProfile?> GetProfileAsync();
        Task<bool> UpdateProfileAsync(string firstName, string lastName, string? profilePictureUrl, string? biography);
        Task<NavigationTask?> GetCurrentNavigationTaskAsync();
    }
}
