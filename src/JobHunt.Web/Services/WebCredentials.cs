using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using JobHunt.MailBot.Services;
using Microsoft.AspNetCore.DataProtection;

namespace JobHunt.Web.Services;

public sealed class WebCredentials(WebStore store, IDataProtectionProvider protection)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _email;
    private string? _password;
    private string FileFor(string email) => Path.Combine(store.Root, "credentials",
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(email.ToLowerInvariant()))) + ".bin");
    private IDataProtector Protector(string email) => protection.CreateProtector("JobHunt.Web.AppPassword.v1", email.ToLowerInvariant());

    public async Task<string?> GetAsync(string email)
    {
        await _gate.WaitAsync();
        try
        {
            if (string.Equals(email, _email, StringComparison.OrdinalIgnoreCase)) return _password;
            var path = FileFor(email);
            return File.Exists(path) ? Protector(email).Unprotect(await File.ReadAllTextAsync(path)) : null;
        }
        finally { _gate.Release(); }
    }

    public async Task UpdateAsync(string email, string? password, bool remember)
    {
        if (!JobTextCodec.IsValidEmail(email)) throw new InvalidDataException("Informe um email válido.");
        var current = string.IsNullOrWhiteSpace(password) ? await GetAsync(email) : AppPassword.Normalize(password);
        if (current is null) throw new InvalidOperationException("Informe sua senha de app do Gmail.");
        await _gate.WaitAsync();
        try
        {
            var path = FileFor(email);
            if (remember)
            {
                WebStore.PrivateDirectory(Path.GetDirectoryName(path)!);
                var temp = path + ".tmp";
                await File.WriteAllTextAsync(temp, Protector(email).Protect(current));
                WebStore.PrivateFile(temp);
                File.Move(temp, path, true);
            }
            else if (File.Exists(path)) File.Delete(path);
            _email = email;
            _password = current;
        }
        finally { _gate.Release(); }
    }

    public async Task ForgetAsync(string email)
    {
        await _gate.WaitAsync();
        try
        {
            var path = FileFor(email);
            if (File.Exists(path)) File.Delete(path);
            if (string.Equals(email, _email, StringComparison.OrdinalIgnoreCase))
            {
                _email = null;
                _password = null;
            }
        }
        finally { _gate.Release(); }
    }

    public static X509Certificate2 LoadCertificate(string root)
    {
        var path = Path.Combine(root, "protection.pfx");
        if (File.Exists(path)) return X509CertificateLoader.LoadPkcs12FromFile(path, null,
            X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
        if (Directory.Exists(Path.Combine(root, "keys")) && Directory.EnumerateFiles(Path.Combine(root, "keys")).Any())
            throw new InvalidOperationException("O certificado de proteção está ausente. Restaure-o junto com as chaves.");
        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest("CN=JobHunt Web Local Protection", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(20));
        File.WriteAllBytes(path, generated.Export(X509ContentType.Pfx));
        WebStore.PrivateFile(path);
        return X509CertificateLoader.LoadPkcs12FromFile(path, null, X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
    }
}
