using JobHunt.MailBot.Models;
using JobHunt.MailBot.Services;
using MimeKit;

namespace JobHunt.MailBot.Tests;

public sealed class GmailSenderTests
{
    private const string Password = "abcdefghijklmnop";
    private static readonly JobOffer Offer = new("rh@example.com", "apinfo - 85202 - .NET Sênior");
    private static readonly MailContent Content = new("eu@example.com", "Candidato", "Olá, envio minha candidatura.", "", 30);

    [Fact]
    public async Task SimpleModeAuthenticatesWithAppPasswordWithoutOAuthOrJsonAndSendsOneRecipient()
    {
        var session = new FakeSession();
        var sender = new GmailSender(new AppPasswordAuthentication("abcd efgh ijkl mnop"), _ => session);
        var response = await sender.SendAsync(Offer, Content, CancellationToken.None);
        Assert.Equal(["connect", "password", "send"], session.Calls);
        Assert.Equal(Content.SenderEmail, session.Email); Assert.Equal(Password, session.Password);
        Assert.Equal(Offer.Email, Assert.Single(session.Recipients));
        Assert.Equal(Offer.Subject, session.Subject); Assert.Equal(Content.Body, session.Body);
        Assert.Contains("Aceito pelo servidor", response); Assert.True(session.Disposed);
    }

    [Fact]
    public async Task RejectedPasswordIsAnIndividualFailureAndTheSecretNeverAppearsInTheError()
    {
        var session = new FakeSession { AuthenticationError = new MailKit.Security.AuthenticationException("Rejected " + Password) };
        var sender = new GmailSender(new AppPasswordAuthentication(Password), _ => session);
        var error = await Assert.ThrowsAsync<MailSendException>(() => sender.SendAsync(Offer, Content, CancellationToken.None));
        Assert.Equal(SendOutcome.Failure, error.Outcome); Assert.False(error.Retryable);
        Assert.Contains("senha de app", error.Message); Assert.DoesNotContain(Password, error.Message);
        Assert.DoesNotContain("send", session.Calls);
    }

    [Fact]
    public async Task ConnectionFailureBeforeTransmissionCanBeRetried()
    {
        var session = new FakeSession { ConnectionError = new IOException("Sem conexão") };
        var sender = new GmailSender(new AppPasswordAuthentication(Password), _ => session);
        var error = await Assert.ThrowsAsync<MailSendException>(() => sender.SendAsync(Offer, Content, CancellationToken.None));
        Assert.Equal(SendOutcome.Failure, error.Outcome); Assert.True(error.Retryable);
        Assert.DoesNotContain("send", session.Calls);
    }

    [Fact]
    public async Task ConnectionFailureDuringTransmissionIsIndeterminateAndCannotRetryAutomatically()
    {
        var session = new FakeSession { SendError = new IOException("Queda " + Password) };
        var sender = new GmailSender(new AppPasswordAuthentication(Password), _ => session);
        var error = await Assert.ThrowsAsync<MailSendException>(() => sender.SendAsync(Offer, Content, CancellationToken.None));
        Assert.Equal(SendOutcome.Indeterminate, error.Outcome); Assert.False(error.Retryable);
        Assert.DoesNotContain(Password, error.Message);
    }

    [Fact]
    public async Task AlternativeAuthenticationCanStillUseAnOAuthToken()
    {
        var session = new FakeSession();
        var sender = new GmailSender(new StubOAuthAuthentication(), _ => session);
        await sender.SendAsync(Offer, Content, CancellationToken.None);
        Assert.Equal(["connect", "oauth", "send"], session.Calls);
        Assert.Null(session.Password);
    }

    private sealed class StubOAuthAuthentication : IGmailAuthentication
    {
        public string FailureMessage => "Autorização recusada";
        public Task AuthenticateAsync(IGmailSmtpSession session, string email, CancellationToken cancellationToken)
            => session.AuthenticateOAuthAsync(email, "access-token-test", cancellationToken);
    }

    private sealed class FakeSession : IGmailSmtpSession
    {
        public List<string> Calls { get; } = [];
        public string? Email { get; private set; }
        public string? Password { get; private set; }
        public List<string> Recipients { get; private set; } = [];
        public string? Subject { get; private set; }
        public string? Body { get; private set; }
        public Exception? ConnectionError { get; init; }
        public Exception? AuthenticationError { get; init; }
        public Exception? SendError { get; init; }
        public bool Disposed { get; private set; }
        public Task ConnectAsync(CancellationToken cancellationToken)
        { Calls.Add("connect"); if (ConnectionError is not null) throw ConnectionError; return Task.CompletedTask; }
        public Task AuthenticatePasswordAsync(string email, string password, CancellationToken cancellationToken)
        {
            Calls.Add("password"); Email = email; Password = password;
            if (AuthenticationError is not null) throw AuthenticationError;
            return Task.CompletedTask;
        }
        public Task AuthenticateOAuthAsync(string email, string accessToken, CancellationToken cancellationToken)
        { Calls.Add("oauth"); Email = email; return Task.CompletedTask; }
        public Task<string> SendAsync(MimeMessage message, CancellationToken cancellationToken)
        {
            Calls.Add("send"); if (SendError is not null) throw SendError;
            Recipients = message.To.Mailboxes.Select(x => x.Address).ToList();
            Subject = message.Subject; Body = message.TextBody;
            return Task.FromResult("250 queued");
        }
        public void Dispose() => Disposed = true;
    }
}
