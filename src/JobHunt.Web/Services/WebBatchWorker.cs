using System.Collections.Concurrent;
using System.Threading.Channels;
using JobHunt.MailBot.Models;
using JobHunt.MailBot.Services;

namespace JobHunt.Web.Services;

public sealed class WebBatchWorker(WebStore store, WebFiles files, WebCredentials credentials,
    Func<string, IEmailSender> senderFactory) : BackgroundService
{
    private sealed record Work(WebBatch Batch, MailContent Content, BatchOptions Options, string Password,
        CancellationTokenSource Cancellation);
    private readonly Channel<Work> _queue = Channel.CreateUnbounded<Work>(new() { SingleReader = true });
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _cancellations = new();

    public async Task<Guid> QueueAsync(string source, IEnumerable<string> selectedKeys)
    {
        var settings = (await store.ReadAsync()).Settings;
        if (!JobTextCodec.IsValidEmail(settings.Email) || string.IsNullOrWhiteSpace(settings.Body))
            throw new InvalidOperationException("Configure o Gmail e a mensagem antes de enviar.");
        var password = await credentials.GetAsync(settings.Email)
            ?? throw new InvalidOperationException("Informe a senha de app em Gmail e mensagem.");
        var selected = selectedKeys.ToHashSet(StringComparer.Ordinal);
        var protectedKeys = await store.ProtectedKeysAsync();
        var items = (await files.LoadAsync(source)).Where(i => i.Offer is not null)
            .Select((item, index) => new WebBatchItem { Index = index + 1, Offer = item.Offer! })
            .Where(i => selected.Contains(i.Offer.Key)).DistinctBy(i => i.Offer.Key).ToList();
        if (items.Count == 0) throw new InvalidOperationException("Selecione pelo menos uma vaga válida.");
        if (items.Any(i => protectedKeys.Contains(i.Offer.Key)))
            throw new InvalidOperationException("A seleção contém emails já enviados ou indeterminados. Confira o histórico.");
        var batch = new WebBatch { Source = WebFiles.SafeName(source), Items = items };
        return await EnqueueAsync(batch, settings, password);
    }

    public async Task<Guid> QueueTestAsync()
    {
        var settings = (await store.ReadAsync()).Settings;
        if (!JobTextCodec.IsValidEmail(settings.Email) || string.IsNullOrWhiteSpace(settings.Body))
            throw new InvalidOperationException("Salve o Gmail e a mensagem antes de enviar o teste.");
        var password = await credentials.GetAsync(settings.Email)
            ?? throw new InvalidOperationException("Informe a senha de app em Gmail e mensagem.");
        var batch = new WebBatch
        {
            Source = "Teste de configuração",
            Items = [new() { Index = 1, Offer = new(settings.Email, "Teste de configuração do JobHunt") }]
        };
        return await EnqueueAsync(batch, settings, password, protectPrevious: false);
    }

    private async Task<Guid> EnqueueAsync(WebBatch batch, WebSettings settings, string password, bool protectPrevious = true)
    {
        await store.ChangeAsync(state =>
        {
            if (state.Batches.Any(b => b.Active)) throw new InvalidOperationException("Já existe um lote ativo. Aguarde ou cancele-o.");
            // Recheck under the state lock to reject stale selections from another browser tab.
            var previous = state.Batches.SelectMany(b => b.Items).Where(i => i.Outcome is SendOutcome.Success or SendOutcome.Indeterminate)
                .Select(i => i.Offer.Key).ToHashSet();
            if (protectPrevious && batch.Items.Any(i => previous.Contains(i.Offer.Key))) throw new InvalidOperationException("Essas vagas já têm envio registrado.");
            state.Batches.Add(batch);
        });
        var cancellation = new CancellationTokenSource();
        _cancellations[batch.Id] = cancellation;
        await _queue.Writer.WriteAsync(new(batch,
            new(settings.Email, settings.SenderName, settings.Body, settings.Attachment, settings.TimeoutSeconds),
            new(TimeSpan.FromSeconds(settings.DelaySeconds), settings.MaxRetries), password, cancellation));
        return batch.Id;
    }

    public void Cancel(Guid id)
    {
        if (_cancellations.TryGetValue(id, out var cancellation)) cancellation.Cancel();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var work in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, work.Cancellation.Token);
                var id = work.Batch.Id;
                try
                {
                    await using var diskLog = new BatchLog(store.Logs, id);
                    await store.ChangeAsync(s =>
                    {
                        var batch = s.Batches.Single(b => b.Id == id);
                        batch.Status = "Enviando";
                        batch.LogName = Path.GetFileName(diskLog.FilePath);
                    });
                    var tracked = new TrackedSender(senderFactory(work.Password), store, id);
                    await using var log = new StateLog(diskLog, store, id);
                    var summary = await new BatchSender(tracked).RunAsync(id, work.Batch.Source,
                        work.Batch.Items.Select(i => new BatchItem(i.Index, i.Offer)).ToList(), work.Content,
                        work.Options, log, null, linked.Token);
                    await FinishAsync(id, summary.Cancelled ? "Cancelado" : "Concluído", "");
                }
                catch (Exception)
                {
                    // Do not expose credentials, raw server replies or infrastructure exceptions in the UI.
                    try { await FinishAsync(id, "Interrompido", "Falha ao processar ou persistir o lote. Confira o armazenamento e o log antes de continuar."); }
                    catch (Exception) { /* Keep the last durable in-flight marker for recovery at startup. */ }
                }
                finally
                {
                    _cancellations.TryRemove(id, out _);
                    work.Cancellation.Dispose();
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private Task FinishAsync(Guid id, string status, string error) => store.ChangeAsync(s =>
    {
        var batch = s.Batches.Single(b => b.Id == id);
        batch.Status = status;
        batch.Error = error;
        foreach (var item in batch.Items.Where(i => i.Status == "Enviando"))
        {
            item.Status = "Indeterminado";
            item.Outcome = SendOutcome.Indeterminate;
            item.Detail = "Envio sem confirmação persistida. Confira a pasta Enviados no Gmail.";
        }
    });

    private sealed class TrackedSender(IEmailSender sender, WebStore store, Guid id) : IEmailSender
    {
        public async Task<string> SendAsync(JobOffer offer, MailContent content, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await store.ChangeAsync(s => s.Batches.Single(b => b.Id == id).Items.Single(i => i.Offer.Key == offer.Key).Status = "Enviando");
            return await sender.SendAsync(offer, content, cancellationToken);
        }
    }

    private sealed class StateLog(IBatchLog log, WebStore store, Guid id) : IBatchLog
    {
        public string FilePath => log.FilePath;
        public async Task WriteAsync(SendResult result)
        {
            await log.WriteAsync(result);
            await store.ChangeAsync(s =>
            {
                var item = s.Batches.Single(b => b.Id == id).Items.Single(i => i.Index == result.Index);
                item.Outcome = result.Outcome;
                item.Detail = result.Detail;
                item.Status = !result.IsFinal ? "Aguardando nova tentativa" : result.Outcome switch
                {
                    SendOutcome.Success => "Sucesso",
                    SendOutcome.Failure => "Falha",
                    _ => "Indeterminado"
                };
            });
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask; // The worker owns the disk log.
    }
}
