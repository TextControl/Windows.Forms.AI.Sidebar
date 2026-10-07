using System.ComponentModel;
using System.Text;
using TXTextControl;

namespace TXTextControl.AISidebar.UI;

/// <summary>Non-document preview. Only an unmodified Tab inserts a valid suggestion.</summary>
internal sealed class AiTypingSuggestionControl : Component, IMessageFilter
{
    private readonly TextControl _editor;
    private readonly Func<bool> _canSuggest;
    private readonly Func<string, CancellationToken, Task<string>> _generate;
    private readonly Action<string> _insert;
    private readonly Func<object, object, bool> _samePart;
    private readonly System.Windows.Forms.Timer _pause = new() { Interval = 750 };
    private readonly SuggestionAdorner _preview = new();
    private readonly ToolTip _tip = new();
    private CancellationTokenSource? _pending;
    private Snapshot? _snapshot;
    private string _suggestion = "";
    private int _revision, _typedSequence, _generation;
    private bool _waitingAfterTyping, _composing, _disposed;

    /// <summary>Connects typing debounce, native caret tracking and a non-document gray preview.</summary>
    internal AiTypingSuggestionControl(TextControl editor, Func<bool> canSuggest,
        Func<string, CancellationToken, Task<string>> generate, Action<string> insert, Func<object, object, bool> samePart)
    {
        _editor = editor; _canSuggest = canSuggest; _generate = generate; _insert = insert; _samePart = samePart;
        editor.Controls.Add(_preview);
        _tip.SetToolTip(_preview, "Tab to accept · Esc to dismiss");
        _pause.Tick += (_, _) => { _pause.Stop(); _waitingAfterTyping = false; RequestSuggestionAsync(); };
        editor.Changed += DocumentChanged;
        editor.InputPositionChanged += PositionChanged;
        editor.LostFocus += LostFocus;
        editor.MouseDown += MouseDown;
        editor.HScroll += ViewChanged; editor.VScroll += ViewChanged;
        editor.Zoomed += ViewChanged; editor.SizeChanged += ViewChanged;
        Application.AddMessageFilter(this);
    }

    /// <summary>Observes editor keyboard and IME messages before native processing to cancel or accept previews safely.</summary>
    public bool PreFilterMessage(ref Message message)
    {
        if (_disposed || !_editor.ContainsFocus) return false;
        if (message.Msg == 0x010d) { _composing = true; Dismiss(); return false; } // WM_IME_STARTCOMPOSITION
        if (message.Msg == 0x010e) { _composing = false; return false; }
        if (message.Msg == 0x0100) // WM_KEYDOWN: before TX's native Tab processing
        {
            var key = (Keys)(int)message.WParam | Control.ModifierKeys;
            if (TryHandleKey(key)) return true;
            Dismiss();
        }
        else if (message.Msg == 0x0102 && !_composing && (int)message.WParam >= 32 // WM_CHAR
            && (Control.ModifierKeys & (Keys.Control | Keys.Alt)) == 0)
        {
            Dismiss();
            int sequence = _typedSequence, before = _revision;
            // The native editor must finish inserting the character and moving its caret first.
            _editor.BeginInvoke(() =>
            {
                if (_disposed || sequence != _typedSequence || before == _revision || !_canSuggest()) return;
                _waitingAfterTyping = true;
                _pause.Start();
            });
        }
        return false;
    }

    /// <summary>Accepts a current continuation with Tab or dismisses it with Escape.</summary>
    internal bool TryHandleKey(Keys key)
    {
        if (key == Keys.Escape && (_preview.Visible || _pending is not null || _pause.Enabled))
        { Dismiss(); return true; }
        if (key != Keys.Tab || !_preview.Visible || _composing) return false;
        var snapshot = _snapshot;
        var suggestion = _suggestion;
        if (snapshot is null || !IsCurrent(snapshot) || !_canSuggest() || _editor.EditMode != EditMode.Edit)
        { Dismiss(); return false; }
        Dismiss();
        try { _insert(suggestion); }
        catch (Exception) { return false; }
        return true;
    }

    /// <summary>Cancels pending work and dismisses transient UI owned by this controller.</summary>
    internal void Dismiss()
    {
        _typedSequence++;
        _waitingAfterTyping = false;
        InvalidateSuggestion();
    }

    /// <summary>Invalidates the captured continuation and cancels its pending generation.</summary>
    private void InvalidateSuggestion()
    {
        _generation++;
        _pause.Stop();
        _pending?.Cancel();
        _preview.Visible = false;
        _snapshot = null; _suggestion = "";
    }

    /// <summary>Invalidates stale requests and reconciles visual suggestions with the edited document.</summary>
    private void DocumentChanged(object? sender, EventArgs e) { _revision++; InvalidateSuggestion(); }
    /// <summary>Reevaluates pending suggestions when the caret moves.</summary>
    private void PositionChanged(object? sender, EventArgs e)
    {
        InvalidateSuggestion();
        if (_waitingAfterTyping) _pause.Start();
    }
    /// <summary>Stops background requests when the editor loses focus.</summary>
    private void LostFocus(object? sender, EventArgs e) => Dismiss();
    /// <summary>Dismisses the caret preview before a mouse gesture can change its target.</summary>
    private void MouseDown(object? sender, MouseEventArgs e) => Dismiss();
    /// <summary>Repositions or dismisses transient UI after scrolling, zooming or resizing.</summary>
    private void ViewChanged(object? sender, EventArgs e) => Dismiss();

    /// <summary>Captures a bounded prefix only at an editable, supported paragraph end.</summary>
    private Snapshot? CaptureContext()
    {
        if (!_editor.ContainsFocus || !_canSuggest() || _composing || _editor.Selection.Length != 0
            || _editor.EditMode != EditMode.Edit || _editor.InputFormat.RightToLeft == true
            || _editor.Tables.GetItem() is not null) return null;
        int position = _editor.Selection.Start;
        var chars = _editor.TextChars;
        if (position < 1 || position > chars.Count) return null;
        // Only preview in the free space at the end of a paragraph, never over existing text.
        if (position < chars.Count && chars[position + 1].Char is not ('\r' or '\n' or '\f' or '\u2029' or '\0')) return null;
        var context = new StringBuilder();
        for (int number = Math.Max(1, position - 1199); number <= position; number++)
        {
            char value = chars[number].Char;
            context.Append(value is '\r' or '\u2029' ? '\n' : value);
        }
        string prefix = context.ToString();
        if (prefix.Trim().Length < 12 || prefix[(prefix.LastIndexOf('\n') + 1)..].Trim().Length < 4) return null;
        return new Snapshot(position, _revision, _editor.TextParts.GetItem(), prefix);
    }

    /// <summary>Checks revision and native source identity before displaying or applying a suggestion.</summary>
    private bool IsCurrent(Snapshot snapshot) => snapshot.Revision == _revision
        && _editor.Selection.Length == 0 && snapshot.Position == _editor.Selection.Start
        && _samePart(snapshot.TextPart, _editor.TextParts.GetItem());

    /// <summary>Requests a continuation and discards stale, canceled or unusable responses.</summary>
    private async void RequestSuggestionAsync()
    {
        Dismiss();
        var snapshot = CaptureContext();
        if (snapshot is null) return;
        int generation = _generation;
        using var cancellation = new CancellationTokenSource();
        _pending = cancellation;
        try
        {
            string suggestion = await _generate(snapshot.Prefix, cancellation.Token);
            if (_disposed || cancellation.IsCancellationRequested || generation != _generation
                || !IsCurrent(snapshot) || !_editor.ContainsFocus || !_canSuggest() || string.IsNullOrWhiteSpace(suggestion)) return;
            _snapshot = snapshot; _suggestion = suggestion;
            ShowPreview(suggestion);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* Suggestions are optional; errors must not interrupt typing. */ }
        finally { if (ReferenceEquals(_pending, cancellation)) _pending = null; }
    }

    /// <summary>Positions gray text beside the native caret with matching font, zoom and available width.</summary>
    private void ShowPreview(string text)
    {
        var caret = _editor.DocumentToClient(_editor.InputPosition.Location);
        if (!_editor.ClientRectangle.Contains(caret)) return;
        float scale = _editor.DeviceDpi / 96f;
        int available = _editor.ClientSize.Width - caret.X - (int)(25 * scale);
        if (available < 90 * scale) return;
        float points = Math.Clamp((_editor.InputFormat.FontSize ?? 220) / 20f * _editor.ZoomFactor / 100f, 6, 48);
        var style = (_editor.InputFormat.Bold == true ? FontStyle.Bold : FontStyle.Regular)
            | (_editor.InputFormat.Italic == true ? FontStyle.Italic : FontStyle.Regular);
        _preview.SetText(text, new Font(_editor.InputFormat.FontFamily ?? "Segoe UI", points, style), scale);
        int width = Math.Min(available, _preview.MeasureWidth());
        _preview.Bounds = new Rectangle(caret.X + 2, caret.Y, width, Math.Max(_editor.InputPosition.CaretSize.Height, _preview.TextHeight));
        _preview.Visible = true;
        _preview.BringToFront();
    }

    /// <summary>Releases owned resources and unsubscribes events before delegating to the base lifetime.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true; Dismiss();
            Application.RemoveMessageFilter(this);
            _editor.Changed -= DocumentChanged; _editor.InputPositionChanged -= PositionChanged;
            _editor.LostFocus -= LostFocus; _editor.MouseDown -= MouseDown;
            _editor.HScroll -= ViewChanged; _editor.VScroll -= ViewChanged;
            _editor.Zoomed -= ViewChanged; _editor.SizeChanged -= ViewChanged;
            _pause.Dispose(); _tip.Dispose(); _preview.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed record Snapshot(int Position, int Revision, object TextPart, string Prefix);

    private sealed class SuggestionAdorner : Control
    {
        private Font? _ownedFont;
        private readonly Font _hintFont = new("Segoe UI", 8);
        private string _text = "";
        private float _scale = 1;
        /// <summary>Creates a nonfocusable child used only for preview painting.</summary>
        internal SuggestionAdorner()
        {
            Visible = false; TabStop = false; BackColor = Color.White;
            AccessibleName = "Typing suggestion. Tab to accept, Escape to dismiss.";
            SetStyle(ControlStyles.Selectable, false);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        /// <summary>Updates preview text and its owned font for the editor's current zoom.</summary>
        internal void SetText(string text, Font font, float scale)
        {
            var previous = _ownedFont;
            // Control.Font ignores an equal replacement; keep the drawing font ourselves so
            // disposing the previous instance cannot leave the control with a disposed font.
            _ownedFont = font; previous?.Dispose();
            _text = text; _scale = scale; Invalidate();
        }
        internal int TextHeight => (_ownedFont ?? Font).Height;
        /// <summary>Measures preview width with the same text-rendering flags used by painting.</summary>
        internal int MeasureWidth() => TextRenderer.MeasureText(_text, _ownedFont ?? Font, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width + (int)(40 * _scale);
        /// <summary>Paints the transient visual without modifying the native document.</summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            int badge = (int)(32 * _scale), gap = (int)(6 * _scale);
            TextRenderer.DrawText(e.Graphics, _text, _ownedFont ?? Font, new Rectangle(0, 0, Width - badge - gap, Height),
                Color.FromArgb(145, 145, 145), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            var hint = new Rectangle(Width - badge, Math.Max(1, (Height - _hintFont.Height - 4) / 2), badge - 1, _hintFont.Height + 3);
            e.Graphics.DrawRectangle(SystemPens.ControlLight, hint);
            TextRenderer.DrawText(e.Graphics, "Tab", _hintFont, hint, SystemColors.GrayText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        /// <summary>Releases owned resources and unsubscribes events before delegating to the base lifetime.</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing) { _ownedFont?.Dispose(); _hintFont.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
