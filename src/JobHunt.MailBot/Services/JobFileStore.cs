using System.Text;
using JobHunt.MailBot.Models;

namespace JobHunt.MailBot.Services;

public sealed class DuplicateJobException() : Exception("Este email e assunto já existem no arquivo.");

public sealed class JobFileStore
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public async Task<List<ImportedJob>> LoadAsync(string path, CancellationToken cancellationToken = default)
        => JobTextCodec.Parse(await File.ReadAllTextAsync(path, Utf8, cancellationToken));

    public async Task AppendAsync(string path, JobOffer offer, bool allowDuplicate = false,
        CancellationToken cancellationToken = default)
    {
        var serialized = JobTextCodec.Serialize(offer);
        path = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(path), ".txt", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Escolha um arquivo com extensão .txt.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite,
            FileShare.Read, 4096, FileOptions.Asynchronous);
        var originalLength = stream.Length;
        string existing;
        try
        {
            using var reader = new StreamReader(stream, Utf8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            existing = await reader.ReadToEndAsync(cancellationToken);
        }
        catch (DecoderFallbackException)
        { throw new InvalidDataException("O TXT existente não está em UTF-8. Converta sua codificação antes de acrescentar vagas."); }
        var previous = JobTextCodec.Parse(existing);
        if (previous.Any(x => x.Error is not null))
            throw new InvalidDataException("O TXT existente contém blocos inválidos. Corrija o formato ou escolha outro arquivo antes de acrescentar vagas.");
        if (!allowDuplicate && previous.Any(x => x.Offer?.Key == offer.Key))
            throw new DuplicateJobException();
        var separator = existing.EndsWith('\r') ? "\n"
            : existing.Length > 0 && !existing.EndsWith('\n') ? "\r\n" : "";
        var bytes = Utf8.GetBytes(separator + serialized);
        stream.Seek(0, SeekOrigin.End);
        try
        {
            await stream.WriteAsync(bytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        catch
        {
            // Restore the original file if an append was only partially written.
            try { stream.SetLength(originalLength); } catch (IOException) { }
            throw;
        }
    }
}
