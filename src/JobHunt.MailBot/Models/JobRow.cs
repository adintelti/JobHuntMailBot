using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace JobHunt.MailBot.Models;

public sealed class JobRow : INotifyPropertyChanged
{
    private bool _selected;
    private string _status = "Pendente";
    private string _detail = "";
    public int Index { get; init; }
    public JobOffer? Offer { get; init; }
    public string Email => Offer?.Email ?? "—";
    public string Subject => Offer?.Subject ?? "—";
    public bool Selected { get => _selected; set { _selected = value; Changed(); } }
    public string Status { get => _status; set { _status = value; Changed(); } }
    public string Detail { get => _detail; set { _detail = value; Changed(); } }
    public SendOutcome? LastOutcome { get; set; }
    public bool PreviouslySent { get; init; }
    public bool PreviouslyIndeterminate { get; init; }
    public bool CompletedInCurrentBatch { get; set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
