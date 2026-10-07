using TXTextControl.AI;
using TXTextControl.AISidebar.AI;

namespace TXTextControl.AISidebar.UI;

using Button = System.Windows.Forms.Button;

/// <summary>Hosted/local-model setup UI, including connection testing and encrypted settings persistence.</summary>
[System.ComponentModel.ToolboxItem(false)]
public sealed class AiConnectionDialog : Form
{
    private readonly ComboBox _provider = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _endpoint = new() { Dock = DockStyle.Fill };
    private readonly TextBox _model = new() { Dock = DockStyle.Fill, PlaceholderText = "Model ID from your provider" };
    private readonly TextBox _key = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly CheckBox _remember = new() { Text = "Remember API key with Windows encryption", AutoSize = true };
    private readonly CheckBox _suggest = new() { Text = "Suggest text while typing (Tab to accept)", AutoSize = true };
    private readonly CheckBox _grammar = new() { Text = "Check grammar while typing", AutoSize = true };
    private readonly CheckBox _grammarIdle = new() { Text = "Check the rest of the document when idle", AutoSize = true };
    private readonly NumericUpDown _timeout = Number(10, 600);
    private readonly NumericUpDown _tokens = Number(128, 16384, 128);
    private readonly NumericUpDown _input = Number(1000, 500000, 1000);
    private readonly TextBox _modelPath = new() { Dock = DockStyle.Fill, PlaceholderText = "Choose a local .gguf model file" };
    private readonly TextBox _runtimePath = new() { Dock = DockStyle.Fill, PlaceholderText = "Optional; otherwise use the installed runtime cache" };
    private readonly NumericUpDown _context = Number(2048, 131072, 1024);
    private readonly NumericUpDown _gpuLayers = Number(-1, 999);
    private readonly ComboBox _backend = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _download = new() { Text = "Download the verified inference runtime if missing", AutoSize = true };
    private readonly Button _test = UiTheme.Button("Test AI / load model");
    private readonly Button _save = UiTheme.Button("Save connection");
    private readonly Button _unload = UiTheme.Button("Unload local model");
    private readonly Label _status = new() { Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, AutoEllipsis = true };
    private readonly TableLayoutPanel _root;
    private readonly List<TableLayoutPanel> _pages = [];
    private readonly AiAssistantService _service;
    private readonly AiSettingsStore _store;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<Control> _editable = [];
    /// <summary>Gets the accepted connection settings after the user saves the dialog.</summary>
    public AiConnectionSettings Settings { get; private set; }

    /// <summary>Builds the setup tabs from existing settings; saving occurs only when Save connection succeeds.</summary>
    public AiConnectionDialog(AiConnectionSettings settings, AiAssistantService service, AiSettingsStore store)
    {
        SuspendLayout();
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        Settings = settings; _service = service; _store = store;
        Text = "AI connection";
        Font = new Font("Segoe UI", 10); BackColor = Color.White; ForeColor = UiTheme.Ink;
        ClientSize = new Size(660, 730); MinimumSize = new Size(600, 660);
        StartPosition = FormStartPosition.CenterParent; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        _root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(22) };
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var api = AddPage(tabs, "AI provider");
        _provider.Items.AddRange(["OpenAI-compatible API", "Local GGUF model"]);
        _provider.SelectedIndex = (int)settings.Provider;
        AddRow(api, "Provider", _provider);
        AddRow(api, "API base URL", _endpoint); AddRow(api, "Model ID", _model); AddRow(api, "API key", _key);
        var showKey = new CheckBox { Text = "Show API key", AutoSize = true };
        showKey.CheckedChanged += (_, _) => _key.UseSystemPasswordChar = !showKey.Checked;
        AddWide(api, showKey); AddWide(api, _remember);
        AddRow(api, "Timeout (seconds)", _timeout); AddRow(api, "Output token limit", _tokens); AddRow(api, "Input character limit", _input);
        _suggest.Checked = settings.SuggestWhileTyping;
        AddWide(api, _suggest);
        _grammar.Checked = settings.CheckGrammarWhileTyping;
        AddWide(api, _grammar);
        _grammarIdle.Checked = settings.CheckGrammarWhenIdle;
        _grammarIdle.Enabled = _grammar.Checked;
        _grammar.CheckedChanged += (_, _) => _grammarIdle.Enabled = _grammar.Checked;
        AddWide(api, _grammarIdle);
        AddNote(api, "Hosted AI sends checked text to your endpoint. Idle grammar checks gradually cover the document when enabled. For local inference, configure the Local model tab.");

        var local = AddPage(tabs, "Local model");
        AddRow(local, "GGUF model", BrowseRow(_modelPath, "GGUF models|*.gguf", () => _provider.SelectedIndex = (int)AiProvider.LocalGguf));
        AddRow(local, "llama-server", BrowseRow(_runtimePath, "llama-server executable|*.exe"));
        AddRow(local, "Context tokens", _context); AddRow(local, "GPU layers", _gpuLayers);
        _backend.Items.AddRange(Enum.GetValues<HardwareBackend>().Cast<object>().ToArray());
        _backend.SelectedItem = settings.HardwareBackend;
        AddRow(local, "Hardware", _backend); AddWide(local, _download); AddWide(local, _unload);
        AddNote(local, "Model files stay on this machine; no model weights are downloaded. Use GPU layers -1 for automatic offload or 0 for CPU. The app loads once, reuses the model, and releases it on exit.");

        _root.Controls.Add(tabs, 0, 0); _root.Controls.Add(_status, 0, 1);
        var buttons = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 3, RowCount = 1 };
        buttons.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        for (int i = 0; i < 3; i++) buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        var cancel = UiTheme.Button("Cancel"); cancel.DialogResult = DialogResult.Cancel;
        foreach (var button in new[] { _test, cancel, _save })
        {
            button.AutoSize = true;
            button.Dock = DockStyle.Top;
            button.MinimumSize = new Size(0, 34);
            button.Padding = new Padding(8, 0, 8, 0);
        }
        buttons.Controls.Add(_test, 0, 0); buttons.Controls.Add(cancel, 1, 0); buttons.Controls.Add(_save, 2, 0);
        _root.Controls.Add(buttons, 0, 2); Controls.Add(_root);
        CancelButton = cancel; AcceptButton = _save;
        _endpoint.Text = settings.Endpoint; _model.Text = settings.Model; _key.Text = settings.ApiKey;
        _remember.Checked = settings.RememberKey; _modelPath.Text = settings.LocalModelPath; _runtimePath.Text = settings.LlamaServerExecutablePath;
        _download.Checked = settings.AllowRuntimeDownload;
        SetNumber(_timeout, settings.TimeoutSeconds); SetNumber(_tokens, settings.MaxOutputTokens); SetNumber(_input, settings.MaxInputCharacters);
        SetNumber(_context, settings.ContextSize); SetNumber(_gpuLayers, settings.GpuLayers);
        _provider.SelectedIndexChanged += (_, _) => UpdateEnabled();
        _save.Click += (_, _) => SaveConnection();
        _test.Click += async (_, _) => await RunTestAsync(async settings =>
        {
            await _service.TestConnectionAsync(settings, _lifetime.Token, ReportStatus);
            return settings.Provider == AiProvider.LocalGguf ? "Local model loaded and responded successfully." : "AI connection verified.";
        });
        _unload.Click += async (_, _) => await RunTestAsync(async _ => { await _service.UnloadModelAsync(_lifetime.Token); return "Local model unloaded."; });
        FormClosing += (_, _) => _lifetime.Cancel();
        UpdateEnabled(); ResumeLayout(true);
    }

    /// <summary>Creates a bounded numeric editor for connection limits.</summary>
    private static NumericUpDown Number(int minimum, int maximum, int increment = 1) => new() { Minimum = minimum, Maximum = maximum, Increment = increment, Dock = DockStyle.Fill };
    /// <summary>Clamps a persisted value to the numeric control's supported range.</summary>
    private static void SetNumber(NumericUpDown control, int value) => control.Value = Math.Clamp(value, (int)control.Minimum, (int)control.Maximum);

    /// <summary>Creates an independently scrollable setup tab.</summary>
    private TableLayoutPanel AddPage(TabControl tabs, string title)
    {
        var page = new TabPage(title) { BackColor = Color.White, Padding = new Padding(10), AutoScroll = true };
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(8) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.Controls.Add(layout); tabs.TabPages.Add(page); _pages.Add(layout); return layout;
    }

    /// <summary>Adds a labeled setting and tracks its editor for busy-state changes.</summary>
    private void AddRow(TableLayoutPanel layout, string text, Control control)
    {
        int row = layout.RowCount++; layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var label = UiTheme.Label(text); label.Margin = new Padding(0, 8, 0, 8);
        control.Margin = new Padding(0, 6, 0, 8);
        layout.Controls.Add(label, 0, row); layout.Controls.Add(control, 1, row); _editable.Add(control);
    }
    /// <summary>Adds a setting spanning both columns.</summary>
    private static void AddWide(TableLayoutPanel layout, Control control)
    {
        int row = layout.RowCount++; layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Margin = new Padding(0, 8, 0, 8);
        layout.Controls.Add(control, 0, row); layout.SetColumnSpan(control, 2);
    }
    /// <summary>Adds a wrapping explanatory note within the setup grid.</summary>
    private static void AddNote(TableLayoutPanel layout, string text)
    {
        var label = UiTheme.Label(text); label.Dock = DockStyle.Fill;
        AddWide(layout, label);
    }
    /// <summary>Pairs a path field with an existing-file picker.</summary>
    private static Control BrowseRow(TextBox input, string filter, Action? onSelected = null)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Height = 36, Margin = Padding.Empty };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85));
        var browse = UiTheme.Button("Browse…");
        browse.Click += (_, _) => { using var dialog = new OpenFileDialog { Filter = filter }; if (dialog.ShowDialog(input.FindForm()) == DialogResult.OK) { input.Text = dialog.FileName; onSelected?.Invoke(); } };
        row.Controls.Add(input, 0, 0); row.Controls.Add(browse, 1, 0); return row;
    }

    /// <summary>Fits the dialog to the working area and scales fixed dimensions using current DPI.</summary>
    protected override void OnLoad(EventArgs e)
    {
        float scale = DeviceDpi / 96f;
        var area = Screen.FromControl(this).WorkingArea;
        ClientSize = new Size((int)(660 * scale), Math.Min((int)(730 * scale), area.Height - 70));
        foreach (var page in _pages) page.ColumnStyles[0].Width = 145 * scale;
        _root.RowStyles[1].Height = 70 * scale;
        base.OnLoad(e);
    }

    /// <summary>Copies edited fields into an immutable settings record without persisting it.</summary>
    private AiConnectionSettings ReadSettings() => Settings with
    {
        Provider = (AiProvider)_provider.SelectedIndex, Endpoint = _endpoint.Text.Trim(), Model = _model.Text.Trim(), ApiKey = _key.Text.Trim(),
        RememberKey = _remember.Checked, TimeoutSeconds = (int)_timeout.Value, MaxOutputTokens = (int)_tokens.Value, MaxInputCharacters = (int)_input.Value,
        SuggestWhileTyping = _suggest.Checked,
        CheckGrammarWhileTyping = _grammar.Checked,
        CheckGrammarWhenIdle = _grammarIdle.Checked,
        LocalModelPath = _modelPath.Text.Trim(), LlamaServerExecutablePath = _runtimePath.Text.Trim(), AllowRuntimeDownload = _download.Checked,
        ContextSize = (int)_context.Value, GpuLayers = (int)_gpuLayers.Value, HardwareBackend = (HardwareBackend)(_backend.SelectedItem ?? HardwareBackend.Auto)
    };

    /// <summary>Validates and persists edited settings before closing with an accepted result.</summary>
    private void SaveConnection()
    {
        try { var settings = ReadSettings(); _store.Save(settings); Settings = settings; DialogResult = DialogResult.OK; Close(); }
        catch (Exception ex) { ReportStatus(ex.Message); }
    }
    /// <summary>Runs a cancelable connection/model operation while conflicting edits are disabled.</summary>
    private async Task RunTestAsync(Func<AiConnectionSettings, Task<string>> operation)
    {
        var settings = ReadSettings(); SetBusy(true); ReportStatus("Working…");
        try { var message = await operation(settings); if (!IsDisposed) ReportStatus(message); }
        catch (OperationCanceledException) { if (!IsDisposed) ReportStatus("Operation canceled or timed out."); }
        catch (Exception ex) { if (!IsDisposed) ReportStatus(AiErrorMessage.For(ex, settings.ApiKey)); }
        finally { if (!IsDisposed) SetBusy(false); }
    }
    /// <summary>Marshals operation progress to the dialog UI thread.</summary>
    private void ReportStatus(string message)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) { BeginInvoke(() => ReportStatus(message)); return; }
        _status.Text = message;
    }
    /// <summary>Disables conflicting actions during inference and shows cancellation/progress UI.</summary>
    private void SetBusy(bool busy)
    {
        foreach (var control in _editable) control.Enabled = !busy;
        _test.Enabled = _save.Enabled = _unload.Enabled = !busy;
        _remember.Enabled = _download.Enabled = !busy;
        if (!busy) UpdateEnabled();
    }
    /// <summary>Enables fields appropriate to the chosen provider and loaded model state.</summary>
    private void UpdateEnabled()
    {
        bool remote = _provider.SelectedIndex == (int)AiProvider.RemoteApi;
        _endpoint.Enabled = _model.Enabled = _key.Enabled = remote;
        _unload.Enabled = _service.LoadedModelPath is not null;
    }
    /// <summary>Releases owned resources and unsubscribes events before delegating to the base lifetime.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed)
        {
            _lifetime.Cancel(); _lifetime.Dispose();
        }
        base.Dispose(disposing);
    }
}
