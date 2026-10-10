using JobHunt.MailBot.Models;
using JobHunt.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace JobHunt.Web.Pages;

public sealed class BatchModel(WebFiles files, WebStore store, WebBatchWorker worker) : UiPage
{
    [BindProperty(SupportsGet = true, Name = "file")] public string FileName { get; set; } = "";
    [BindProperty] public List<string> Selected { get; set; } = [];
    [BindProperty] public bool Confirm { get; set; }
    public string[] Files { get; set; } = [];
    public List<ImportedJob> Jobs { get; set; } = [];
    public HashSet<string> Protected { get; set; } = [];
    public WebSettings Settings { get; set; } = new();
    public WebBatch? Active { get; set; }

    public async Task OnGetAsync()
    {
        try { await LoadAsync(); } catch (Exception ex) { ShowError(ex); }
    }

    private async Task LoadAsync()
    {
        Files = await files.ListAsync();
        if (FileName.Length == 0) FileName = Files.FirstOrDefault() ?? "";
        if (FileName.Length > 0) Jobs = await files.LoadAsync(FileName);
        Protected = await store.ProtectedKeysAsync();
        var state = await store.ReadAsync();
        Settings = state.Settings;
        Active = state.Batches.LastOrDefault(b => b.Active);
    }

    public async Task<IActionResult> OnPostSendAsync()
    {
        try
        {
            if (!Confirm) throw new InvalidOperationException("Confirme que revisou os destinatários e a mensagem.");
            var id = await worker.QueueAsync(FileName, Selected);
            TempData["Notice"] = "Lote iniciado. Você pode fechar a aba; mantenha o serviço JobHunt em execução.";
            return RedirectToPage("/History", new { id });
        }
        catch (Exception ex) { ShowError(ex); }
        try { await LoadAsync(); } catch (Exception ex) { ShowError(ex); }
        return Page();
    }
}
