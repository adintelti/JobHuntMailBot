namespace JobHunt.MailBot.Services;

public sealed class AppPasswordStore(string dataDirectory)
{
    private readonly ProtectedTokenStore _store = new(Path.Combine(dataDirectory, "app-passwords"));

    public static string Normalize(string password)
    {
        return AppPassword.Normalize(password);
    }

    private static string Key(string email)
    {
        email = email.Trim();
        if (!JobTextCodec.IsValidEmail(email))
            throw new InvalidOperationException("Informe seu endereço completo do Gmail.");
        return email.ToLowerInvariant();
    }

    public async Task UpdateAsync(string email, string password, bool remember)
    {
        var key = Key(email);
        if (remember) await _store.StoreAsync(key, Normalize(password));
        else await _store.DeleteAsync<string>(key);
    }

    public Task<string?> LoadAsync(string email) => _store.GetAsync<string?>(Key(email));
    public Task ForgetAsync(string email) => _store.DeleteAsync<string>(Key(email));
}
