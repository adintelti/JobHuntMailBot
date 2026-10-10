using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobHunt.MailBot.Models;

namespace JobHunt.MailBot.Services;

public interface IBatchLog : IAsyncDisposable
{
    string FilePath { get; }
    Task WriteAsync(SendResult result);
}

public sealed class BatchLog : IBatchLog
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly StreamWriter _writer;
    public string FilePath { get; }

    public BatchLog(string directory, Guid batchId)
    {
        Directory.CreateDirectory(directory);
        FilePath = Path.Combine(directory, $"lote-{DateTime.Now:yyyyMMdd-HHmmss}-{batchId:N}.jsonl");
        _writer = new StreamWriter(new FileStream(FilePath, FileMode.CreateNew, FileAccess.Write,
            FileShare.Read, 4096, FileOptions.Asynchronous), new UTF8Encoding(false));
    }

    public async Task WriteAsync(SendResult result)
    {
        await _writer.WriteLineAsync(JsonSerializer.Serialize(result, JsonOptions));
        await _writer.FlushAsync();
    }

    public ValueTask DisposeAsync() => _writer.DisposeAsync();

    public static async Task<Dictionary<string, SendOutcome>> LoadProtectedKeysAsync(string directory)
    {
        var keys = new Dictionary<string, SendOutcome>(StringComparer.Ordinal);
        if (!Directory.Exists(directory)) return keys;
        foreach (var path in Directory.EnumerateFiles(directory, "lote-*.jsonl"))
        {
            using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            while (await reader.ReadLineAsync() is { } line)
            {
                try
                {
                    var result = JsonSerializer.Deserialize<SendResult>(line, JsonOptions);
                    if (result?.Offer is not null && result.Outcome is SendOutcome.Success or SendOutcome.Indeterminate)
                    {
                        var key = result.Offer.Key;
                        if (!keys.TryGetValue(key, out var old) || old != SendOutcome.Success)
                            keys[key] = result.Outcome;
                    }
                }
                catch (JsonException) { /* A truncated final line is not evidence of a successful send. */ }
            }
        }
        return keys;
    }
}
