using System.Collections.Generic;
using System.Threading.Tasks;
using msfsclient.Models;

namespace msfsclient.Contracts.Services
{
    public interface IProfileService
    {
        System.Threading.Tasks.Task<UserProfile?> GetProfileAsync();
        System.Threading.Tasks.Task<bool> UpdateProfileAsync(string? firstName = null, string? lastName = null, string? profilePictureUrl = null, string? biography = null, byte[]? profileImageData = null, string? imageFileName = null);
        System.Threading.Tasks.Task<System.Collections.Generic.List<msfsclient.Models.NavigationTaskResponse>> GetCurrentNavigationTasksAsync();
    }
}
