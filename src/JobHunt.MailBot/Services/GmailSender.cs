using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using JobHunt.MailBot.Models;

namespace JobHunt.MailBot.Services;

public sealed class GmailSender(GmailAuthorization authorization, string clientFile) : IEmailSender
{
    public async Task<string> SendAsync(JobOffer offer, MailContent content, CancellationToken cancellationToken)
    {
        var error = JobTextCodec.Validate(offer);
        if (error is not null) throw new MailSendException(error, SendOutcome.Failure);
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(content.TimeoutSeconds));
        var token = timeout.Token;
        using var smtp = new SmtpClient { Timeout = content.TimeoutSeconds * 1000 };
        using var message = new MimeMessage();
        try
        {
            message.From.Add(new MailboxAddress(content.SenderName, content.SenderEmail));
            message.To.Add(MailboxAddress.Parse(offer.Email));
            message.Subject = offer.Subject;
            message.MessageId = MimeKit.Utils.MimeUtils.GenerateMessageId();
            var builder = new BodyBuilder { TextBody = content.Body };
            if (!string.IsNullOrWhiteSpace(content.AttachmentPath))
                await builder.Attachments.AddAsync(content.AttachmentPath, token);
            message.Body = builder.ToMessageBody();
            var accessToken = await authorization.GetAccessTokenAsync(clientFile, token);
            await smtp.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls, token);
            await smtp.AuthenticateAsync(new SaslMechanismOAuth2(content.SenderEmail, accessToken), token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        { throw new MailSendException("Tempo esgotado antes de transmitir a mensagem.", SendOutcome.Failure, true); }
        catch (MailKit.Security.AuthenticationException)
        { throw new MailSendException("O Gmail recusou a autenticação. Reconecte sua conta e confira a autorização SMTP.", SendOutcome.Failure); }
        catch (Google.Apis.Auth.OAuth2.Responses.TokenResponseException)
        { throw new MailSendException("A autorização do Google expirou ou foi revogada. Conecte a conta novamente.", SendOutcome.Failure); }
        catch (Exception ex)
        {
            throw new MailSendException("Não foi possível preparar ou conectar o envio (" + ex.GetType().Name + "). "
                + (ex is InvalidOperationException ? ex.Message : "Confira a conexão, o JSON OAuth e o anexo."),
                SendOutcome.Failure, ex is IOException or System.Net.Sockets.SocketException);
        }
        {
            try
            {
                // After this starts, an interrupted connection may have delivered the DATA command.
                var response = await smtp.SendAsync(message, token);
                // A later QUIT failure cannot undo an acknowledged SMTP send.
                return "Aceito pelo servidor para envio. " + response;
            }
            catch (SmtpCommandException ex)
            {
                var code = (int)ex.StatusCode;
                throw new MailSendException($"SMTP {code}: {ex.Message}", SendOutcome.Failure,
                    code is >= 400 and < 500, code == 421 ? TimeSpan.FromSeconds(60) : null);
            }
            catch (Exception ex)
            {
                throw new MailSendException("Sem confirmação conclusiva do servidor (" + ex.GetType().Name
                    + "). Confira a pasta Enviados antes de tentar novamente.", SendOutcome.Indeterminate);
            }
        }
    }
}
