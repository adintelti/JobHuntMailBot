using JobHunt.MailBot.Models;
using JobHunt.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace JobHunt.Web.Pages;

public sealed class IndexModel(WebFiles files, WebStore store) : UiPage
{
    [BindProperty] public string FileName { get; set; } = "vagas.txt";
    [BindProperty] public string Entry { get; set; } = "";
    [BindProperty] public IFormFile? Upload { get; set; }
    public string[] Files { get; set; } = [];
    public string DirectoryPath { get; set; } = "";
    public List<ImportedJob> Jobs { get; set; } = [];

    public async Task OnGetAsync(string? file)
    {
        if (file is not null) FileName = file;
        try { await LoadAsync(); } catch (Exception ex) { ShowError(ex); }
    }

    private async Task LoadAsync()
    {
        DirectoryPath = (await store.ReadAsync()).Settings.JobsDirectory;
        Files = await files.ListAsync();
        if (Files.Contains(FileName)) Jobs = await files.LoadAsync(FileName);
    }

    public async Task<IActionResult> OnPostAddAsync()
    {
        try
        {
            await files.AppendAsync(FileName, Entry);
            TempData["Notice"] = "Vaga adicionada ao TXT.";
            return RedirectToPage(new { file = FileName });
        }
        catch (Exception ex) { ShowError(ex); }
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostImportAsync()
    {
        try
        {
            if (Upload is null) throw new InvalidDataException("Selecione um TXT para importar.");
            await using var input = Upload.OpenReadStream();
            await files.ImportAsync(FileName, input, Upload.Length);
            TempData["Notice"] = "TXT importado. O arquivo existente nunca é substituído.";
            return RedirectToPage(new { file = FileName });
        }
        catch (Exception ex) { ShowError(ex); }
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnGetDownloadAsync(string file)
    {
        try
        {
            var path = await files.PathForAsync(file);
            return File(await System.IO.File.ReadAllBytesAsync(path), "text/plain; charset=utf-8", WebFiles.SafeName(file));
        }
        catch (Exception) { return NotFound(); }
    }
}
