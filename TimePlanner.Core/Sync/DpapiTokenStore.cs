using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;

namespace TimePlanner.Core.Sync
{
    //-----------------------------
    //token file encrypted for the current windows user
    [SupportedOSPlatform("windows")]
    public sealed class DpapiTokenStore : ITokenStore
    {
        private readonly string _path;

        public DpapiTokenStore(string path) => _path = path;

        //-----------------------------
        //reads and decrypts the saved token
        public async Task<StoredToken?> LoadAsync()
        {
            //no file means never signed in
            if (!File.Exists(_path))
            {
                return null;
            }

            try
            {
                var encrypted = await File.ReadAllBytesAsync(_path);
                var json = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                return JsonSerializer.Deserialize<StoredToken>(json);
            }
            //another windows user or a damaged file
            catch (CryptographicException)
            {
                return null;
            }
            //a damaged file counts as signed out
            catch (JsonException)
            {
                return null;
            }
        }

        //-----------------------------
        //encrypts and writes the token
        public async Task SaveAsync(StoredToken token)
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(token);
            var encrypted = ProtectedData.Protect(json, null, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await File.WriteAllBytesAsync(_path, encrypted);
        }

        //-----------------------------
        //deletes the saved token
        public Task ClearAsync()
        {
            //nothing to delete when signed out
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }

            return Task.CompletedTask;
        }
    }
}
//------------------------------EOF-----------------------------\\
