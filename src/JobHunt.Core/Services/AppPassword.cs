namespace JobHunt.MailBot.Services;

public static class AppPassword
{
    public static string Normalize(string password)
    {
        var compact = string.Concat(password.Where(c => !char.IsWhiteSpace(c)));
        if (compact.Length != 16 || !compact.All(char.IsAsciiLetterOrDigit))
            throw new InvalidOperationException("Informe a senha de app de 16 caracteres gerada pelo Google. Não use a senha habitual da conta.");
        return compact;
    }
}
