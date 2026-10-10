using Microsoft.AspNetCore.Mvc.RazorPages;

namespace JobHunt.Web.Pages;

public abstract class UiPage : PageModel
{
    protected void ShowError(Exception error)
    {
        var message = error switch
        {
            InvalidDataException or InvalidOperationException or JobHunt.MailBot.Services.DuplicateJobException => error.Message,
            FileNotFoundException => "Arquivo não encontrado. Atualize a página e selecione novamente.",
            UnauthorizedAccessException => "Sem permissão para acessar o caminho configurado.",
            IOException => "Não foi possível acessar ou gravar o arquivo. Confira o nome, as permissões e se ele já existe.",
            _ => "Não foi possível concluir a operação. Confira os dados e o armazenamento local."
        };
        ModelState.AddModelError("", message);
    }
}
