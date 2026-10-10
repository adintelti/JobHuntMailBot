using System.Text;
using JobHunt.MailBot.Models;
using JobHunt.MailBot.Services;

namespace JobHunt.MailBot.Tests;

public sealed class AppPasswordTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MailBot-password-tests-" + Guid.NewGuid().ToString("N"));
    private const string Email = "pessoa@example.com";
    private const string Password = "abcd efgh ijkl mnop";

    [Fact]
    public void SpacesFromGoogleDisplayAreRemovedWithoutChangingCase()
        => Assert.Equal("AbCdEfGhIjKlMnOp", AppPasswordStore.Normalize(" AbCd EfGh\tIjKl MnOp\r\n"));

    [Theory]
    [InlineData("")]
    [InlineData("senha-pessoal")]
    [InlineData("abcdefghijklmnopq")]
    [InlineData("ábcdefghijklmnop")]
    public void InvalidAppPasswordsAreRejectedWithoutEchoingTheInput(string input)
    {
        var error = Assert.Throws<InvalidOperationException>(() => AppPasswordStore.Normalize(input));
        Assert.Contains("16 caracteres", error.Message);
        if (input.Length > 0) Assert.DoesNotContain(input, error.Message);
    }

    [Fact]
    public async Task RememberedPasswordIsEncryptedSeparateFromSettingsAndReloadsAfterRestart()
    {
        await new AppPasswordStore(_directory).UpdateAsync(Email, Password, remember: true);
        await new SettingsStore(_directory).SaveAsync(new AppSettings
        { SenderEmail = Email, AppPasswordEmail = Email, RememberAppPassword = true });
        var saved = await new AppPasswordStore(_directory).LoadAsync(Email);
        Assert.Equal("abcdefghijklmnop", saved);
        var json = await File.ReadAllTextAsync(Path.Combine(_directory, "settings.json"));
        Assert.DoesNotContain("abcdefghijklmnop", json);
        Assert.DoesNotContain(Password, json);
        var encrypted = await File.ReadAllBytesAsync(Assert.Single(Directory.GetFiles(Path.Combine(_directory, "app-passwords"))));
        Assert.DoesNotContain("abcdefghijklmnop", Encoding.UTF8.GetString(encrypted));
    }

    [Fact]
    public async Task SessionOnlyPasswordIsNotPersistedAndOptingOutDeletesAnExistingCopy()
    {
        var store = new AppPasswordStore(_directory);
        await store.UpdateAsync(Email, Password, remember: false);
        Assert.Null(await store.LoadAsync(Email));
        Assert.False(Directory.Exists(_directory));
        await store.UpdateAsync(Email, Password, remember: true);
        await store.UpdateAsync(Email, Password, remember: false);
        Assert.Null(await store.LoadAsync(Email));
    }

    [Fact]
    public async Task PasswordIsBoundToItsEmailAndEmailMatchingIgnoresCase()
    {
        var store = new AppPasswordStore(_directory);
        await store.UpdateAsync(" PESSOA@example.com ", Password, remember: true);
        Assert.Equal("abcdefghijklmnop", await store.LoadAsync(Email));
        Assert.Null(await store.LoadAsync("outra@example.com"));
        await store.ForgetAsync(Email);
        Assert.Null(await store.LoadAsync(Email));
    }

    [Fact]
    public async Task InvalidReplacementDoesNotOverwriteTheSavedPassword()
    {
        var store = new AppPasswordStore(_directory);
        await store.UpdateAsync(Email, Password, true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateAsync(Email, "errada", true));
        Assert.Equal("abcdefghijklmnop", await store.LoadAsync(Email));
    }

    [Fact]
    public void NewInstallationsDefaultToAppPasswordWithPersistenceDisabled()
    {
        var settings = new SettingsStore(_directory).Load();
        Assert.Equal(EmailAuthenticationMode.AppPassword, settings.AuthenticationMode);
        Assert.False(settings.RememberAppPassword);
    }

    [Fact]
    public async Task LegacyOAuthSettingsKeepTheirAuthenticationModeAndAccount()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "settings.json"),
            "{\"OAuthClientFile\":\"client.json\",\"SenderEmail\":\"pessoa@example.com\",\"MessageBody\":\"Candidatura\"}");
        var settings = new SettingsStore(_directory).Load();
        Assert.Equal(EmailAuthenticationMode.OAuth2, settings.AuthenticationMode);
        Assert.Equal(Email, settings.SenderEmail);
        Assert.Equal("Candidatura", settings.MessageBody);
    }

    [Fact]
    public async Task ExplicitSimpleModeIsPreservedEvenIfOAuthWasPreviouslyConfigured()
    {
        await new SettingsStore(_directory).SaveAsync(new AppSettings
        {
            AuthenticationMode = EmailAuthenticationMode.AppPassword, OAuthClientFile = "client.json",
            SenderEmail = Email, AppPasswordEmail = Email, OAuthSenderEmail = "outra@example.com"
        });
        var settings = new SettingsStore(_directory).Load();
        Assert.Equal(EmailAuthenticationMode.AppPassword, settings.AuthenticationMode);
        Assert.Equal(Email, settings.AppPasswordEmail);
        Assert.Equal("outra@example.com", settings.OAuthSenderEmail);
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
