using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace JobHunt.MailBot.Models;

public sealed record JobOffer(string Email, string Subject)
{
    public string Key => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(Email.ToLowerInvariant() + "\0" + Subject)));
}

public sealed record ImportedJob(int SourceLine, JobOffer? Offer, string? Error);

public enum SendOutcome { Success, Failure, Indeterminate }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EmailAuthenticationMode { AppPassword, OAuth2 }

public sealed record SendResult(Guid BatchId, string SourceFile, int Index, JobOffer Offer,
    DateTimeOffset Timestamp, int Attempt, SendOutcome Outcome, string Detail, bool IsFinal = true);

public sealed class AppSettings
{
    public string OutputFilePath { get; set; } = "";
    public string OAuthClientFile { get; set; } = "";
    public string SenderEmail { get; set; } = "";
    public EmailAuthenticationMode AuthenticationMode { get; set; } = EmailAuthenticationMode.AppPassword;
    public string AppPasswordEmail { get; set; } = "";
    public string OAuthSenderEmail { get; set; } = "";
    public bool RememberAppPassword { get; set; }
    public string SenderName { get; set; } = "";
    public string MessageBody { get; set; } = "";
    public string AttachmentPath { get; set; } = "";
    public int DelaySeconds { get; set; } = 3;
    public int MaxRetries { get; set; } = 1;
    public int TimeoutSeconds { get; set; } = 45;
}

public sealed record MailContent(string SenderEmail, string SenderName, string Body,
    string AttachmentPath, int TimeoutSeconds);

public sealed record BatchOptions(TimeSpan Interval, int MaxRetries);

public sealed record BatchItem(int Index, JobOffer Offer);

public sealed record BatchSummary(int Success, int Failure, int Indeterminate, int Pending,
    string LogPath, bool Cancelled);
