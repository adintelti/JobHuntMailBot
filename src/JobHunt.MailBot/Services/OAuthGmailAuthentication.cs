namespace JobHunt.MailBot.Services;

public sealed class OAuthGmailAuthentication(GmailAuthorization authorization, string clientFile) : IGmailAuthentication
{
    public string FailureMessage => "O Gmail recusou a autenticação. Reconecte sua conta e confira a autorização SMTP.";

    public async Task AuthenticateAsync(IGmailSmtpSession session, string email, CancellationToken cancellationToken)
    {
        var accessToken = await authorization.GetAccessTokenAsync(clientFile, cancellationToken);
        await session.AuthenticateOAuthAsync(email, accessToken, cancellationToken);
    }
}
