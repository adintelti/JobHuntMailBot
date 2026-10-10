using System.Text;
using System.Text.Json;
using JobHunt.MailBot.Models;
using JobHunt.MailBot.Services;
using Google.Apis.Auth.OAuth2.Responses;

namespace JobHunt.MailBot.Tests;

public sealed class PersistenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MailBot-storage-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SettingsSurviveReopeningAndDoNotLeaveTempFiles()
    {
        var settings = new AppSettings { OutputFilePath = @"C:\Vagas\vagas.txt", SenderName = "João", MessageBody = "Olá, candidatura", DelaySeconds = 7 };
        await new SettingsStore(_directory).SaveAsync(settings);
        var restored = new SettingsStore(_directory).Load();
        Assert.Equal(settings.OutputFilePath, restored.OutputFilePath);
        Assert.Equal(settings.MessageBody, restored.MessageBody); Assert.Equal(7, restored.DelaySeconds);
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task TokensAreEncryptedOnDiskAndCanBeLoadedByTheSameWindowsUser()
    {
        var store = new ProtectedTokenStore(_directory);
        await store.StoreAsync("account", "refresh-token-sensitive-example");
        var bytes = await File.ReadAllBytesAsync(Assert.Single(Directory.GetFiles(_directory)));
        Assert.DoesNotContain("refresh-token-sensitive-example", Encoding.UTF8.GetString(bytes));
        Assert.Equal("refresh-token-sensitive-example", await new ProtectedTokenStore(_directory).GetAsync<string>("account"));
        await store.DeleteAsync<string>("account");
        Assert.Null(await store.GetAsync<string>("account"));
    }

    [Fact]
    public async Task ActualGoogleTokenFieldsSurviveEncryptedSerialization()
    {
        var store = new ProtectedTokenStore(_directory);
        var token = new TokenResponse { AccessToken = "access-example", RefreshToken = "refresh-example",
            TokenType = "Bearer", ExpiresInSeconds = 3600, IssuedUtc = DateTime.UtcNow,
            Scope = "https://mail.google.com/ openid email" };
        await store.StoreAsync("account", token);
        var loaded = await store.GetAsync<TokenResponse>("account");
        Assert.Equal(token.RefreshToken, loaded.RefreshToken);
        Assert.Equal(token.AccessToken, loaded.AccessToken);
        Assert.Equal(token.IssuedUtc, loaded.IssuedUtc);
        Assert.Equal(token.ExpiresInSeconds, loaded.ExpiresInSeconds);
        Assert.Equal(token.Scope, loaded.Scope);
    }

    [Fact]
    public async Task EveryLogRecordIsFlushedAndContainsItsBatchAndJob()
    {
        var id = Guid.NewGuid();
        await using var log = new BatchLog(_directory, id);
        var result = new SendResult(id, "vagas.txt", 2, new("rh@example.com", "Líder técnico"),
            DateTimeOffset.Now, 1, SendOutcome.Failure, "SMTP 550");
        await log.WriteAsync(result);
        using var reader = new StreamReader(new FileStream(log.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        var line = await reader.ReadLineAsync();
        var restored = JsonSerializer.Deserialize<SendResult>(line!, BatchLog.JsonOptions);
        Assert.Equal(result, restored);
    }

    [Fact]
    public async Task HistoryProtectsSuccessAndIndeterminateRecordsEvenWithAnIncompleteLastLine()
    {
        var success = new JobOffer("sucesso@example.com", "Vaga 1");
        var unknown = new JobOffer("incerto@example.com", "Vaga 2");
        var failure = new JobOffer("falha@example.com", "Vaga 3");
        var id = Guid.NewGuid();
        string path;
        await using (var log = new BatchLog(_directory, id))
        {
            path = log.FilePath;
            await log.WriteAsync(new(id, "vagas.txt", 1, success, DateTimeOffset.Now, 1, SendOutcome.Success, "Aceito"));
            await log.WriteAsync(new(id, "vagas.txt", 2, unknown, DateTimeOffset.Now, 1, SendOutcome.Indeterminate, "Sem confirmação"));
            await log.WriteAsync(new(id, "vagas.txt", 3, failure, DateTimeOffset.Now, 1, SendOutcome.Failure, "Rejeitado"));
        }
        await File.AppendAllTextAsync(path, "{incompleto");
        var protectedKeys = await BatchLog.LoadProtectedKeysAsync(_directory);
        Assert.Equal(SendOutcome.Success, protectedKeys[success.Key]);
        Assert.Equal(SendOutcome.Indeterminate, protectedKeys[unknown.Key]);
        Assert.False(protectedKeys.ContainsKey(failure.Key));
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
