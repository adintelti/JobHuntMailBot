using System.Text;
using JobHunt.MailBot.Models;
using JobHunt.MailBot.Services;
using JobHunt.Web.Services;
using Microsoft.AspNetCore.DataProtection;

namespace JobHunt.Web.Tests;

public sealed class TestDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jobhunt-web-tests-" + Guid.NewGuid().ToString("N"));
    public TestDirectory() => Directory.CreateDirectory(Path);
    public void Dispose()
    {
        var absolute = System.IO.Path.GetFullPath(Path);
        var prefix = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jobhunt-web-tests-");
        if (!absolute.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected test directory.");
        if (Directory.Exists(absolute)) Directory.Delete(absolute, true);
    }
}

public sealed class WebServicesTests : IDisposable
{
    private readonly TestDirectory _directory = new();
    private const string Password = "abcdefghijklmnop";
    private readonly JobOffer[] _offers = [new("one@example.com", "Vaga 1"), new("two@example.com", "Vaga 2"), new("three@example.com", "Vaga 3")];
    public void Dispose() => _directory.Dispose();

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("..\\secret.txt")]
    [InlineData("/tmp/file.txt")]
    [InlineData(".hidden.txt")]
    [InlineData("vagas.json")]
    public void RejectsTraversalAndInvalidNames(string name) => Assert.Throws<InvalidDataException>(() => WebFiles.SafeName(name));

    [Fact]
    public async Task ImportsAndAppendsExactUtf8CrlfAndRefusesOverwrite()
    {
        var store = new WebStore(_directory.Path);
        var files = new WebFiles(store);
        var input = Encoding.UTF8.GetBytes("Email : um@example.com\nAssunto a ser colocado no email : Ação C#\n");
        await files.ImportAsync("vagas.txt", new MemoryStream(input), input.Length);
        await files.AppendAsync("vagas.txt", JobTextCodec.Serialize(_offers[0]));
        var path = await files.PathForAsync("vagas.txt");
        Assert.Equal(Encoding.UTF8.GetBytes(JobTextCodec.Serialize(new("um@example.com", "Ação C#")) + JobTextCodec.Serialize(_offers[0])), await File.ReadAllBytesAsync(path));
        await Assert.ThrowsAsync<IOException>(() => files.ImportAsync("vagas.txt", new MemoryStream(input), input.Length));
        await Assert.ThrowsAsync<DuplicateJobException>(() => files.AppendAsync("vagas.txt", JobTextCodec.Serialize(_offers[0])));
    }

    [Fact]
    public async Task InvalidImportDoesNotCreateFile()
    {
        var files = new WebFiles(new WebStore(_directory.Path));
        var input = Encoding.UTF8.GetBytes("Email : bad@example.com\n");
        await Assert.ThrowsAsync<InvalidDataException>(() => files.ImportAsync("bad.txt", new MemoryStream(input), input.Length));
        Assert.Empty(await files.ListAsync());
    }

    [Fact]
    public async Task CredentialsSurviveRestartEncryptedAndAreBoundToAccount()
    {
        var store = new WebStore(_directory.Path);
        using var certificate = WebCredentials.LoadCertificate(store.Root);
        var keys = new DirectoryInfo(System.IO.Path.Combine(store.Root, "keys"));
        var provider = DataProtectionProvider.Create(keys, b => b.SetApplicationName("test").ProtectKeysWithCertificate(certificate));
        var credentials = new WebCredentials(store, provider);
        await credentials.UpdateAsync("me@example.com", Password, true);
        Assert.Null(await credentials.GetAsync("other@example.com"));
        var encrypted = await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(System.IO.Path.Combine(store.Root, "credentials"))));
        Assert.DoesNotContain(Password, encrypted);
        using var restartedCertificate = WebCredentials.LoadCertificate(store.Root);
        var restartedProvider = DataProtectionProvider.Create(keys, b => b.SetApplicationName("test").ProtectKeysWithCertificate(restartedCertificate));
        var restarted = new WebCredentials(store, restartedProvider);
        Assert.Equal(Password, await restarted.GetAsync("ME@example.com"));
        await restarted.UpdateAsync("me@example.com", null, false);
        Assert.Empty(Directory.GetFiles(System.IO.Path.Combine(store.Root, "credentials")));
        Assert.Equal(Password, await restarted.GetAsync("me@example.com"));
        Assert.Null(await new WebCredentials(store, restartedProvider).GetAsync("me@example.com"));
        await restarted.ForgetAsync("me@example.com");
        Assert.Null(await restarted.GetAsync("me@example.com"));
    }

    [Fact]
    public async Task ForgettingOldAccountKeepsCurrentSessionCredential()
    {
        var store = new WebStore(_directory.Path);
        var credentials = new WebCredentials(store, new EphemeralDataProtectionProvider());
        await credentials.UpdateAsync("old@example.com", Password, true);
        await credentials.UpdateAsync("new@example.com", "ponmlkjihgfedcba", false);
        await credentials.ForgetAsync("old@example.com");
        Assert.Equal("ponmlkjihgfedcba", await credentials.GetAsync("new@example.com"));
        Assert.Null(await credentials.GetAsync("old@example.com"));
    }

    [Fact]
    public async Task RestartMarksInFlightUnknownAndDoesNotResumePendingItems()
    {
        var store = new WebStore(_directory.Path);
        var batch = new WebBatch { Status = "Enviando", Items = [new() { Offer = _offers[0], Status = "Enviando" }, new() { Offer = _offers[1] }] };
        await store.ChangeAsync(s => s.Batches.Add(batch));
        var restarted = new WebStore(_directory.Path);
        var recovered = Assert.Single((await restarted.ReadAsync()).Batches);
        Assert.Equal("Interrompido", recovered.Status);
        Assert.Equal(SendOutcome.Indeterminate, recovered.Items[0].Outcome);
        Assert.Equal("Pendente", recovered.Items[1].Status);
        Assert.Contains(_offers[0].Key, await restarted.ProtectedKeysAsync());
        Assert.DoesNotContain(_offers[1].Key, await restarted.ProtectedKeysAsync());
    }

    [Fact]
    public async Task WorkerContinuesAfterFailurePersistsLogAndBlocksSuccessResend()
    {
        var (store, files, credentials) = await PrepareAsync();
        var calls = new List<string>();
        using var worker = new WebBatchWorker(store, files, credentials, _ => new FakeSender((offer, _) =>
        {
            calls.Add(offer.Email);
            if (offer == _offers[1]) throw new MailSendException("Destinatário recusado", SendOutcome.Failure);
            return Task.FromResult("Aceito");
        }));
        await worker.StartAsync(CancellationToken.None);
        var id = await worker.QueueAsync("vagas.txt", _offers.Select(o => o.Key));
        var batch = await WaitFinishedAsync(store, id);
        Assert.Equal("Concluído", batch.Status);
        Assert.Equal(new[] { "Sucesso", "Falha", "Sucesso" }, batch.Items.Select(i => i.Status));
        Assert.Equal(3, calls.Count);
        Assert.Equal(3, (await File.ReadAllLinesAsync(System.IO.Path.Combine(store.Logs, batch.LogName))).Length);
        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.QueueAsync("vagas.txt", [_offers[0].Key]));
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task WorkerRejectsConcurrentBatchAndCancellationLeavesRemainingPending()
    {
        var (store, files, credentials) = await PrepareAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var worker = new WebBatchWorker(store, files, credentials, _ => new FakeSender(async (_, token) =>
        {
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) { throw new MailSendException("Transmissão interrompida", SendOutcome.Indeterminate); }
            return "Aceito";
        }));
        await worker.StartAsync(CancellationToken.None);
        var id = await worker.QueueAsync("vagas.txt", _offers.Select(o => o.Key));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.QueueAsync("vagas.txt", [_offers[2].Key]));
        worker.Cancel(id);
        var batch = await WaitFinishedAsync(store, id);
        Assert.Equal("Cancelado", batch.Status);
        Assert.Equal("Indeterminado", batch.Items[0].Status);
        Assert.All(batch.Items.Skip(1), i => Assert.Equal("Pendente", i.Status));
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task InFlightMarkerIsDurableBeforeCallingSmtp()
    {
        var (store, files, credentials) = await PrepareAsync();
        var markerSeen = false;
        using var worker = new WebBatchWorker(store, files, credentials, _ => new FakeSender((_, _) =>
        {
            var recovered = new WebStore(_directory.Path).ReadAsync().GetAwaiter().GetResult();
            markerSeen = recovered.Batches.Single().Items[0].Outcome == SendOutcome.Indeterminate;
            return Task.FromResult("Aceito");
        }));
        await worker.StartAsync(CancellationToken.None);
        var id = await worker.QueueAsync("vagas.txt", [_offers[0].Key]);
        await WaitFinishedAsync(store, id);
        Assert.True(markerSeen);
        await worker.StopAsync(CancellationToken.None);
    }

    private async Task<(WebStore, WebFiles, WebCredentials)> PrepareAsync()
    {
        var store = new WebStore(_directory.Path);
        var files = new WebFiles(store);
        var credentials = new WebCredentials(store, new EphemeralDataProtectionProvider());
        await credentials.UpdateAsync("me@example.com", Password, false);
        await store.ChangeAsync(s => s.Settings = new() { JobsDirectory = s.Settings.JobsDirectory, Email = "me@example.com", Body = "Candidatura", DelaySeconds = 0, MaxRetries = 0 });
        foreach (var offer in _offers) await files.AppendAsync("vagas.txt", JobTextCodec.Serialize(offer));
        return (store, files, credentials);
    }

    [Fact]
    public async Task TestEmailUsesSavedSenderAndIsExplicitlyRepeatable()
    {
        var (store, files, credentials) = await PrepareAsync();
        var recipients = new List<string>();
        using var worker = new WebBatchWorker(store, files, credentials, _ => new FakeSender((offer, _) =>
        {
            recipients.Add(offer.Email);
            return Task.FromResult("Aceito");
        }));
        await worker.StartAsync(CancellationToken.None);
        var first = await worker.QueueTestAsync();
        await WaitFinishedAsync(store, first);
        var second = await worker.QueueTestAsync();
        await WaitFinishedAsync(store, second);
        Assert.Equal(new[] { "me@example.com", "me@example.com" }, recipients);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StorageFailureStopsBeforeAnySmtpCall()
    {
        var (store, files, credentials) = await PrepareAsync();
        var calls = 0;
        using var worker = new WebBatchWorker(store, files, credentials, _ => new FakeSender((_, _) =>
        {
            calls++;
            return Task.FromResult("Aceito");
        }));
        var id = await worker.QueueAsync("vagas.txt", _offers.Select(o => o.Key));
        Directory.CreateDirectory(System.IO.Path.Combine(store.Root, "web-state.json.tmp"));
        await worker.StartAsync(CancellationToken.None);
        var batch = await WaitFinishedAsync(store, id);
        Assert.Equal("Interrompido", batch.Status);
        Assert.Equal(0, calls);
        await worker.StopAsync(CancellationToken.None);
    }

    private static async Task<WebBatch> WaitFinishedAsync(WebStore store, Guid id)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (true)
        {
            var batch = (await store.ReadAsync()).Batches.Single(b => b.Id == id);
            if (!batch.Active) return batch;
            await Task.Delay(25, timeout.Token);
        }
    }

    private sealed class FakeSender(Func<JobOffer, CancellationToken, Task<string>> send) : IEmailSender
    {
        public Task<string> SendAsync(JobOffer offer, MailContent content, CancellationToken cancellationToken) => send(offer, cancellationToken);
    }
}
