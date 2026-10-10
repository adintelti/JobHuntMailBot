namespace JobHunt.MailBot;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static int Main(string[] args)
    {
        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        if (args.Length == 2 && args[0] == "--verify-file")
        {
            var items = new Services.JobFileStore().LoadAsync(args[1]).GetAwaiter().GetResult();
            Console.WriteLine($"Vagas válidas: {items.Count(x => x.Offer is not null)}; erros: {items.Count(x => x.Error is not null)}");
            return items.Any(x => x.Error is not null) ? 1 : 0;
        }
        if (args.Length == 2 && args[0] == "--preview")
        {
            using var preview = new MainForm(new Services.SettingsStore(
                Path.Combine(Path.GetTempPath(), "JobHunt-preview-" + Guid.NewGuid().ToString("N"))), previewMode: true);
            preview.Show();
            Application.DoEvents();
            preview.SavePreviews(args[1]);
            preview.Close();
            return 0;
        }
        Application.Run(new MainForm());
        return 0;
    }
}
