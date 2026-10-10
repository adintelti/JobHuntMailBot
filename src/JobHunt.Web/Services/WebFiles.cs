using System.Text;
using JobHunt.MailBot.Models;
using JobHunt.MailBot.Services;

namespace JobHunt.Web.Services;

public sealed class WebFiles(WebStore store)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public const long MaxTextBytes = 2 * 1024 * 1024;
    public const long MaxAttachmentBytes = 10 * 1024 * 1024;

    public static string SafeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120 || name.Contains('/') || name.Contains('\\')
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Any(char.IsControl)
            || !name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || name.StartsWith('.'))
            throw new InvalidDataException("Informe apenas um nome de arquivo .txt, sem pastas.");
        return name;
    }

    public async Task<string> PathForAsync(string name)
        => Path.Combine((await store.ReadAsync()).Settings.JobsDirectory, SafeName(name));

    public async Task<string[]> ListAsync()
    {
        var directory = (await store.ReadAsync()).Settings.JobsDirectory;
        return Directory.Exists(directory) ? Directory.EnumerateFiles(directory)
            .Select(Path.GetFileName).Where(n => n is not null && n.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            .Cast<string>().Order(StringComparer.OrdinalIgnoreCase).ToArray() : [];
    }

    public async Task<List<ImportedJob>> LoadAsync(string name)
    {
        var path = await PathForAsync(name);
        if (new FileInfo(path).Length > MaxTextBytes) throw new InvalidDataException("O TXT deve ter no máximo 2 MB.");
        return await new JobFileStore().LoadAsync(path);
    }

    public async Task AppendAsync(string name, string entry)
    {
        var offer = JobTextCodec.ParseSingle(entry);
        await _gate.WaitAsync();
        try
        {
            var path = await PathForAsync(name);
            var length = File.Exists(path) ? new FileInfo(path).Length : 0;
            if (length + Encoding.UTF8.GetByteCount(JobTextCodec.Serialize(offer)) > MaxTextBytes)
                throw new InvalidDataException("O TXT deve ter no máximo 2 MB.");
            await new JobFileStore().AppendAsync(path, offer);
        }
        finally { _gate.Release(); }
    }

    public async Task ImportAsync(string name, Stream input, long length)
    {
        if (length is <= 0 or > MaxTextBytes) throw new InvalidDataException("Envie um TXT de até 2 MB.");
        using var reader = new StreamReader(input, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
        var items = JobTextCodec.Parse(await reader.ReadToEndAsync());
        if (items.Count == 0 || items.Any(i => i.Offer is null))
            throw new InvalidDataException("O TXT está vazio ou contém blocos inválidos. Corrija-o antes de importar.");
        await _gate.WaitAsync();
        try
        {
            var path = await PathForAsync(name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var bytes = Encoding.UTF8.GetBytes(string.Concat(items.Select(i => JobTextCodec.Serialize(i.Offer!))));
            if (bytes.Length > MaxTextBytes) throw new InvalidDataException("O TXT convertido excede 2 MB.");
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
                {
                    await output.WriteAsync(bytes);
                    output.Flush(flushToDisk: true);
                }
                File.Move(temp, path, overwrite: false);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        finally { _gate.Release(); }
    }

    public async Task<(string Path, string Name)> SaveAttachmentAsync(IFormFile file)
    {
        if (file.Length is <= 0 or > MaxAttachmentBytes) throw new InvalidDataException("O currículo deve ter no máximo 10 MB.");
        var name = Path.GetFileName(file.FileName.Replace('\\', '/'));
        var extension = Path.GetExtension(name).ToLowerInvariant();
        if (extension is not (".pdf" or ".doc" or ".docx" or ".rtf" or ".txt"))
            throw new InvalidDataException("Use PDF, DOC, DOCX, RTF ou TXT para o currículo.");
        var directory = Path.Combine(store.Root, "attachments");
        WebStore.PrivateDirectory(directory);
        // Keep the original basename for the MIME attachment; use a unique private parent directory.
        var uploadDirectory = Path.Combine(directory, Guid.NewGuid().ToString("N"));
        WebStore.PrivateDirectory(uploadDirectory);
        var path = Path.Combine(uploadDirectory, name);
        await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write)) await file.CopyToAsync(output);
        WebStore.PrivateFile(path);
        return (path, name);
    }
}
