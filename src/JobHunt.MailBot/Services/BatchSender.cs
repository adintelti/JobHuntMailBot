using JobHunt.MailBot.Models;

namespace JobHunt.MailBot.Services;

public interface IEmailSender
{
    Task<string> SendAsync(JobOffer offer, MailContent content, CancellationToken cancellationToken);
}

public sealed class MailSendException(string message, SendOutcome outcome, bool retryable = false,
    TimeSpan? retryAfter = null, Exception? innerException = null) : Exception(message, innerException)
{
    public SendOutcome Outcome { get; } = outcome;
    public bool Retryable { get; } = retryable;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

public sealed class BatchSender(IEmailSender sender,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    public async Task<BatchSummary> RunAsync(Guid batchId, string sourceFile,
        IReadOnlyList<BatchItem> items, MailContent content, BatchOptions options, IBatchLog log,
        IProgress<SendResult>? progress, CancellationToken cancellationToken)
    {
        if (options.MaxRetries is < 0 or > 3 || options.Interval < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options));
        var success = 0;
        var failure = 0;
        var unknown = 0;
        var completed = 0;
        try
        {
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var attempt = 1; ; attempt++)
                {
                    SendOutcome outcome;
                    string detail;
                    MailSendException? error = null;
                    try
                    {
                        detail = await sender.SendAsync(item.Offer, content, cancellationToken);
                        outcome = SendOutcome.Success;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        // Cancellation before sending: leave this record pending.
                        throw;
                    }
                    catch (MailSendException ex)
                    {
                        error = ex;
                        outcome = ex.Outcome;
                        detail = ex.Message;
                    }
                    catch (Exception ex)
                    {
                        outcome = SendOutcome.Failure;
                        detail = "Falha no processamento: " + ex.Message;
                    }
                    var willRetry = error is { Retryable: true, Outcome: SendOutcome.Failure }
                        && attempt <= options.MaxRetries && !cancellationToken.IsCancellationRequested;
                    var result = new SendResult(batchId, sourceFile, item.Index, item.Offer,
                        DateTimeOffset.Now, attempt, outcome, detail, IsFinal: !willRetry);
                    // Do not use the cancelled token here: the result must be persisted even on cancellation.
                    await log.WriteAsync(result);
                    progress?.Report(result);
                    if (willRetry)
                    {
                        await _delay(error!.RetryAfter ?? TimeSpan.FromSeconds(5 * Math.Pow(2, attempt - 1)), cancellationToken);
                        continue;
                    }
                    switch (outcome)
                    {
                        case SendOutcome.Success: success++; break;
                        case SendOutcome.Failure: failure++; break;
                        case SendOutcome.Indeterminate: unknown++; break;
                    }
                    completed++;
                    break;
                }
                if (completed < items.Count && options.Interval > TimeSpan.Zero)
                    await _delay(options.Interval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        return new(success, failure, unknown, items.Count - completed, log.FilePath,
            cancellationToken.IsCancellationRequested);
    }
}
