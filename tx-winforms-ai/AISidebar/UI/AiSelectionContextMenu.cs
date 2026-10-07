using System.ComponentModel;
using System.Drawing.Drawing2D;
using TXTextControl;
using TXTextControl.AISidebar.AI;

namespace TXTextControl.AISidebar.UI;

using Button = System.Windows.Forms.Button;

/// <summary>Selection shortcut and additions to TX's native context menu.</summary>
internal sealed class AiSelectionContextMenu : Component
{
    private readonly TextControl _editor;
    private readonly Func<bool> _canRun;
    private readonly Func<QuickAiRunner?> _captureRequest;
    private readonly Func<string> _language;
    private readonly Func<string> _tone;
    private readonly SelectionShortcut _shortcut = new();
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 150 };
    private readonly ToolTip _tooltip = new();
    private readonly Font _menuFont = new("Segoe UI", 10);
    private readonly HashSet<ToolStripDropDown> _menus = [];
    private ContextMenuStrip? _popup;
    private ContextMenuStrip? _nativeMenu;
    private bool _shortcutEnabled = true;
    private bool _disposed;
    private bool _applying;
    private bool _quickQueued;
    private CancellationTokenSource? _quickCancellation;
    private QuickAiResultControl? _quickView;
    internal bool IsOpen => _quickQueued || _menus.Any(menu => !menu.IsDisposed && menu.Visible)
        || _nativeMenu is { IsDisposed: false, Visible: true };

    /// <summary>Connects the selection shortcut and extends native menus while retaining their existing items.</summary>
    internal AiSelectionContextMenu(TextControl editor, Func<bool> canRun,
        Func<QuickAiRunner?> captureRequest, Func<string> language, Func<string> tone)
    {
        _editor = editor; _canRun = canRun; _captureRequest = captureRequest; _language = language; _tone = tone;
        editor.Controls.Add(_shortcut);
        _tooltip.SetToolTip(_shortcut, "AI actions for selected text");
        _shortcut.Click += (_, _) => OpenPopup();
        _refresh.Tick += (_, _) => { _refresh.Stop(); UpdateShortcut(); };
        editor.InputPositionChanged += SelectionChanged;
        editor.MouseUp += MouseReleased;
        editor.KeyUp += KeyReleased;
        editor.LostFocus += FocusLost;
        editor.GotFocus += SelectionChanged;
        editor.Changed += DocumentChanged;
        editor.HScroll += ViewChanged;
        editor.VScroll += ViewChanged;
        editor.Zoomed += ViewChanged;
        editor.SizeChanged += ViewChanged;
        editor.TextContextMenuOpening += NativeMenuOpening;
        // Also handle a selection that existed before a component was attached at runtime.
        RefreshShortcut();
    }

    /// <summary>Schedules a shortcut refresh after native selection/focus updates finish.</summary>
    internal void RefreshShortcut()
    {
        if (_disposed) return;
        _refresh.Stop(); _refresh.Start();
    }

    /// <summary>Cancels pending work and dismisses transient UI owned by this controller.</summary>
    internal void Dismiss()
    {
        _refresh.Stop();
        _shortcut.Visible = false;
        foreach (var menu in _menus.ToArray()) if (!menu.IsDisposed) menu.Close();
        if (_nativeMenu is { IsDisposed: false }) _nativeMenu.Close();
    }

    /// <summary>Defers shortcut positioning until the native selection has settled.</summary>
    private void SelectionChanged(object? sender, EventArgs e) => RefreshShortcut();
    /// <summary>Refreshes the shortcut after the native editor finishes a mouse selection.</summary>
    private void MouseReleased(object? sender, MouseEventArgs e) => RefreshShortcut();
    /// <summary>Refreshes shortcut visibility after keyboard selection changes.</summary>
    private void KeyReleased(object? sender, KeyEventArgs e) => RefreshShortcut();
    /// <summary>Reevaluates shortcut visibility after focus leaves the editor.</summary>
    private void FocusLost(object? sender, EventArgs e) => RefreshShortcut();
    /// <summary>Invalidates stale requests and reconciles visual suggestions with the edited document.</summary>
    private void DocumentChanged(object? sender, EventArgs e) { if (!_applying) Dismiss(); RefreshShortcut(); }
    /// <summary>Repositions or dismisses transient UI after scrolling, zooming or resizing.</summary>
    private void ViewChanged(object? sender, EventArgs e)
    {
        // Scrolling or resizing must not cancel a task on an unchanged captured selection.
        if (!_applying && _quickCancellation is null && _quickView is not { IsDisposed: false, Visible: true }) Dismiss();
        RefreshShortcut();
    }

    /// <summary>Positions the selection shortcut within the visible native editor viewport.</summary>
    private void UpdateShortcut()
    {
        if (_disposed || _editor.IsDisposed) return;
        if (Control.MouseButtons != MouseButtons.None) { RefreshShortcut(); return; }
        if (_menus.Any(menu => menu.Visible)) return;
        _shortcut.Visible = false;
        if (!_shortcutEnabled || !_canRun() || !_editor.ContainsFocus
            || _editor.Selection.Length == 0 || string.IsNullOrWhiteSpace(_editor.Selection.Text)) return;

        // TX converts document twips using the current scroll position, zoom and DPI.
        var caret = _editor.DocumentToClient(_editor.InputPosition.Location);
        var viewport = _editor.ClientRectangle;
        if (!viewport.Contains(caret)) return;
        int size = Scale(34), gap = Scale(5), edge = Scale(22);
        _shortcut.Size = new Size(size, size);
        int maxX = viewport.Right - size - edge, maxY = viewport.Bottom - size - edge;
        if (maxX < gap || maxY < gap) return;
        int y = caret.Y + _editor.InputPosition.CaretSize.Height + gap;
        if (y > maxY) y = caret.Y - size - gap;
        _shortcut.Location = new Point(Math.Clamp(caret.X + gap, gap, maxX), Math.Clamp(y, gap, maxY));
        _shortcut.Visible = true;
        _shortcut.BringToFront();
    }

    /// <summary>Converts logical pixel dimensions using the editor's current DPI.</summary>
    private int Scale(int value) => (int)Math.Round(value * _editor.DeviceDpi / 96f);

    /// <summary>Captures the source selection before opening its actions menu.</summary>
    private void OpenPopup()
    {
        var request = _captureRequest();
        if (request is null) { Dismiss(); return; }
        ReplacePopup();
        PopulateMenu(_popup!, request);
        _popup!.Show(_shortcut, new Point(0, _shortcut.Height + Scale(4)));
    }

    /// <summary>Replaces the prior owned popup and releases its hosted controls.</summary>
    private void ReplacePopup()
    {
        var previous = _popup;
        previous?.Close();
        _popup = new ContextMenuStrip();
        previous?.Dispose();
    }

    /// <summary>Defers popup replacement until the action menu has finished closing.</summary>
    private void RunQuickAction(QuickAiRunner run, QuickAiRequest request)
    {
        if (_disposed || _quickQueued || _quickCancellation is not null || !_canRun()) return;
        var anchor = _popup is { Visible: true } ? _popup.Location
            : _editor.PointToScreen(_editor.DocumentToClient(_editor.InputPosition.Location));
        _quickQueued = true;
        Dismiss();
        // Let WinForms finish closing the action menu before showing the result popup.
        _editor.BeginInvoke(() =>
        {
            _quickQueued = false;
            if (!_disposed) OpenQuickResult(run, request, anchor);
        });
    }

    /// <summary>Creates a self-contained preview, retaining source and preferences for retries.</summary>
    private void OpenQuickResult(QuickAiRunner run, QuickAiRequest request, Point anchor)
    {
        if (_disposed || _quickCancellation is not null || !_canRun()) return;
        ReplacePopup();
        var popup = _popup!;
        popup.ShowImageMargin = false;
        popup.ShowCheckMargin = false;
        popup.AutoClose = false;
        popup.Font = _menuFont;
        popup.BackColor = Color.White;
        popup.Padding = new Padding(Scale(8));
        popup.Renderer = new ToolStripProfessionalRenderer(new MenuColors());
        _menus.Add(popup);
        popup.Disposed += (_, _) => _menus.Remove(popup);
        popup.Closed += (_, _) => RefreshShortcut();
        var title = request.Action == AiAction.Custom ? "Ask AI"
            : AiActionCatalog.Items.Single(item => item.Action == request.Action).Title;
        if (request.Action == AiAction.Translate) title = $"Translation to {request.Language ?? _language()}";
        bool rewrite = request.Action is AiAction.Rephrase or AiAction.ImproveWriting;
        if (rewrite) request = request with { Tone = request.Tone ?? _tone() };
        var view = new QuickAiResultControl(title, Scale(330), Scale(rewrite ? 340 : 280), _editor.DeviceDpi / 96f, request.Tone);
        _quickView = view;
        view.CloseRequested += Dismiss;
        view.RetryRequested += () => GenerateQuickPreviewAsync(run, request with { Tone = view.Tone }, popup, view);
        view.ApplyRequested += apply =>
        {
            if (_disposed || !_canRun() || _quickCancellation is not null || !popup.Visible) return;
            _applying = true;
            try { apply(); view.MarkApplied(); }
            catch (Exception ex) { view.ShowError(ex.Message); }
            finally { _applying = false; }
        };
        popup.Items.Add(new ToolStripControlHost(view) { AutoSize = false, Size = view.Size, Margin = Padding.Empty, Padding = Padding.Empty });
        popup.Show(anchor);
        GenerateQuickPreviewAsync(run, request, popup, view);
    }

    /// <summary>Streams a cancelable popup request and wires Apply only to a complete validated result.</summary>
    private async void GenerateQuickPreviewAsync(QuickAiRunner run, QuickAiRequest request,
        ContextMenuStrip popup, QuickAiResultControl view)
    {
        if (_disposed || _quickCancellation is not null || !_canRun() || !popup.Visible || view.IsDisposed) return;
        bool rewrite = request.Action is AiAction.Rephrase or AiAction.ImproveWriting;
        popup.AutoClose = false;
        view.BeginWorking(request.Tone);
        using var cancellation = new CancellationTokenSource();
        _quickCancellation = cancellation;
        ToolStripDropDownClosedEventHandler closed = (_, _) => { cancellation.Cancel(); RefreshShortcut(); };
        popup.Closed += closed;
        try
        {
            var result = await run(request, view.QueuePreview, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (_disposed || view.IsDisposed) return;
            view.Complete(result.Text, result.Apply);
            popup.AutoClose = !rewrite;
        }
        catch (OperationCanceledException)
        {
            if (!_disposed && !view.IsDisposed) { view.ShowError("Request canceled."); popup.AutoClose = !rewrite; }
        }
        catch (Exception ex)
        {
            if (!_disposed && !view.IsDisposed) { view.ShowError(ex.Message); popup.AutoClose = !rewrite; }
        }
        finally
        {
            popup.Closed -= closed;
            view.StopPreview();
            _quickCancellation = null;
            RefreshShortcut();
        }
    }

    /// <summary>Adds AI actions only at supported native context-menu locations.</summary>
    private void NativeMenuOpening(object? sender, TextContextMenuEventArgs e) =>
        ExtendNativeMenu(e.TextContextMenu, e.ContextMenuLocation);

    /// <summary>Adds or refreshes only owned AI items while preserving native menu styling and commands.</summary>
    private void ExtendNativeMenu(ContextMenuStrip menu, ContextMenuLocation location)
    {
        RemoveNativeItems();
        if (_nativeMenu is not null) _nativeMenu.Closed -= NativeMenuClosed;
        _nativeMenu = menu;
        menu.Closed += NativeMenuClosed;
        if ((location & ContextMenuLocation.TextSelection) == 0 || !_canRun()) return;
        var request = _captureRequest();
        if (request is null) return;
        var separator = new ToolStripSeparator { Tag = this };
        var root = new ToolStripMenuItem("AI Assistant") { Tag = this };
        PopulateMenu(root.DropDown, request, menu);
        menu.Items.Add(separator);
        menu.Items.Add(root);
        _shortcut.Visible = false;
    }

    /// <summary>Refreshes the shortcut after the native menu releases focus.</summary>
    private void NativeMenuClosed(object? sender, ToolStripDropDownClosedEventArgs e) => RefreshShortcut();

    /// <summary>Removes previously injected AI items without modifying the host's other menu entries.</summary>
    private void RemoveNativeItems()
    {
        if (_nativeMenu is null || _nativeMenu.IsDisposed) return;
        foreach (var item in _nativeMenu.Items.Cast<ToolStripItem>().Where(item => ReferenceEquals(item.Tag, this)).ToArray())
        {
            if (item is ToolStripMenuItem root) _menus.Remove(root.DropDown);
            _nativeMenu.Items.Remove(item);
            item.Dispose();
        }
    }

    /// <summary>Builds shared action items and a custom prompt, using native menu metrics when available.</summary>
    private void PopulateMenu(ToolStripDropDown menu, QuickAiRunner request, ToolStripDropDown? nativeStyle = null)
    {
        if (nativeStyle is not null)
        {
            menu.Font = nativeStyle.Font;
            menu.Renderer = nativeStyle.Renderer;
            menu.BackColor = nativeStyle.BackColor;
            menu.ForeColor = nativeStyle.ForeColor;
        }
        _menus.Add(menu);
        menu.Closed += (_, _) => RefreshShortcut();
        menu.Disposed += (_, _) => _menus.Remove(menu);

        foreach (var (action, title, _) in AiActionCatalog.Items)
        {
            var label = action == AiAction.Translate ? "Translate…" : title;
            var item = new ToolStripMenuItem(label) { Tag = action };
            if (action == AiAction.Translate)
            {
                item.DropDown.Font = menu.Font;
                item.DropDown.Renderer = menu.Renderer;
                string[] languages = [_language(), "English", "German", "French", "Spanish", "Italian", "Dutch", "Japanese"];
                foreach (var language in languages.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct())
                {
                    var choice = new ToolStripMenuItem(language) { Tag = language };
                    choice.Click += (_, _) => RunQuickAction(request, new QuickAiRequest(action, Language: language));
                    item.DropDownItems.Add(choice);
                }
            }
            else item.Click += (_, _) => RunQuickAction(request, new QuickAiRequest(action));
            menu.Items.Add(item);
        }
        menu.Items.Add(new ToolStripSeparator());
        var prompt = new SelectionPrompt(Scale(246), Scale(88));
        prompt.Submitted += instructions => RunQuickAction(request, new QuickAiRequest(AiAction.Custom, instructions));
        menu.Items.Add(new ToolStripControlHost(prompt) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false, Size = prompt.Size });
        menu.Items.Add(new ToolStripSeparator());
        var toggle = new ToolStripMenuItem(_shortcutEnabled ? "Hide selection shortcut" : "Show selection shortcut");
        toggle.Click += (_, _) => { _shortcutEnabled = !_shortcutEnabled; Dismiss(); RefreshShortcut(); };
        menu.Items.Add(toggle);
    }

    /// <summary>Releases owned resources and unsubscribes events before delegating to the base lifetime.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            Dismiss();
            _editor.InputPositionChanged -= SelectionChanged;
            _editor.MouseUp -= MouseReleased;
            _editor.KeyUp -= KeyReleased;
            _editor.LostFocus -= FocusLost;
            _editor.GotFocus -= SelectionChanged;
            _editor.Changed -= DocumentChanged;
            _editor.HScroll -= ViewChanged;
            _editor.VScroll -= ViewChanged;
            _editor.Zoomed -= ViewChanged;
            _editor.SizeChanged -= ViewChanged;
            _editor.TextContextMenuOpening -= NativeMenuOpening;
            RemoveNativeItems();
            if (_nativeMenu is not null) _nativeMenu.Closed -= NativeMenuClosed;
            _refresh.Dispose(); _tooltip.Dispose(); _popup?.Dispose(); _shortcut.Dispose(); _menuFont.Dispose();
            _menus.Clear();
        }
        base.Dispose(disposing);
    }

    private sealed class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.White;
        public override Color ImageMarginGradientBegin => Color.White;
        public override Color ImageMarginGradientMiddle => Color.White;
        public override Color ImageMarginGradientEnd => Color.White;
        public override Color MenuItemSelected => Color.FromArgb(238, 241, 246);
        public override Color MenuItemBorder => Color.FromArgb(218, 223, 232);
        public override Color MenuBorder => Color.FromArgb(218, 223, 232);
    }

    private sealed class SelectionPrompt : UserControl
    {
        private readonly TextBox _prompt = new() { Multiline = true, Dock = DockStyle.Fill, PlaceholderText = "Ask AI…",
            AccessibleName = "Instruction for selected text", ScrollBars = ScrollBars.Vertical };
        private readonly Button _run = UiTheme.Button("Run");
        internal event Action<string>? Submitted;

        /// <summary>Creates the embedded custom-instruction field and Run button.</summary>
        internal SelectionPrompt(int width, int height)
        {
            Size = new Size(width, height);
            BackColor = Color.White;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
            _prompt.Margin = new Padding(2, 4, 2, 4);
            _run.Enabled = false;
            _run.Click += (_, _) => Submit();
            _prompt.TextChanged += (_, _) => _run.Enabled = !string.IsNullOrWhiteSpace(_prompt.Text);
            layout.Controls.Add(_prompt, 0, 0); layout.Controls.Add(_run, 0, 1);
            Controls.Add(layout);
        }

        /// <summary>Raises a custom request only when the prompt contains text.</summary>
        private void Submit()
        {
            if (!string.IsNullOrWhiteSpace(_prompt.Text)) Submitted?.Invoke(_prompt.Text.Trim());
        }
    }

    private sealed class SelectionShortcut : Button
    {
        /// <summary>Creates the small non-tab-stop sparkle button beside a selection.</summary>
        internal SelectionShortcut()
        {
            Visible = false; TabStop = false;
            AccessibleName = "AI actions for selected text";
            AccessibleRole = AccessibleRole.PushButton;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.Selectable, false);
        }

        /// <summary>Updates the circular hit-test region when DPI or shortcut dimensions change.</summary>
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            using var path = new GraphicsPath();
            path.AddEllipse(ClientRectangle);
            var previous = Region;
            Region = new Region(path);
            previous?.Dispose();
        }

        /// <summary>Paints the transient visual without modifying the native document.</summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.White);
            float s = Width / 34f;
            using var border = new Pen(Color.FromArgb(187, 180, 204), 1.2f * s);
            g.DrawEllipse(border, 1 * s, 1 * s, Width - 2 * s, Height - 2 * s);
            using var pen = new Pen(Color.FromArgb(112, 89, 156), 1.5f * s) { LineJoin = LineJoin.Round };
            DrawSparkle(g, pen, 19 * s, 18 * s, 7 * s);
            DrawSparkle(g, pen, 10 * s, 10 * s, 2.5f * s);
            DrawSparkle(g, pen, 10 * s, 25 * s, 2 * s);
        }

        /// <summary>Draws one sparkle outline in logical icon coordinates.</summary>
        private static void DrawSparkle(Graphics g, Pen pen, float x, float y, float r) =>
            g.DrawPolygon(pen, new PointF[] { new(x, y-r), new(x+r*.3f, y-r*.3f), new(x+r, y),
                new(x+r*.3f, y+r*.3f), new(x, y+r), new(x-r*.3f, y+r*.3f), new(x-r, y), new(x-r*.3f, y-r*.3f) });
    }
}
