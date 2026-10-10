using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;

namespace JobHunt.MailBot.Services;

public sealed class GmailAuthorization(string dataDirectory) : IDisposable
{
    private static readonly string[] Scopes = ["https://mail.google.com/", "openid", "email"];
    private UserCredential? _credential;
    private string? _clientId;

    private static async Task<ClientSecrets> ReadSecretsAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) throw new InvalidOperationException("Selecione o JSON do cliente OAuth para aplicativo desktop.");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken));
        if (!document.RootElement.TryGetProperty("installed", out var installed)
            || !installed.TryGetProperty("client_id", out var id)
            || !installed.TryGetProperty("client_secret", out var secret)
            || string.IsNullOrWhiteSpace(id.GetString()) || string.IsNullOrWhiteSpace(secret.GetString()))
            throw new InvalidDataException("O JSON deve ser de um cliente OAuth do tipo Aplicativo para computador (installed).");
        return new() { ClientId = id.GetString(), ClientSecret = secret.GetString() };
    }

    private ProtectedTokenStore StoreFor(string clientId) => new(Path.Combine(dataDirectory, "tokens",
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clientId)))));

    public async Task<string> ConnectAsync(string clientFile, CancellationToken cancellationToken)
    {
        var secrets = await ReadSecretsAsync(clientFile, cancellationToken);
        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            new GoogleAuthorizationCodeFlow.Initializer { ClientSecrets = secrets }, Scopes, "account",
            usePkce: true, cancellationToken, StoreFor(secrets.ClientId));
        var token = await credential.GetAccessTokenForRequestAsync(cancellationToken: cancellationToken);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://openidconnect.googleapis.com/v1/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var profile = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!profile.RootElement.TryGetProperty("email", out var emailElement)
            || !JobTextCodec.IsValidEmail(emailElement.GetString() ?? ""))
            throw new InvalidOperationException("O Google não retornou um endereço de email válido.");
        DisposeCredential();
        _credential = credential;
        _clientId = secrets.ClientId;
        return emailElement.GetString()!;
    }

    public async Task<string> GetAccessTokenAsync(string clientFile, CancellationToken cancellationToken)
    {
        var secrets = await ReadSecretsAsync(clientFile, cancellationToken);
        if (_credential is null || _clientId != secrets.ClientId)
        {
            DisposeCredential();
            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = secrets, Scopes = Scopes, DataStore = StoreFor(secrets.ClientId)
            });
            var token = await flow.LoadTokenAsync("account", cancellationToken);
            if (token is null)
            {
                flow.Dispose();
                throw new InvalidOperationException("Conecte sua conta do Google na área Gmail e mensagem.");
            }
            _credential = new UserCredential(flow, "account", token);
            _clientId = secrets.ClientId;
        }
        // Never open a browser from the batch loop. Expired/revoked authorization is an item failure.
        return await _credential.GetAccessTokenForRequestAsync(cancellationToken: cancellationToken);
    }

    public async Task DisconnectAsync(string clientFile, CancellationToken cancellationToken)
    {
        var secrets = await ReadSecretsAsync(clientFile, cancellationToken);
        try
        {
            if (_credential is not null && _clientId != secrets.ClientId) DisposeCredential();
            if (_credential is null)
            {
                var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
                { ClientSecrets = secrets, Scopes = Scopes, DataStore = StoreFor(secrets.ClientId) });
                var token = await flow.LoadTokenAsync("account", cancellationToken);
                if (token is not null)
                { _credential = new UserCredential(flow, "account", token); _clientId = secrets.ClientId; }
                else flow.Dispose();
            }
            if (_credential is not null) await _credential.RevokeTokenAsync(cancellationToken);
        }
        finally
        {
            await StoreFor(secrets.ClientId).ClearAsync();
            DisposeCredential();
        }
    }

    private void DisposeCredential()
    {
        (_credential?.Flow as IDisposable)?.Dispose();
        _credential = null;
        _clientId = null;
    }

    public void Dispose() => DisposeCredential();
}
