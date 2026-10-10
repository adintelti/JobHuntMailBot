using System.Net.Mail;
using System.Text.RegularExpressions;
using JobHunt.MailBot.Models;

namespace JobHunt.MailBot.Services;

public static partial class JobTextCodec
{
    public const string EmailPrefix = "Email : ";
    public const string SubjectPrefix = "Assunto a ser colocado no email : ";

    public static List<ImportedJob> Parse(string text)
    {
        var result = new List<ImportedJob>();
        string? email = null;
        var emailLine = 0;
        var lines = text.TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0) continue;
            var emailMatch = EmailPattern().Match(line);
            if (emailMatch.Success)
            {
                if (email is not null)
                    result.Add(new(emailLine, null, "Email sem a linha de assunto correspondente."));
                email = Unwrap(emailMatch.Groups[1].Value);
                emailLine = i + 1;
                continue;
            }
            var subjectMatch = SubjectPattern().Match(line);
            if (subjectMatch.Success)
            {
                if (email is null)
                {
                    result.Add(new(i + 1, null, "Assunto sem a linha de email correspondente."));
                    continue;
                }
                var offer = new JobOffer(email, Unwrap(subjectMatch.Groups[1].Value));
                var error = Validate(offer);
                result.Add(new(emailLine, error is null ? offer : null, error));
                email = null;
                continue;
            }
            if (email is not null)
            {
                result.Add(new(emailLine, null, "Bloco interrompido por uma linha não reconhecida."));
                email = null;
            }
            result.Add(new(i + 1, null, "Linha não reconhecida; esperado Email ou Assunto."));
        }
        if (email is not null)
            result.Add(new(emailLine, null, "Email sem a linha de assunto correspondente."));
        return result;
    }

    public static JobOffer ParseSingle(string text)
    {
        var items = Parse(text);
        if (items.Count != 1 || items[0].Offer is null)
            throw new InvalidDataException(items.FirstOrDefault(x => x.Error is not null)?.Error
                ?? "Cole exatamente uma vaga, com uma linha de email e outra de assunto.");
        return items[0].Offer!;
    }

    public static string Serialize(JobOffer offer)
    {
        var error = Validate(offer);
        if (error is not null) throw new InvalidDataException(error);
        return $"{EmailPrefix}{offer.Email}\r\n{SubjectPrefix}{offer.Subject}\r\n";
    }

    public static string? Validate(JobOffer offer)
    {
        if (!IsValidEmail(offer.Email)) return "Endereço de email inválido. Informe um único endereço, sem nome de exibição.";
        if (string.IsNullOrWhiteSpace(offer.Subject)) return "O assunto não pode ficar vazio.";
        if (offer.Subject.Contains('\r') || offer.Subject.Contains('\n') || offer.Subject.Contains('\0'))
            return "O assunto deve ocupar uma única linha.";
        return null;
    }

    public static bool IsValidEmail(string value) => !string.IsNullOrWhiteSpace(value)
        && !value.Any(char.IsControl) && MailAddress.TryCreate(value, out var address)
        && string.Equals(address.Address, value, StringComparison.Ordinal)
        && value.Contains('@');

    private static string Unwrap(string value)
    {
        value = value.Trim();
        return value.Length >= 4 && value.StartsWith("**") && value.EndsWith("**")
            ? value[2..^2].Trim() : value;
    }

    [GeneratedRegex(@"^Email\s*:\s*(.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"^Assunto a ser colocado no email\s*:\s*(.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SubjectPattern();
}
