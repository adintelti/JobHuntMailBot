using System.Text.Json;
using JobHunt.MailBot.Models;
using JobHunt.MailBot.Services;

namespace JobHunt.Web.Services;

public sealed class WebSettings
{
    public string JobsDirectory { get; set; } = "";
    public string Email { get; set; } = "";
    public string SenderName { get; set; } = "";
    public string Body { get; set; } = "";
    public string Attachment { get; set; } = "";
    public string AttachmentName { get; set; } = "";
    public bool RememberPassword { get; set; }
    public int DelaySeconds { get; set; } = 3;
    public int MaxRetries { get; set; } = 1;
    public int TimeoutSeconds { get; set; } = 45;
}

public sealed class WebBatchItem
{
    public int Index { get; set; }
    public JobOffer Offer { get; set; } = new("", "");
    public string Status { get; set; } = "Pendente";
    public string Detail { get; set; } = "";
    public SendOutcome? Outcome { get; set; }
}

public sealed class WebBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
    public string Source { get; set; } = "";
    public string Status { get; set; } = "Na fila";
    public string Error { get; set; } = "";
    public string LogName { get; set; } = "";
    public List<WebBatchItem> Items { get; set; } = [];
    public bool Active => Status is "Na fila" or "Enviando";
}

public sealed class WebState
{
    public WebSettings Settings { get; set; } = new();
    public List<WebBatch> Batches { get; set; } = [];
}

public sealed class WebStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly WebState _state;
    private static readonly JsonSerializerOptions Json = new(BatchLog.JsonOptions) { WriteIndented = true };
    public string Root { get; }
    public string Logs => Path.Combine(Root, "logs");
    private string StatePath => Path.Combine(Root, "web-state.json");

    public WebStore(string root)
    {
        Root = Path.GetFullPath(root);
        PrivateDirectory(Root);
        PrivateDirectory(Logs);
        _state = File.Exists(StatePath)
            ? JsonSerializer.Deserialize<WebState>(File.ReadAllText(StatePath), Json)
                ?? throw new InvalidDataException("Estado web inválido.")
            : new();
        if (string.IsNullOrWhiteSpace(_state.Settings.JobsDirectory))
            _state.Settings.JobsDirectory = Path.Combine(Root, "jobs");
        foreach (var batch in _state.Batches.Where(b => b.Active))
        {
            batch.Status = "Interrompido";
            batch.Error = "O serviço foi reiniciado. Itens pendentes não foram reenviados.";
            foreach (var item in batch.Items.Where(i => i.Status == "Enviando"))
            {
                item.Status = "Indeterminado";
                item.Outcome = SendOutcome.Indeterminate;
                item.Detail = "Serviço interrompido durante o envio. Confira a pasta Enviados no Gmail.";
            }
        }
        Save();
    }

    public static void PrivateDirectory(string path)
    {
        Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    public static void PrivateFile(string path)
    {
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private void Save()
    {
        var temp = StatePath + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write))
        {
            JsonSerializer.Serialize(stream, _state, Json);
            stream.Flush(flushToDisk: true);
        }
        PrivateFile(temp);
        File.Move(temp, StatePath, true);
    }

    public async Task<WebState> ReadAsync()
    {
        await _gate.WaitAsync();
        try { return JsonSerializer.Deserialize<WebState>(JsonSerializer.Serialize(_state, Json), Json)!; }
        finally { _gate.Release(); }
    }

    public async Task ChangeAsync(Action<WebState> change)
    {
        await _gate.WaitAsync();
        try { change(_state); Save(); }
        finally { _gate.Release(); }
    }

    public async Task<HashSet<string>> ProtectedKeysAsync()
    {
        var logged = await BatchLog.LoadProtectedKeysAsync(Logs);
        var keys = logged.Keys.ToHashSet(StringComparer.Ordinal);
        var state = await ReadAsync();
        foreach (var item in state.Batches.SelectMany(b => b.Items)
            .Where(i => i.Outcome is SendOutcome.Success or SendOutcome.Indeterminate || i.Status == "Enviando"))
            keys.Add(item.Offer.Key);
        return keys;
    }
}
