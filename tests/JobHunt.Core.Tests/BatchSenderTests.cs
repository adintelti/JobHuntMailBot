using JobHunt.MailBot.Models;
using JobHunt.MailBot.Services;

namespace JobHunt.MailBot.Tests;

public sealed class BatchSenderTests
{
    private static readonly MailContent Content = new("eu@example.com", "Nome", "Candidatura", "", 30);
    private static readonly BatchItem[] Items = [new(1, new("um@example.com", "Vaga 1")),
        new(2, new("dois@example.com", "Vaga 2")), new(3, new("tres@example.com", "Vaga 3"))];

    [Fact]
    public async Task FailureOnSecondMessageDoesNotPreventThirdMessage()
    {
        var sender = new FakeSender((offer, _) => offer.Email.StartsWith("dois")
            ? throw new MailSendException("SMTP 550: destinatário rejeitado", SendOutcome.Failure)
            : Task.FromResult("Aceito"));
        await using var log = new MemoryLog();
        var summary = await new BatchSender(sender).RunAsync(Guid.NewGuid(), "vagas.txt", Items,
            Content, new(TimeSpan.Zero, 1), log, null, CancellationToken.None);
        Assert.Equal(["um@example.com", "dois@example.com", "tres@example.com"], sender.Calls);
        Assert.Equal(2, summary.Success); Assert.Equal(1, summary.Failure); Assert.Equal(0, summary.Pending);
        Assert.Equal(3, log.Results.Count);
    }

    [Fact]
    public async Task TemporaryRejectionRetriesWithinTheLimit()
    {
        var attempts = 0;
        var sender = new FakeSender((_, _) => ++attempts == 1
            ? throw new MailSendException("SMTP 450", SendOutcome.Failure, true)
            : Task.FromResult("Aceito"));
        await using var log = new MemoryLog();
        var delays = new List<TimeSpan>();
        var summary = await new BatchSender(sender, (duration, _) => { delays.Add(duration); return Task.CompletedTask; })
            .RunAsync(Guid.NewGuid(), "vagas.txt", [Items[0]], Content, new(TimeSpan.Zero, 2), log, null, CancellationToken.None);
        Assert.Equal(2, attempts); Assert.Equal(1, summary.Success); Assert.Equal(0, summary.Failure);
        Assert.False(log.Results[0].IsFinal); Assert.True(log.Results[1].IsFinal);
        Assert.Equal(TimeSpan.FromSeconds(5), Assert.Single(delays));
    }

    [Fact]
    public async Task ExhaustedRetriesAdvanceToTheNextJob()
    {
        var sender = new FakeSender((offer, _) => offer == Items[0].Offer
            ? throw new MailSendException("SMTP 450", SendOutcome.Failure, true)
            : Task.FromResult("Aceito"));
        await using var log = new MemoryLog();
        var summary = await new BatchSender(sender, (_, _) => Task.CompletedTask)
            .RunAsync(Guid.NewGuid(), "vagas.txt", Items, Content, new(TimeSpan.Zero, 2), log, null, CancellationToken.None);
        Assert.Equal(5, sender.Calls.Count); Assert.Equal(1, summary.Failure); Assert.Equal(2, summary.Success);
        Assert.Equal(3, log.Results.Count(x => x.IsFinal));
    }

    [Fact]
    public async Task IndeterminateSendIsNeverAutomaticallyRetried()
    {
        var sender = new FakeSender((offer, _) => offer == Items[0].Offer
            ? throw new MailSendException("Conexão perdida após transmissão", SendOutcome.Indeterminate, true)
            : Task.FromResult("Aceito"));
        await using var log = new MemoryLog();
        var summary = await new BatchSender(sender).RunAsync(Guid.NewGuid(), "vagas.txt", Items,
            Content, new(TimeSpan.Zero, 3), log, null, CancellationToken.None);
        Assert.Equal(3, sender.Calls.Count); Assert.Equal(1, summary.Indeterminate); Assert.Equal(2, summary.Success);
    }

    [Fact]
    public async Task CancellationDuringTransmissionPersistsTheUncertainResult()
    {
        using var cancellation = new CancellationTokenSource();
        var sender = new FakeSender((_, _) =>
        {
            cancellation.Cancel();
            throw new MailSendException("Cancelado durante a transmissão", SendOutcome.Indeterminate);
        });
        await using var log = new MemoryLog();
        var summary = await new BatchSender(sender).RunAsync(Guid.NewGuid(), "vagas.txt", Items,
            Content, new(TimeSpan.Zero, 1), log, null, cancellation.Token);
        Assert.True(summary.Cancelled); Assert.Equal(1, summary.Indeterminate); Assert.Equal(2, summary.Pending);
        Assert.Single(log.Results);
    }

    [Fact]
    public async Task CancellationBeforeSendingLeavesTheJobPending()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var sender = new FakeSender((_, _) => Task.FromResult("Aceito"));
        await using var log = new MemoryLog();
        var summary = await new BatchSender(sender).RunAsync(Guid.NewGuid(), "vagas.txt", Items,
            Content, new(TimeSpan.Zero, 1), log, null, cancellation.Token);
        Assert.Equal(3, summary.Pending); Assert.Empty(log.Results); Assert.Empty(sender.Calls);
    }

    [Fact]
    public async Task CancellationBetweenRetryAttemptsDoesNotCountTheJobAsFinished()
    {
        using var cancellation = new CancellationTokenSource();
        var sender = new FakeSender((_, _) => throw new MailSendException("SMTP 450", SendOutcome.Failure, true));
        await using var log = new MemoryLog();
        var summary = await new BatchSender(sender, (_, token) => { cancellation.Cancel(); token.ThrowIfCancellationRequested(); return Task.CompletedTask; })
            .RunAsync(Guid.NewGuid(), "vagas.txt", Items, Content, new(TimeSpan.Zero, 1), log, null, cancellation.Token);
        Assert.True(summary.Cancelled); Assert.Equal(3, summary.Pending); Assert.Equal(0, summary.Failure);
        Assert.False(Assert.Single(log.Results).IsFinal);
    }

    [Fact]
    public async Task UnexpectedItemExceptionAlsoAllowsFollowingJobs()
    {
        var sender = new FakeSender((offer, _) => offer == Items[1].Offer
            ? throw new InvalidOperationException("Erro isolado") : Task.FromResult("Aceito"));
        await using var log = new MemoryLog();
        var summary = await new BatchSender(sender).RunAsync(Guid.NewGuid(), "vagas.txt", Items,
            Content, new(TimeSpan.Zero, 0), log, null, CancellationToken.None);
        Assert.Equal(2, summary.Success); Assert.Equal(1, summary.Failure); Assert.Equal(3, sender.Calls.Count);
    }

    private sealed class FakeSender(Func<JobOffer, CancellationToken, Task<string>> send) : IEmailSender
    {
        public List<string> Calls { get; } = [];
        public Task<string> SendAsync(JobOffer offer, MailContent content, CancellationToken cancellationToken)
        { Calls.Add(offer.Email); return send(offer, cancellationToken); }
    }

    private sealed class MemoryLog : IBatchLog
    {
        public string FilePath => "memory.jsonl";
        public List<SendResult> Results { get; } = [];
        public Task WriteAsync(SendResult result) { Results.Add(result); return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
