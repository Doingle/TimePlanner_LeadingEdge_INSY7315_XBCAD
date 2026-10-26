using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TimePlanner.Widget.Services.Ai
{
    //-----------------------------
    //secure storage for external ai api keys using dpapi
    public sealed class AiKeyStore
    {
        private readonly string _path;
        private readonly object _lock = new();

        public AiKeyStore(string? folder = null)
        {
            var baseFolder = folder ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TimePlanner");
            _path = Path.Combine(baseFolder, "ai-keys.bin");
        }

        //-----------------------------
        //retrieves decrypted api key for given provider kind
        public string? Get(string kind)
        {
            lock (_lock)
            {
                var dict = Load();
                return dict.TryGetValue(kind, out var key) ? key : null;
            }
        }

        //-----------------------------
        //encrypts and saves api key for given provider kind
        public void Set(string kind, string? key)
        {
            lock (_lock)
            {
                var dict = Load();

                //remove entry when null or blank
                if (string.IsNullOrWhiteSpace(key))
                {
                    dict.Remove(kind);
                }
                else
                {
                    dict[kind] = key.Trim();
                }

                Save(dict);
            }
        }

        //-----------------------------
        //reads and decrypts key dictionary from disk
        private Dictionary<string, string> Load()
        {
            //missing key file returns empty map
            if (!File.Exists(_path))
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                var encrypted = File.ReadAllBytes(_path);
                var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                var json = Encoding.UTF8.GetString(decrypted);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            //corrupted or unreadable keys fall back to empty map
            catch (Exception)
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        //-----------------------------
        //encrypts and writes key dictionary to disk
        private void Save(Dictionary<string, string> dict)
        {
            var dir = Path.GetDirectoryName(_path);
            //ensure local appdata directory exists
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(dict);
            var plainBytes = Encoding.UTF8.GetBytes(json);
            var encrypted = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);

            File.WriteAllBytes(_path, encrypted);
        }
    }
}
//------------------------------EOF-----------------------------\\
