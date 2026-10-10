using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace JobHunt.MailBot.Services;

public interface IGmailSmtpSession : IDisposable
{
    Task ConnectAsync(CancellationToken cancellationToken);
    Task AuthenticatePasswordAsync(string email, string password, CancellationToken cancellationToken);
    Task AuthenticateOAuthAsync(string email, string accessToken, CancellationToken cancellationToken);
    Task<string> SendAsync(MimeMessage message, CancellationToken cancellationToken);
}

public sealed class GmailSmtpSession(int timeoutSeconds) : IGmailSmtpSession
{
    private readonly SmtpClient _smtp = new() { Timeout = timeoutSeconds * 1000 };

    public Task ConnectAsync(CancellationToken cancellationToken)
        => _smtp.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls, cancellationToken);

    public Task AuthenticatePasswordAsync(string email, string password, CancellationToken cancellationToken)
        => _smtp.AuthenticateAsync(email, password, cancellationToken);

    public Task AuthenticateOAuthAsync(string email, string accessToken, CancellationToken cancellationToken)
        => _smtp.AuthenticateAsync(new SaslMechanismOAuth2(email, accessToken), cancellationToken);

    public Task<string> SendAsync(MimeMessage message, CancellationToken cancellationToken)
        => _smtp.SendAsync(message, cancellationToken);

    public void Dispose() => _smtp.Dispose();
}

public interface IGmailAuthentication
{
    string FailureMessage { get; }
    Task AuthenticateAsync(IGmailSmtpSession session, string email, CancellationToken cancellationToken);
}

public sealed class AppPasswordAuthentication : IGmailAuthentication
{
    private readonly string _password;
    public AppPasswordAuthentication(string password) => _password = AppPassword.Normalize(password);

    public string FailureMessage => "O Gmail recusou a autenticação. Confira seu email, a verificação em duas etapas e a senha de app. Gere uma nova senha de app se necessário.";

    public Task AuthenticateAsync(IGmailSmtpSession session, string email, CancellationToken cancellationToken)
        => session.AuthenticatePasswordAsync(email, _password, cancellationToken);
}
