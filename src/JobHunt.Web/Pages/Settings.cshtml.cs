using JobHunt.MailBot.Services;
using JobHunt.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace JobHunt.Web.Pages;

public sealed class SettingsModel(WebStore store, WebCredentials credentials, WebFiles files, WebBatchWorker worker) : UiPage
{
    [BindProperty] public WebSettings Input { get; set; } = new();
    [BindProperty] public string? Password { get; set; }
    [BindProperty] public IFormFile? Attachment { get; set; }
    [BindProperty] public bool RemoveAttachment { get; set; }
    public bool HasPassword { get; set; }
    public string DataDirectory => store.Root;

    public async Task OnGetAsync()
    {
        Input = (await store.ReadAsync()).Settings;
        try { HasPassword = await credentials.GetAsync(Input.Email) is not null; }
        catch (Exception ex) { ShowError(ex); }
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        try
        {
            if (!ModelState.IsValid) throw new InvalidDataException("Confira os valores informados nos campos.");
            Input.Email = Input.Email.Trim();
            Input.SenderName = Input.SenderName.Trim();
            if (!JobTextCodec.IsValidEmail(Input.Email)) throw new InvalidDataException("Informe um email válido.");
            if (Input.SenderName.Length > 200 || Input.SenderName.Any(char.IsControl)) throw new InvalidDataException("Nome do remetente inválido.");
            if (string.IsNullOrWhiteSpace(Input.Body) || Input.Body.Length > 50000) throw new InvalidDataException("Escreva uma mensagem de até 50 mil caracteres.");
            if (Input.DelaySeconds is < 0 or > 3600 || Input.MaxRetries is < 0 or > 3 || Input.TimeoutSeconds is < 10 or > 120)
                throw new InvalidDataException("Confira o intervalo (0–3600), novas tentativas (0–3) e tempo limite (10–120).");
            if (string.IsNullOrWhiteSpace(Input.JobsDirectory) || !Path.IsPathFullyQualified(Input.JobsDirectory))
                throw new InvalidDataException("Informe um caminho absoluto para a pasta de vagas na máquina do servidor.");
            Input.JobsDirectory = Path.GetFullPath(Input.JobsDirectory);
            Directory.CreateDirectory(Input.JobsDirectory);
            // Attachment paths are never taken from posted form fields.
            var previous = (await store.ReadAsync()).Settings;
            Input.Attachment = RemoveAttachment ? "" : previous.Attachment;
            Input.AttachmentName = RemoveAttachment ? "" : previous.AttachmentName;
            if (Attachment is not null)
            {
                var saved = await files.SaveAttachmentAsync(Attachment);
                Input.Attachment = saved.Path;
                Input.AttachmentName = saved.Name;
            }
            await credentials.UpdateAsync(Input.Email, Password, Input.RememberPassword);
            if (!string.Equals(previous.Email, Input.Email, StringComparison.OrdinalIgnoreCase) && previous.Email.Length > 0)
                await credentials.ForgetAsync(previous.Email);
            await store.ChangeAsync(s => s.Settings = Input);
            TempData["Notice"] = "Configurações salvas. A senha nunca é exibida ao carregar a página.";
            return RedirectToPage();
        }
        catch (Exception ex) { ShowError(ex); }
        Password = null;
        ModelState.Remove(nameof(Password));
        Input.AttachmentName = (await store.ReadAsync()).Settings.AttachmentName;
        try { HasPassword = await credentials.GetAsync(Input.Email) is not null; }
        catch (Exception) { HasPassword = false; }
        return Page();
    }

    public async Task<IActionResult> OnPostForgetAsync()
    {
        var email = (await store.ReadAsync()).Settings.Email;
        await credentials.ForgetAsync(email);
        await store.ChangeAsync(s => s.Settings.RememberPassword = false);
        TempData["Notice"] = "Senha removida da sessão e do armazenamento local. A credencial no Google permanece ativa.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostTestAsync()
    {
        try
        {
            var id = await worker.QueueTestAsync();
            TempData["Notice"] = "Teste iniciado para o próprio email configurado, com a mensagem e o currículo salvos.";
            return RedirectToPage("/History", new { id });
        }
        catch (Exception ex) { ShowError(ex); }
        await OnGetAsync();
        return Page();
    }
}
