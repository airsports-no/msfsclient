using System;
using System.IO;
using System.Text.Json;
using Firebase.Auth;
using Firebase.Auth.Repository;
using Windows.Security.Credentials;

namespace msfsclient.Services
{
    /// <summary>
    /// Keeps the signed-in Firebase user in the Windows credential store (PasswordVault) instead of a plain file. Only what
    /// is needed to sign in again is stored: the refresh token and the user's identity. The ID token is not stored (it is
    /// short-lived and fetched again with the refresh token), which also keeps the entry under the vault's size limit.
    /// </summary>
    public sealed class VaultUserRepository : IUserRepository
    {
        private const string Resource = "Airsports MSFS client";
        private const string UserName = "firebase-user";

        private sealed record Stored(string Uid, string? Email, bool Verified, string? DisplayName, string RefreshToken, FirebaseProviderType Provider);

        private Stored? _memory;

        public VaultUserRepository(string? legacyUserFile = null)
        {
            MigrateLegacyFile(legacyUserFile);
        }

        public bool UserExists() => Read() != null;

        public (UserInfo, FirebaseCredential) ReadUser()
        {
            var s = Read() ?? throw new InvalidOperationException("No stored user");
            var info = new UserInfo { Uid = s.Uid, Email = s.Email, IsEmailVerified = s.Verified, DisplayName = s.DisplayName };
            // An empty, expired ID token makes the client fetch a fresh one with the refresh token on first use. "Expired" must be
            // computable: Firebase checks Created.AddSeconds(ExpiresIn - 5), which throws for DateTime.MinValue.
            var credential = new FirebaseCredential { IdToken = "", RefreshToken = s.RefreshToken, Created = DateTime.Now.AddDays(-1), ExpiresIn = 3600, ProviderType = s.Provider };
            return (info, credential);
        }

        public void SaveUser(User user) => Write(new Stored(
            user.Info.Uid, user.Info.Email, user.Info.IsEmailVerified, user.Info.DisplayName, user.Credential.RefreshToken, user.Credential.ProviderType));

        public void DeleteUser()
        {
            _memory = null;
            try
            {
                var vault = new PasswordVault();
                foreach (var c in vault.FindAllByResource(Resource)) vault.Remove(c);
            }
            catch (Exception)
            {
                // Nothing stored (FindAllByResource throws when there is no entry).
            }
        }

        private Stored? Read()
        {
            if (_memory != null) return _memory;
            try
            {
                var c = new PasswordVault().Retrieve(Resource, UserName);
                c.RetrievePassword();
                _memory = JsonSerializer.Deserialize<Stored>(c.Password);
            }
            catch (Exception)
            {
                _memory = null;
            }
            return _memory;
        }

        private void Write(Stored s)
        {
            _memory = s;
            try
            {
                var vault = new PasswordVault();
                vault.Add(new PasswordCredential(Resource, UserName, JsonSerializer.Serialize(s)));
            }
            catch (Exception ex)
            {
                // The pilot stays signed in for this run; they sign in again at the next start.
                System.Diagnostics.Debug.WriteLine($"VaultUserRepository: could not store the user: {ex.Message}");
            }
        }

        /// <summary>
        /// Earlier versions kept the whole user, tokens included, in a plain file (Firebase's FileUserRepository, which writes
        /// <c>&lt;folder&gt;\firebase.json</c>; the folder was called user.json): move it into the vault and delete the file.
        /// </summary>
        private void MigrateLegacyFile(string? folder)
        {
            if (folder == null) return;
            var path = Path.Combine(folder, "firebase.json");
            if (!File.Exists(path)) return;
            try
            {
                var (info, credential) = new FileUserRepository(folder).ReadUser();
                if (info != null && credential?.RefreshToken != null)
                {
                    Write(new Stored(info.Uid, info.Email, info.IsEmailVerified, info.DisplayName, credential.RefreshToken, credential.ProviderType));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"VaultUserRepository: could not migrate {path}: {ex.Message}");
            }
            finally
            {
                try
                {
                    File.Delete(path);
                    if (Directory.GetFileSystemEntries(folder).Length == 0) Directory.Delete(folder);
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
