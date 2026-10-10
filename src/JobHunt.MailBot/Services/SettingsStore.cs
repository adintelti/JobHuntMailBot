using System.Text.Json;
using JobHunt.MailBot.Models;

namespace JobHunt.MailBot.Services;

public sealed class SettingsStore
{
    public string DataDirectory { get; }
    public string LogsDirectory => Path.Combine(DataDirectory, "logs");
    private string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public SettingsStore(string? dataDirectory = null)
    {
        DataDirectory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JobHunt.MailBot");
    }

    public AppSettings Load()
    {
        if (!File.Exists(SettingsPath)) return new();
        return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new();
    }

    public async Task SaveAsync(AppSettings settings)
    {
        Directory.CreateDirectory(DataDirectory);
        var temp = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temp, SettingsPath, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
