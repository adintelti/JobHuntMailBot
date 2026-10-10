using System.Text;
using JobHunt.MailBot.Models;
using JobHunt.MailBot.Services;

namespace JobHunt.MailBot.Tests;

public sealed class JobTextTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MailBot-tests-" + Guid.NewGuid().ToString("N"));
    private readonly JobFileStore _files = new();
    private static JobOffer Offer(string subject = "apinfo - 85202 - An. Negócios") => new("recrutamento@example.com", subject);

    [Fact]
    public void PastedBoldValuesAreUnwrappedWithoutChangingTheSubject()
    {
        var parsed = JobTextCodec.ParseSingle("Email : **recrutamento@example.com**\r\nAssunto a ser colocado no email : **apinfo - 85202 - An. Negócios**");
        Assert.Equal(Offer(), parsed);
    }

    [Fact]
    public void MissingSubjectDoesNotStealTheNextJobsSubject()
    {
        var parsed = JobTextCodec.Parse("Email : perdido@example.com\n" + JobTextCodec.Serialize(Offer()));
        Assert.Equal(2, parsed.Count);
        Assert.Null(parsed[0].Offer);
        Assert.Equal(1, parsed[0].SourceLine);
        Assert.Equal(Offer(), parsed[1].Offer);
    }

    [Fact]
    public void UnexpectedLineBreaksTheBlockAndParsingRecovers()
    {
        var parsed = JobTextCodec.Parse("Email : perdido@example.com\nconteúdo inesperado\nAssunto a ser colocado no email : órfão\n" + JobTextCodec.Serialize(Offer()));
        Assert.Equal(3, parsed.Count(x => x.Error is not null));
        Assert.Equal(Offer(), parsed.Single(x => x.Offer is not null).Offer);
    }

    [Theory]
    [InlineData("")]
    [InlineData("pessoa sem email")]
    [InlineData("Nome <recrutamento@example.com>")]
    [InlineData("um@example.com;dois@example.com")]
    [InlineData("um@example.com\r\nBcc: outro@example.com")]
    public void RejectsInvalidOrMultipleAddresses(string email)
        => Assert.False(JobTextCodec.IsValidEmail(email));

    [Fact]
    public void SingleEntryRejectsMultipleJobs()
        => Assert.Throws<InvalidDataException>(() => JobTextCodec.ParseSingle(JobTextCodec.Serialize(Offer()) + JobTextCodec.Serialize(Offer("outra"))));

    [Fact]
    public void SubjectHeaderInjectionIsRejected()
        => Assert.Throws<InvalidDataException>(() => JobTextCodec.Serialize(Offer("assunto\r\nBcc: outro@example.com")));

    [Fact]
    public async Task AppendingMatchesExactUtf8WithoutBomAndCrLfFormat()
    {
        var path = Path.Combine(_directory, "vagas.txt");
        await _files.AppendAsync(path, Offer());
        await _files.AppendAsync(path, Offer("apinfo - 89063 -"));
        var expected = "Email : recrutamento@example.com\r\nAssunto a ser colocado no email : apinfo - 85202 - An. Negócios\r\n"
            + "Email : recrutamento@example.com\r\nAssunto a ser colocado no email : apinfo - 89063 -\r\n";
        Assert.Equal(new UTF8Encoding(false).GetBytes(expected), await File.ReadAllBytesAsync(path));
        Assert.Equal(2, (await _files.LoadAsync(path)).Count);
    }

    [Fact]
    public async Task ExistingFileWithoutFinalNewlineGetsASeparator()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "vagas.txt");
        await File.WriteAllTextAsync(path, JobTextCodec.Serialize(Offer()).TrimEnd('\r', '\n'));
        await _files.AppendAsync(path, Offer("outra vaga"));
        var items = await _files.LoadAsync(path);
        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.NotNull(item.Offer));
    }

    [Fact]
    public async Task DuplicateNeedsExplicitOverrideAndOriginalFileIsPreserved()
    {
        var path = Path.Combine(_directory, "vagas.txt");
        await _files.AppendAsync(path, Offer());
        var before = await File.ReadAllBytesAsync(path);
        await Assert.ThrowsAsync<DuplicateJobException>(() => _files.AppendAsync(path, Offer() with { Email = "RECRUTAMENTO@example.com" }));
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        await _files.AppendAsync(path, Offer(), allowDuplicate: true);
        Assert.Equal(2, (await _files.LoadAsync(path)).Count);
    }

    [Fact]
    public async Task DifferentSubjectsForTheSameEmailAreDifferentJobs()
    {
        var path = Path.Combine(_directory, "vagas.txt");
        await _files.AppendAsync(path, Offer(".NET Sênior"));
        await _files.AppendAsync(path, Offer("Líder técnico"));
        Assert.Equal(2, (await _files.LoadAsync(path)).Count);
    }

    [Fact]
    public async Task OtherFileExtensionsAreRejected()
        => await Assert.ThrowsAsync<InvalidDataException>(() => _files.AppendAsync(Path.Combine(_directory, "vagas.json"), Offer()));

    [Fact]
    public async Task AppendingToUtf16NeverMixesEncodingsOrChangesTheFile()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "vagas.txt");
        await File.WriteAllTextAsync(path, JobTextCodec.Serialize(Offer()), Encoding.Unicode);
        var before = await File.ReadAllBytesAsync(path);
        await Assert.ThrowsAsync<InvalidDataException>(() => _files.AppendAsync(path, Offer("outra vaga")));
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task AppendingToAnIncompleteBlockNeverAssociatesItWithANewJob()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "vagas.txt");
        await File.WriteAllTextAsync(path, "Email : perdido@example.com\r\n");
        var before = await File.ReadAllBytesAsync(path);
        await Assert.ThrowsAsync<InvalidDataException>(() => _files.AppendAsync(path, Offer()));
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
