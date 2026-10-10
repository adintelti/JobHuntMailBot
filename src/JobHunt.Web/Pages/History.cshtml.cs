using JobHunt.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace JobHunt.Web.Pages;

public sealed class HistoryModel(WebStore store, WebBatchWorker worker) : UiPage
{
    public List<WebBatch> Batches { get; set; } = [];
    public WebBatch? Current { get; set; }
    public async Task OnGetAsync(Guid? id)
    {
        Batches = (await store.ReadAsync()).Batches.OrderByDescending(b => b.Created).ToList();
        Current = id is null ? Batches.FirstOrDefault() : Batches.FirstOrDefault(b => b.Id == id);
    }

    public async Task<IActionResult> OnGetStatusAsync()
        => new JsonResult(new { active = (await store.ReadAsync()).Batches.Any(b => b.Active) });

    public IActionResult OnPostCancel(Guid id)
    {
        worker.Cancel(id);
        TempData["Notice"] = "Cancelamento solicitado. O resultado do envio atual será registrado antes de encerrar.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnGetLogAsync(Guid id)
    {
        var batch = (await store.ReadAsync()).Batches.FirstOrDefault(b => b.Id == id);
        if (batch is null || string.IsNullOrEmpty(batch.LogName) || Path.GetFileName(batch.LogName) != batch.LogName) return NotFound();
        var path = Path.Combine(store.Logs, batch.LogName);
        if (!System.IO.File.Exists(path)) return NotFound();
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer);
        return File(buffer.ToArray(), "application/x-ndjson", batch.LogName);
    }
}
