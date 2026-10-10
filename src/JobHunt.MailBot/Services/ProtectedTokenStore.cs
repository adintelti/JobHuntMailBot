using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Apis.Util.Store;

namespace JobHunt.MailBot.Services;

public sealed class ProtectedTokenStore(string directory) : IDataStore
{
    private string FileFor<T>(string key) => Path.Combine(directory,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(typeof(T).FullName + ":" + key))) + ".bin");

    public async Task StoreAsync<T>(string key, T value)
    {
        Directory.CreateDirectory(directory);
        var path = FileFor<T>(key);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        try
        {
            var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            await File.WriteAllBytesAsync(temp, encrypted);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public async Task<T> GetAsync<T>(string key)
    {
        var path = FileFor<T>(key);
        if (!File.Exists(path)) return default!;
        var bytes = ProtectedData.Unprotect(await File.ReadAllBytesAsync(path), null, DataProtectionScope.CurrentUser);
        try { return JsonSerializer.Deserialize<T>(bytes)!; }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public Task DeleteAsync<T>(string key)
    {
        var path = FileFor<T>(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        if (Directory.Exists(directory))
            foreach (var path in Directory.EnumerateFiles(directory, "*.bin")) File.Delete(path);
        return Task.CompletedTask;
    }
}
