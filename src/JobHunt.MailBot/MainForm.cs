using System.ComponentModel;
using System.Diagnostics;
using JobHunt.MailBot.Models;
using JobHunt.MailBot.Services;

namespace JobHunt.MailBot;

public partial class MainForm : Form
{
    private readonly SettingsStore _settings;
    private readonly JobFileStore _files = new();
    private readonly GmailAuthorization _authorization;
    private readonly BindingList<JobRow> _rows = [];
    private readonly bool _previewMode;
    private CancellationTokenSource? _operation;
    private bool _busy, _allowClose, _closeWhenIdle;
    private string? _lastLog;

    public MainForm() : this(new SettingsStore()) { }

    public MainForm(SettingsStore settings, bool previewMode = false)
    {
        _settings = settings;
        _previewMode = previewMode;
        _authorization = new GmailAuthorization(settings.DataDirectory);
        InitializeComponent();
        StyleButtons(this);
        jobsGrid.DataSource = _rows;
        WireEvents();
        try { ApplySettings(settings.Load()); }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        { registrationStatus.Text = "Não foi possível ler as configurações anteriores. Configure novamente e salve."; }
    }

    private void WireEvents()
    {
        browseOutput.Click += async (_, _) => await ChooseOutputAsync(false);
        newOutput.Click += async (_, _) => await ChooseOutputAsync(true);
        jobInput.TextChanged += (_, _) => UpdatePreview();
        clearJob.Click += (_, _) => { jobInput.Clear(); jobInput.Focus(); };
        addJob.Click += async (_, _) => await RunOperationAsync(AddJobAsync);
        outputPath.TextChanged += (_, _) => fileCount.Text = "Caminho selecionado. O total será atualizado ao incluir uma vaga.";
        Shown += async (_, _) =>
        {
            if (!_previewMode && !string.IsNullOrWhiteSpace(outputPath.Text))
                await RunOperationAsync(UpdateFileCountAsync);
        };
        loadBatch.Click += async (_, _) => await ChooseBatchAsync();
        selectAll.Click += (_, _) => SelectRows(x => x.Offer is not null && !x.PreviouslySent
            && !x.PreviouslyIndeterminate && x.LastOutcome is null);
        selectNone.Click += (_, _) => SelectRows(_ => false);
        selectFailures.Click += (_, _) => SelectRows(x => x.LastOutcome == SendOutcome.Failure);
        jobsGrid.CurrentCellDirtyStateChanged += (_, _) =>
        { if (jobsGrid.IsCurrentCellDirty) jobsGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        jobsGrid.CellBeginEdit += (_, e) =>
        { if (_rows[e.RowIndex].Offer is null || _busy) e.Cancel = true; };
        jobsGrid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            var row = _rows[e.RowIndex];
            e.CellStyle!.ForeColor = row.Offer is null || row.LastOutcome == SendOutcome.Failure
                ? Color.FromArgb(172, 48, 48) : row.LastOutcome == SendOutcome.Success
                    ? Color.FromArgb(20, 112, 83) : Color.FromArgb(35, 49, 62);
        };
        sendBatch.Click += async (_, _) => await RunOperationAsync(SendBatchAsync);
        cancelBatch.Click += (_, _) => { _operation?.Cancel(); appStatus.Text = "Cancelamento solicitado. Registrando o resultado atual..."; };
        browseClient.Click += async (_, _) => await ChooseClientAsync();
        connectGoogle.Click += async (_, _) => await RunOperationAsync(async () =>
        {
            _operation!.CancelAfter(TimeSpan.FromMinutes(3));
            appStatus.Text = "Autorize no navegador. Para cancelar, use Cancelar operação na aba Envio em lote.";
            var email = await _authorization.ConnectAsync(clientPath.Text, _operation.Token);
            senderEmail.Text = email;
            connectionStatus.Text = "Conta autorizada";
            connectionStatus.ForeColor = Color.FromArgb(20, 112, 83);
            if (string.IsNullOrWhiteSpace(testEmail.Text)) testEmail.Text = email;
            await PersistAsync();
            AddHistory("Conta Google conectada: " + email);
        });
        disconnectGoogle.Click += async (_, _) => await RunOperationAsync(async () =>
        {
            _operation!.CancelAfter(TimeSpan.FromSeconds(30));
            try { await _authorization.DisconnectAsync(clientPath.Text, _operation.Token); }
            finally
            {
                senderEmail.Clear(); connectionStatus.Text = "Conta desconectada";
                await PersistAsync();
            }
        });
        browseAttachment.Click += async (_, _) =>
        {
            using var dialog = new OpenFileDialog { Title = "Selecionar currículo", Filter = "Documentos|*.pdf;*.docx;*.doc|Todos os arquivos|*.*" };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            { attachmentPath.Text = dialog.FileName; await RunOperationAsync(PersistAsync); }
        };
        clearAttachment.Click += async (_, _) => { attachmentPath.Clear(); await RunOperationAsync(PersistAsync); };
        saveSettings.Click += async (_, _) => await RunOperationAsync(async () =>
        { await PersistAsync(); AddHistory("Configurações salvas."); });
        sendTest.Click += async (_, _) => await RunOperationAsync(SendTestAsync);
        previewMessage.Click += (_, _) => ShowMessagePreview();
        openLogs.Click += (_, _) => OpenLogs();
        openLastLog.Click += (_, _) =>
        {
            try
            {
                var path = _lastLog ?? (Directory.Exists(_settings.LogsDirectory)
                    ? Directory.EnumerateFiles(_settings.LogsDirectory, "lote-*.jsonl").OrderDescending().FirstOrDefault() : null);
                if (path is null) { MessageBox.Show(this, "Ainda não há logs salvos.", Text); return; }
                Process.Start(new ProcessStartInfo("notepad.exe") { ArgumentList = { path }, UseShellExecute = true });
            }
            catch (Exception ex) { ShowError(ex); }
        };
        FormClosing += OnFormClosing;
    }

    private static void StyleButtons(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            if (control is Button button)
            {
                button.Height = 36; button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Color.FromArgb(201, 211, 220);
                button.BackColor = Color.FromArgb(247, 249, 251);
                button.ForeColor = Color.FromArgb(35, 49, 62);
                button.Cursor = Cursors.Hand;
                if (button.Text is "Adicionar ao arquivo" or "Enviar selecionadas" or "Conectar ao Google" or "Salvar configurações")
                {
                    button.BackColor = Color.FromArgb(15, 109, 113); button.ForeColor = Color.White;
                    button.FlatAppearance.BorderSize = 0;
                }
            }
            StyleButtons(control);
        }
    }

    private void ApplySettings(AppSettings settings)
    {
        outputPath.Text = settings.OutputFilePath; clientPath.Text = settings.OAuthClientFile;
        senderEmail.Text = settings.SenderEmail; senderName.Text = settings.SenderName;
        messageBody.Text = settings.MessageBody; attachmentPath.Text = settings.AttachmentPath;
        delaySeconds.Value = Math.Clamp(settings.DelaySeconds, 0, 3600);
        maxRetries.Value = Math.Clamp(settings.MaxRetries, 0, 3);
        timeoutSeconds.Value = Math.Clamp(settings.TimeoutSeconds, 10, 300);
        testEmail.Text = settings.SenderEmail;
        connectionStatus.Text = string.IsNullOrWhiteSpace(settings.SenderEmail) ? "Conta não conectada" : "Autorização salva";
    }

    private AppSettings ReadSettings() => new()
    {
        OutputFilePath = outputPath.Text.Trim(), OAuthClientFile = clientPath.Text,
        SenderEmail = senderEmail.Text, SenderName = senderName.Text.Trim(), MessageBody = messageBody.Text,
        AttachmentPath = attachmentPath.Text, DelaySeconds = (int)delaySeconds.Value,
        MaxRetries = (int)maxRetries.Value, TimeoutSeconds = (int)timeoutSeconds.Value
    };

    private Task PersistAsync() => _previewMode ? Task.CompletedTask : _settings.SaveAsync(ReadSettings());

    private void UpdatePreview()
    {
        try
        {
            var offer = JobTextCodec.ParseSingle(jobInput.Text);
            previewEmail.Text = offer.Email; previewSubject.Text = offer.Subject;
            registrationStatus.Text = "Vaga válida. Clique em Adicionar ao arquivo para gravar.";
            registrationStatus.ForeColor = Color.FromArgb(20, 112, 83);
        }
        catch (InvalidDataException ex)
        {
            previewEmail.Clear(); previewSubject.Clear();
            registrationStatus.Text = jobInput.TextLength == 0 ? "Cole uma vaga para visualizar os dados." : ex.Message;
            registrationStatus.ForeColor = Color.FromArgb(83, 99, 115);
        }
    }

    private async Task ChooseOutputAsync(bool createNew)
    {
        if (_busy) return;
        using var dialog = new SaveFileDialog
        {
            Title = createNew ? "Definir novo arquivo de vagas" : "Escolher TXT para acrescentar vagas",
            Filter = "Arquivo de vagas (*.txt)|*.txt", DefaultExt = "txt", AddExtension = true,
            OverwritePrompt = false, FileName = createNew ? $"vagas-{DateTime.Now:yyyy-MM-dd}.txt" : Path.GetFileName(outputPath.Text)
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (createNew && File.Exists(dialog.FileName))
        { MessageBox.Show(this, "Esse arquivo já existe. Escolha outro nome ou use Escolher TXT para acrescentar vagas.", Text); return; }
        outputPath.Text = dialog.FileName;
        await RunOperationAsync(async () => { await PersistAsync(); await UpdateFileCountAsync(); });
    }

    private async Task AddJobAsync()
    {
        var offer = JobTextCodec.ParseSingle(jobInput.Text);
        if (string.IsNullOrWhiteSpace(outputPath.Text)) throw new InvalidOperationException("Escolha o arquivo de saída antes de adicionar a vaga.");
        try { await _files.AppendAsync(outputPath.Text.Trim(), offer, cancellationToken: _operation!.Token); }
        catch (DuplicateJobException)
        {
            if (MessageBox.Show(this, "Este email e assunto já existem neste TXT. Adicionar a mesma vaga novamente?",
                "Vaga repetida", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            await _files.AppendAsync(outputPath.Text.Trim(), offer, true, _operation!.Token);
        }
        jobInput.Clear();
        registrationStatus.Text = "Vaga gravada com sucesso. Cole a próxima vaga.";
        registrationStatus.ForeColor = Color.FromArgb(20, 112, 83);
        await UpdateFileCountAsync();
        AddHistory("Vaga adicionada ao TXT: " + offer.Subject);
        await PersistAsync();
    }

    private async Task UpdateFileCountAsync()
    {
        if (string.IsNullOrWhiteSpace(outputPath.Text)) { fileCount.Text = "Nenhum arquivo selecionado."; return; }
        if (!File.Exists(outputPath.Text)) { fileCount.Text = "O arquivo será criado ao adicionar a primeira vaga."; return; }
        var items = await _files.LoadAsync(outputPath.Text);
        fileCount.Text = $"Arquivo atual: {items.Count(x => x.Offer is not null)} vagas • {items.Count(x => x.Error is not null)} problemas de formato";
    }

    private async Task ChooseClientAsync()
    {
        using var dialog = new OpenFileDialog { Title = "Selecionar cliente OAuth desktop do Google", Filter = "JSON OAuth (*.json)|*.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (!string.Equals(dialog.FileName, clientPath.Text, StringComparison.OrdinalIgnoreCase))
        { senderEmail.Clear(); connectionStatus.Text = "Conecte a conta com este cliente"; }
        clientPath.Text = dialog.FileName;
        await RunOperationAsync(PersistAsync);
    }

    private async Task ChooseBatchAsync()
    {
        using var dialog = new OpenFileDialog { Title = "Carregar vagas do TXT", Filter = "Arquivo de vagas (*.txt)|*.txt" };
        if (File.Exists(outputPath.Text)) dialog.FileName = outputPath.Text;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await RunOperationAsync(async () =>
        {
            var imported = await _files.LoadAsync(dialog.FileName, _operation!.Token);
            var protectedKeys = await BatchLog.LoadProtectedKeysAsync(_settings.LogsDirectory);
            _rows.Clear();
            var index = 0;
            foreach (var item in imported)
            {
                SendOutcome? previous = item.Offer is not null && protectedKeys.TryGetValue(item.Offer.Key, out var previousOutcome)
                    ? previousOutcome : null;
                var sent = previous == SendOutcome.Success;
                var indeterminate = previous == SendOutcome.Indeterminate;
                _rows.Add(new JobRow
                {
                    Index = ++index, Offer = item.Offer, Selected = item.Offer is not null && !sent && !indeterminate,
                    PreviouslySent = sent, PreviouslyIndeterminate = indeterminate,
                    Status = item.Error is not null ? "Inválida" : sent ? "Já enviado" : indeterminate ? "Indeterminada" : "Pendente",
                    Detail = item.Error is not null ? $"Linha {item.SourceLine}: {item.Error}" : sent
                        ? "Há sucesso anterior no histórico. Marque manualmente para reenviar."
                        : indeterminate ? "Confira a pasta Enviados antes de marcar para reenviar." : ""
                });
            }
            batchPath.Text = dialog.FileName;
            batchProgress.Value = 0;
            batchSummary.Text = $"{_rows.Count(x => x.Offer is not null)} vagas válidas • {_rows.Count(x => x.Offer is null)} problemas • {_rows.Count(x => x.PreviouslySent)} já enviadas";
            AddHistory("TXT carregado: " + dialog.FileName);
        });
    }

    private void SelectRows(Func<JobRow, bool> predicate)
    { jobsGrid.EndEdit(); foreach (var row in _rows) row.Selected = row.Offer is not null && predicate(row); }

    private MailContent ReadMailContent()
    {
        if (!File.Exists(clientPath.Text) || !JobTextCodec.IsValidEmail(senderEmail.Text))
            throw new InvalidOperationException("Selecione o JSON OAuth e conecte sua conta na aba Gmail e mensagem.");
        if (string.IsNullOrWhiteSpace(messageBody.Text)) throw new InvalidOperationException("Preencha o corpo da mensagem na aba Gmail e mensagem.");
        if (!string.IsNullOrWhiteSpace(attachmentPath.Text) && !File.Exists(attachmentPath.Text))
            throw new FileNotFoundException("O currículo selecionado não está disponível. Escolha o arquivo novamente.");
        return new(senderEmail.Text, senderName.Text.Trim(), messageBody.Text, attachmentPath.Text, (int)timeoutSeconds.Value);
    }

    private async Task SendBatchAsync()
    {
        jobsGrid.EndEdit();
        var selected = _rows.Where(x => x.Selected && x.Offer is not null).ToList();
        if (selected.Count == 0) throw new InvalidOperationException("Selecione pelo menos uma vaga válida para enviar.");
        var content = ReadMailContent();
        if (selected.Any(x => x.PreviouslySent || x.PreviouslyIndeterminate || x.LastOutcome is SendOutcome.Success or SendOutcome.Indeterminate))
        {
            if (MessageBox.Show(this, "A seleção inclui mensagens já enviadas ou sem confirmação. Confira a pasta Enviados. Reenviar mesmo assim?",
                "Confirmar reenvio", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        }
        await PersistAsync();
        var batchId = Guid.NewGuid();
        await using var log = new BatchLog(_settings.LogsDirectory, batchId);
        _lastLog = log.FilePath;
        batchProgress.Maximum = selected.Count; batchProgress.Value = 0;
        foreach (var row in selected) { row.Status = "Na fila"; row.Detail = ""; row.CompletedInCurrentBatch = false; }
        var progress = new Progress<SendResult>(result =>
        {
            var row = selected.Single(x => x.Index == result.Index);
            row.LastOutcome = result.Outcome;
            row.Status = result.IsFinal ? OutcomeText(result.Outcome) : "Nova tentativa";
            row.Detail = $"Tentativa {result.Attempt}: {result.Detail}";
            if (result.Outcome is SendOutcome.Success or SendOutcome.Indeterminate) row.Selected = false;
            row.CompletedInCurrentBatch = result.IsFinal;
            batchProgress.Value = selected.Count(x => x.CompletedInCurrentBatch);
            batchSummary.Text = $"{batchProgress.Value}/{selected.Count} processadas • acompanhando tentativas";
            AddHistory($"Vaga {result.Index} • {row.Status} • {result.Offer.Email} • {result.Detail}");
        });
        var sender = new BatchSender(new GmailSender(_authorization, clientPath.Text));
        var summary = await sender.RunAsync(batchId, batchPath.Text,
            selected.Select(x => new BatchItem(x.Index, x.Offer!)).ToList(), content,
            new BatchOptions(TimeSpan.FromSeconds((int)delaySeconds.Value), (int)maxRetries.Value), log,
            progress, _operation!.Token);
        // Progress<T> posts to the UI thread; let any final result update settle before the final summary.
        await Task.Yield();
        foreach (var row in selected.Where(x => !x.CompletedInCurrentBatch)) row.Status = "Pendente";
        batchProgress.Value = selected.Count - summary.Pending;
        batchSummary.Text = $"{(summary.Cancelled ? "Cancelado" : "Concluído")} • {summary.Success} sucessos • {summary.Failure} falhas • {summary.Indeterminate} indeterminadas • {summary.Pending} pendentes";
        AddHistory(batchSummary.Text + Environment.NewLine + "Log: " + summary.LogPath);
    }

    private async Task SendTestAsync()
    {
        if (!JobTextCodec.IsValidEmail(testEmail.Text.Trim())) throw new InvalidOperationException("Informe um destinatário válido para o teste.");
        var content = ReadMailContent();
        await PersistAsync();
        var batchId = Guid.NewGuid();
        await using var log = new BatchLog(_settings.LogsDirectory, batchId);
        _lastLog = log.FilePath;
        var sender = new BatchSender(new GmailSender(_authorization, clientPath.Text));
        var progress = new Progress<SendResult>(result => AddHistory("Teste • " + OutcomeText(result.Outcome) + " • " + result.Detail));
        var summary = await sender.RunAsync(batchId, "[teste]", [new(1, new(testEmail.Text.Trim(), "JobHunt MailBot — teste de envio"))],
            content, new(TimeSpan.Zero, 0), log, progress, _operation!.Token);
        await Task.Yield();
        MessageBox.Show(this, summary.Success == 1 ? "Email de teste aceito pelo servidor. Confira o destinatário."
            : "O teste não confirmou o envio. Consulte a aba Acompanhamento.", "Teste de envio");
    }

    private static string OutcomeText(SendOutcome outcome) => outcome switch
    { SendOutcome.Success => "Sucesso", SendOutcome.Failure => "Falha", _ => "Indeterminada" };

    private void ShowMessagePreview()
    {
        var offer = _rows.FirstOrDefault(x => x.Selected && x.Offer is not null)?.Offer;
        using var dialog = new Form
        {
            Text = "Prévia da mensagem", StartPosition = FormStartPosition.CenterParent,
            Size = new Size(800, 620), MinimumSize = new Size(600, 450), Font = Font
        };
        var preview = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.White,
            DetectUrls = false, BorderStyle = BorderStyle.None, Padding = new Padding(15), Font = Font };
        preview.Text = $"De: {senderName.Text} <{senderEmail.Text}>\n"
            + $"Para: {offer?.Email ?? "(selecione uma vaga na aba Envio em lote)"}\n"
            + $"Assunto: {offer?.Subject ?? "(assunto da vaga selecionada)"}\n"
            + $"Anexo: {(string.IsNullOrWhiteSpace(attachmentPath.Text) ? "Nenhum" : attachmentPath.Text)}\n\n"
            + messageBody.Text;
        dialog.Controls.Add(preview); dialog.ShowDialog(this);
    }

    private async Task RunOperationAsync(Func<Task> action)
    {
        if (_busy) return;
        _operation = new CancellationTokenSource();
        SetBusy(true);
        try { await action(); appStatus.Text = "Pronto."; }
        catch (OperationCanceledException) { appStatus.Text = "Operação cancelada."; }
        catch (Exception ex) { appStatus.Text = "Operação não concluída. Consulte a mensagem de erro."; ShowError(ex); }
        finally
        {
            SetBusy(false); _operation.Dispose(); _operation = null;
            if (_closeWhenIdle) Close();
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        registerTab.Enabled = !busy; settingsTab.Enabled = !busy;
        loadBatch.Enabled = !busy; selectAll.Enabled = !busy; selectNone.Enabled = !busy;
        selectFailures.Enabled = !busy; sendBatch.Enabled = !busy; jobsGrid.ReadOnly = busy;
        cancelBatch.Enabled = busy; cancelBatch.Text = busy ? "Cancelar operação" : "Cancelar lote";
        UseWaitCursor = busy;
        if (busy) appStatus.Text = "Processando...";
    }

    private void AddHistory(string text)
    {
        if (historyText.TextLength > 150_000) historyText.Text = historyText.Text[^100_000..];
        historyText.AppendText($"[{DateTime.Now:dd/MM/yyyy HH:mm:ss}] {text}{Environment.NewLine}{Environment.NewLine}");
        historyText.SelectionStart = historyText.TextLength; historyText.ScrollToCaret();
    }

    private void OpenLogs()
    {
        try
        {
            Directory.CreateDirectory(_settings.LogsDirectory);
            Process.Start(new ProcessStartInfo(_settings.LogsDirectory) { UseShellExecute = true });
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void ShowError(Exception exception)
    {
        var message = exception is Google.Apis.Auth.OAuth2.Responses.TokenResponseException
            ? "O Google recusou a autorização. Confira o cliente OAuth, os escopos e os usuários de teste; depois reconecte."
            : exception.Message;
        MessageBox.Show(this, message, "JobHunt — atenção", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private async void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose || _previewMode) return;
        e.Cancel = true;
        if (_busy)
        {
            _closeWhenIdle = true; _operation?.Cancel();
            appStatus.Text = "Finalizando e registrando os resultados antes de fechar...";
            return;
        }
        try { await PersistAsync(); }
        catch (Exception ex) { ShowError(ex); }
        _allowClose = true;
        BeginInvoke(Close);
    }

    internal void SavePreviews(string directory)
    {
        Directory.CreateDirectory(directory);
        jobInput.Text = "Email : recrutamento@empresa.example\r\nAssunto a ser colocado no email : apinfo - 85202 - An. Negocios";
        batchPath.Text = "vagas-exemplo.txt";
        _rows.Add(new() { Index = 1, Offer = new("rh@empresa.example", "apinfo - 85202 - An. Negocios"), Status = "Sucesso", LastOutcome = SendOutcome.Success, Detail = "Aceito pelo servidor para envio." });
        _rows.Add(new() { Index = 2, Offer = new("selecao@empresa.example", "apinfo - 85203 - .NET Sênior"), Status = "Falha", LastOutcome = SendOutcome.Failure, Selected = true, Detail = "SMTP 550: destinatário rejeitado." });
        _rows.Add(new() { Index = 3, Offer = new("carreiras@empresa.example", "apinfo - 85204 - Líder técnico"), Status = "Sucesso", LastOutcome = SendOutcome.Success, Detail = "Enviado após a falha da vaga 2." });
        batchSummary.Text = "Exemplo demonstrativo • 2 sucessos • 1 falha • o processamento continuou";
        AddHistory("Exemplo: vaga 2 rejeitada pelo servidor; falha registrada.");
        AddHistory("Exemplo: vaga 3 processada normalmente após a falha anterior.");
        foreach (TabPage page in tabs.TabPages)
        {
            tabs.SelectedTab = page;
            Application.DoEvents();
            using var bitmap = new Bitmap(Width, Height);
            DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
            bitmap.Save(Path.Combine(directory, $"tela-{tabs.SelectedIndex + 1}.png"));
        }
    }
}
