using System.Threading.Tasks;
using msfsclient.Models;

namespace msfsclient.Contracts.Services
{
    public interface IProfileService
    {
        Task<UserProfile?> GetProfileAsync();
        Task<bool> UpdateProfileAsync(string? firstName = null, string? lastName = null, string? profilePictureUrl = null, string? biography = null, byte[]? profileImageData = null, string? imageFileName = null);
        Task<NavigationTask?> GetCurrentNavigationTaskAsync();
    }
}
