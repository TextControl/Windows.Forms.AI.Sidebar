namespace TXTextControl.AISidebar.UI;

using BorderStyle = System.Windows.Forms.BorderStyle;
using Button = System.Windows.Forms.Button;

/// <summary>Streams popup previews and enables Apply only after a complete rewrite has been reviewed.</summary>
internal sealed class QuickAiResultControl : UserControl
{
    private readonly RichTextBox _result = new() { Dock = DockStyle.Fill, ReadOnly = true, DetectUrls = false,
        BackColor = Color.White, ForeColor = UiTheme.Ink, BorderStyle = BorderStyle.FixedSingle, AccessibleName = "AI result" };
    private readonly Label _status = new() { Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, Text = "Working…",
        AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _copy = UiTheme.Button("Copy");
    private readonly Button _close = UiTheme.Button("Cancel");
    private readonly Button _apply = UiTheme.Button("Apply");
    private readonly Button _retry = UiTheme.Button("Try again");
    private readonly ComboBox _tone = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Tone" };
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee };
    private readonly System.Windows.Forms.Timer _renderTimer = new() { Interval = 80 };
    private string? _pending;
    private readonly bool _rewrite;
    private bool _busy;
    private bool _applied;
    private string? _generatedTone;
    private Action? _applyResult;
    internal event Action? CloseRequested;
    internal event Action? RetryRequested;
    internal event Action<Action>? ApplyRequested;
    /// <summary>Gets the tone currently selected for rewriting.</summary>
    internal string Tone => _tone.Text;

    /// <summary>Builds a popup result view, with tone/retry/apply controls for rewrite tasks.</summary>
    internal QuickAiResultControl(string title, int width, int height, float scale, string? tone = null)
    {
        _rewrite = tone is not null;
        Size = new Size(width, height);
        BackColor = Color.White;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Margin = Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32 * scale));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, _rewrite ? 58 * scale : 0));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32 * scale));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34 * scale));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 4 * scale));
        layout.Controls.Add(new Label { Text = title, ForeColor = UiTheme.Ink, Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        var options = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty, Visible = _rewrite };
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105 * scale));
        options.RowStyles.Add(new RowStyle(SizeType.Absolute, 22 * scale));
        options.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        options.Controls.Add(new Label { Text = "Tone", Dock = DockStyle.Fill, ForeColor = UiTheme.Muted }, 0, 0);
        _tone.Items.AddRange(AiActionCatalog.Tones);
        _tone.SelectedItem = tone ?? "Professional";
        _tone.SelectedIndexChanged += (_, _) =>
        {
            _apply.Enabled = !_busy && !_applied && _applyResult is not null && Tone == _generatedTone;
            if (!_busy && !_applied && _applyResult is not null)
                _status.Text = Tone == _generatedTone ? "Review the result, then Apply." : "Choose Try again for this tone.";
        };
        _retry.Click += (_, _) => RetryRequested?.Invoke();
        options.Controls.Add(_tone, 0, 1); options.Controls.Add(_retry, 1, 1);
        layout.Controls.Add(options, 0, 1);
        layout.Controls.Add(_result, 0, 2);
        layout.Controls.Add(_status, 0, 3);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = _rewrite ? 3 : 2, Margin = Padding.Empty };
        for (int i = 0; i < footer.ColumnCount; i++) footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / footer.ColumnCount));
        _copy.Enabled = false;
        _copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(_result.Text); _status.Text = "Copied."; }
            catch { _status.Text = "Could not copy. Select the text and press Ctrl+C."; }
        };
        _close.Click += (_, _) => CloseRequested?.Invoke();
        _apply.Click += (_, _) => { if (_applyResult is not null) ApplyRequested?.Invoke(_applyResult); };
        _apply.Enabled = false;
        footer.Controls.Add(_copy, 0, 0);
        if (_rewrite) footer.Controls.Add(_apply, 1, 0);
        footer.Controls.Add(_close, _rewrite ? 2 : 1, 0);
        layout.Controls.Add(footer, 0, 4); layout.Controls.Add(_progress, 0, 5);
        Controls.Add(layout);
        _renderTimer.Tick += (_, _) => FlushPreview();
    }

    /// <summary>Queues the latest accumulated response for the next throttled UI update.</summary>
    internal void QueuePreview(string text) => _pending = text;
    /// <summary>Stops queued streaming paints when the result popup closes.</summary>
    internal void StopPreview() { if (!IsDisposed) _renderTimer.Stop(); }
    /// <summary>Clears the previous result and disables Apply while a new generation is in progress.</summary>
    internal void BeginWorking(string? tone)
    {
        _busy = true; _applyResult = null; _generatedTone = tone; _pending = null; _result.Clear();
        _apply.Enabled = _copy.Enabled = _retry.Enabled = _tone.Enabled = false;
        _status.Text = "Working…"; _close.Text = "Cancel"; _progress.Visible = true;
        _renderTimer.Start();
    }
    /// <summary>Stores completed text and its guarded Apply callback.</summary>
    internal void Complete(string text, Action apply)
    {
        _busy = false; _applyResult = _rewrite ? apply : null;
        _pending = null; _result.Text = text; _copy.Enabled = true;
        _apply.Enabled = _rewrite && Tone == _generatedTone;
        _retry.Enabled = _tone.Enabled = _rewrite;
        _status.Text = _rewrite ? "Review the result, then Apply." : "Ready.";
        _close.Text = "Close"; _progress.Visible = false;
    }
    /// <summary>Displays a failure and disables application of incomplete output.</summary>
    internal void ShowError(string message)
    {
        _busy = false; _applyResult = null;
        _pending = null; _result.Text = message; _copy.Enabled = _apply.Enabled = false;
        _retry.Enabled = _tone.Enabled = _rewrite && !_applied;
        _status.Text = ""; _close.Text = "Close"; _progress.Visible = false;
    }

    /// <summary>Keeps the reviewed result visible with an undo hint after successful application.</summary>
    internal void MarkApplied()
    {
        _applied = true; _applyResult = null;
        _apply.Enabled = _retry.Enabled = _tone.Enabled = false;
        _status.Text = "Applied. Ctrl+Z to undo.";
    }

    /// <summary>Renders only the latest queued text to avoid repaints for every streamed token.</summary>
    private void FlushPreview()
    {
        if (_pending is null) return;
        _result.Text = _pending;
        _pending = null;
    }

    /// <summary>Handles popup keyboard shortcuts before forwarding ordinary input.</summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { CloseRequested?.Invoke(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>Releases owned resources and unsubscribes events before delegating to the base lifetime.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _renderTimer.Dispose();
            if (!_rewrite) _apply.Dispose();
        }
        base.Dispose(disposing);
    }
}
