using TXTextControl.AISidebar.AI;

namespace TXTextControl.AISidebar.UI;

using BorderStyle = System.Windows.Forms.BorderStyle;
using Button = System.Windows.Forms.Button;
using Image = System.Drawing.Image;

/// <summary>Sidebar content and preview UI. AiAssistant supplies the native sidebar and editor integration.</summary>
[System.ComponentModel.ToolboxItem(false)]
public sealed class AiSidebarControl : UserControl
{
    private readonly List<Button> _actions = [];
    private readonly Button _settings = UiTheme.Button("");
    private readonly ToolTip _toolTip = new();
    private readonly TableLayoutPanel _header = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
    private readonly ComboBox _scope = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _tone = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _language = new() { Dock = DockStyle.Fill };
    private readonly TextBox _instructions = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical,
        PlaceholderText = "e.g. Keep it under 100 words, or extract invoice fields…" };
    private readonly RichTextBox _result = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
        BackColor = Color.White, ForeColor = UiTheme.Ink, ReadOnly = true, DetectUrls = false };
    private readonly Label _status = new() { Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, Text = "Choose an action to get started." };
    private readonly Button _cancel = UiTheme.Button("Cancel");
    private readonly TableLayoutPanel _feedback = new() { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
    private readonly Button _replace = UiTheme.Button("Replace source");
    private readonly Button _insert = UiTheme.Button("Insert at cursor");
    private readonly Button _copy = UiTheme.Button("Copy result");
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee, Visible = false };
    private readonly System.Windows.Forms.Timer _renderTimer = new() { Interval = 80 };
    private string? _pendingResult;
    private bool _busy;
    private readonly TableLayoutPanel _layout;

    /// <summary>Occurs when the user starts a sidebar action.</summary>
    public event Action<AiAction>? ActionRequested;
    /// <summary>Occurs when the user opens connection settings from the sidebar header.</summary>
    public event Action? SettingsRequested;
    /// <summary>Occurs when the user cancels a running request.</summary>
    public event Action? CancelRequested;
    /// <summary>Occurs when the user asks to replace the captured source with a completed result.</summary>
    public event Action? ReplaceRequested;
    /// <summary>Occurs when the user asks to insert the result at the caret.</summary>
    public event Action? InsertRequested;
    /// <summary>Gets the selected source scope used when capturing an AI request.</summary>
    public string Scope => _scope.SelectedItem?.ToString() ?? "Selection or document";
    /// <summary>Gets the tone currently selected for rewriting.</summary>
    public string Tone => _tone.Text;
    /// <summary>Gets the target language for translation.</summary>
    public string Language => _language.Text.Trim();
    /// <summary>Gets trimmed additional task instructions.</summary>
    public string Instructions => _instructions.Text.Trim();
    /// <summary>Gets the displayed result text.</summary>
    public string ResultText => _result.Text;

    /// <summary>Builds the action controls, scope preferences, throttled result preview and review commands.</summary>
    public AiSidebarControl()
    {
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9);
        BackColor = UiTheme.Surface;
        Dock = DockStyle.Fill;
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, RowCount = 11, Padding = new Padding(16) };
        _layout = layout;
        float[] heights = [28, 28, 132, 48, 20, 48, 24, 0, 32, 34, 4];
        AutoScroll = true;
        for (int i = 0; i < heights.Length; i++)
            layout.RowStyles.Add(new RowStyle(i == 7 ? SizeType.Percent : SizeType.Absolute, i == 7 ? 100 : heights[i]));
        // Keep Settings in the content header so it follows the native sidebar when detached.
        _header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28));
        _header.Controls.Add(UiTheme.Label("Text", true), 0, 0);
        _settings.Dock = DockStyle.Fill;
        _settings.Margin = Padding.Empty;
        _settings.FlatStyle = FlatStyle.Flat;
        _settings.FlatAppearance.BorderSize = 0;
        _settings.AccessibleName = "Settings";
        _settings.AccessibleDescription = "Open AI connection settings";
        _settings.Cursor = Cursors.Hand;
        _settings.Click += (_, _) => SettingsRequested?.Invoke();
        _toolTip.SetToolTip(_settings, "Settings");
        _header.Controls.Add(_settings, 1, 0);
        layout.Controls.Add(_header, 0, 0);
        _scope.Items.AddRange(["Selection or document", "Selection only", "Whole document"]); _scope.SelectedIndex = 0;
        layout.Controls.Add(_scope, 0, 1);
        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Margin = new Padding(0, 6, 0, 0) };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (int i = 0; i < 4; i++) actions.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        var items = AiActionCatalog.Items;
        for (int i = 0; i < items.Length; i++)
        {
            var action = items[i].Action;
            var button = UiTheme.Button($"{items[i].Symbol}  {items[i].Title}");
            button.Click += (_, _) => ActionRequested?.Invoke(action);
            actions.Controls.Add(button, i % 2, i / 2);
            _actions.Add(button);
        }
        layout.Controls.Add(actions, 0, 2);
        var options = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        options.Controls.Add(UiTheme.Label("Tone"), 0, 0); options.Controls.Add(UiTheme.Label("Translate to"), 1, 0);
        _tone.Items.AddRange(AiActionCatalog.Tones); _tone.SelectedIndex = 0;
        _language.Items.AddRange(["English", "German", "French", "Spanish", "Italian", "Dutch", "Japanese"]); _language.Text = "English";
        options.Controls.Add(_tone, 0, 1); options.Controls.Add(_language, 1, 1);
        layout.Controls.Add(options, 0, 3);
        layout.Controls.Add(UiTheme.Label("Additional instructions"), 0, 4);
        layout.Controls.Add(_instructions, 0, 5);
        var resultTitle = UiTheme.Label("Result", true);
        layout.Controls.Add(resultTitle, 0, 6);
        var resultFrame = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(10), Margin = new Padding(0, 3, 0, 6) };
        resultFrame.Controls.Add(_result);
        layout.Controls.Add(resultFrame, 0, 7);
        _feedback.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _feedback.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0));
        _feedback.Controls.Add(_status, 0, 0); _feedback.Controls.Add(_cancel, 1, 0);
        _cancel.Visible = false;
        layout.Controls.Add(_feedback, 0, 8);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty };
        for (int i = 0; i < 3; i++) footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        footer.Controls.Add(_replace, 0, 0); footer.Controls.Add(_insert, 1, 0); footer.Controls.Add(_copy, 2, 0);
        layout.Controls.Add(footer, 0, 9);
        layout.Controls.Add(_progress, 0, 10);
        Controls.Add(layout);
        var custom = UiTheme.Button("Run custom instruction");
        custom.Click += (_, _) => ActionRequested?.Invoke(AiAction.Custom);
        _actions.Add(custom);
        actions.Controls.Add(custom, 0, 3);
        actions.SetColumnSpan(custom, 2);
        _cancel.Click += (_, _) => CancelRequested?.Invoke();
        _replace.Click += (_, _) => ReplaceRequested?.Invoke();
        _insert.Click += (_, _) => InsertRequested?.Invoke();
        _copy.Click += (_, _) => { try { Clipboard.SetText(ResultText); } catch (Exception ex) { SetStatus(ex.Message); } };
        _renderTimer.Tick += (_, _) => FlushPreview();
        _renderTimer.Start();
        SetResultActions(false);
    }

    /// <summary>Labels the replacement command to identify whether it affects a selection or the main document.</summary>
    public void SetSourceKind(bool selection) => _replace.Text = selection ? "Replace selection" : "Replace document";

    /// <summary>Initializes layout once native control DPI is available.</summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ScaleRows();
    }

    /// <summary>Recomputes logical row heights after a monitor DPI change.</summary>
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ScaleRows();
    }

    /// <summary>Scales fixed rows and reserves a scrollable minimum height for the result preview.</summary>
    private void ScaleRows()
    {
        float[] heights = [28, 28, 132, 48, 20, 48, 24, 0, 32, 34, 4];
        float scale = DeviceDpi / 96f;
        _layout.SuspendLayout();
        _header.ColumnStyles[1].Width = 28 * scale;
        // Recreate the owned bitmap only when the monitor DPI changes, not on every resize.
        int iconSize = (int)Math.Round(16 * scale);
        if (_settings.Image?.Width != iconSize)
        {
            var previous = _settings.Image;
            _settings.Image = SidebarIcons.Settings(iconSize);
            previous?.Dispose();
        }
        for (int i = 0; i < heights.Length; i++)
            if (i != 7) _layout.RowStyles[i].Height = heights[i] * scale;
        _feedback.ColumnStyles[1].Width = _busy ? 88 * scale : 0;
        _layout.Height = Math.Max(ClientSize.Height, (int)(614 * scale));
        AutoScrollMinSize = new Size(0, (int)(614 * scale));
        _layout.ResumeLayout(true);
    }

    /// <summary>Keeps the result layout usable when the native sidebar is resized.</summary>
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_layout is not null) ScaleRows();
    }

    /// <summary>Disables conflicting actions during inference and shows cancellation/progress UI.</summary>
    public void SetBusy(bool busy)
    {
        _busy = busy;
        _settings.Enabled = !busy;
        foreach (var action in _actions) action.Enabled = !busy;
        _scope.Enabled = _tone.Enabled = _language.Enabled = _instructions.Enabled = !busy;
        _cancel.Visible = busy;
        _progress.Visible = busy;
        SetResultActions(false);
        ScaleRows();
    }

    /// <summary>Queues the latest accumulated response for the next throttled UI update.</summary>
    public void QueuePreview(string result) => _pendingResult = result;
    /// <summary>Clears queued and displayed text and disables result commands.</summary>
    public void ClearResult() { _pendingResult = null; _result.Clear(); SetResultActions(false); }
    /// <summary>Displays a completed response and enables review commands.</summary>
    public void CompleteResult(string result) { _pendingResult = null; _result.Text = result; SetResultActions(true); }
    /// <summary>Updates the concise user-facing status line.</summary>
    public void SetStatus(string status) => _status.Text = status;
    /// <summary>Disables replacement because the captured source is no longer current.</summary>
    public void InvalidateReplacement() => _replace.Enabled = false;

    /// <summary>Renders only the latest queued text to avoid repaints for every streamed token.</summary>
    private void FlushPreview()
    {
        if (_pendingResult is null) return;
        _result.Text = _pendingResult;
        _result.SelectionStart = _result.TextLength;
        _result.ScrollToCaret();
        _pendingResult = null;
    }

    /// <summary>Enables result commands only when a completed result is available and no request is active.</summary>
    private void SetResultActions(bool ready) => _replace.Enabled = _insert.Enabled = _copy.Enabled = ready && !_busy;

    /// <summary>Releases owned resources and unsubscribes events before delegating to the base lifetime.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _renderTimer.Stop(); _renderTimer.Dispose();
            _toolTip.Dispose();
            var image = _settings.Image;
            _settings.Image = null;
            image?.Dispose();
        }
        base.Dispose(disposing);
    }
}
