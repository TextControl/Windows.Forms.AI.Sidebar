using System.ComponentModel;
using TXTextControl;
using TXTextControl.AISidebar.AI;

namespace TXTextControl.AISidebar.UI;

using Button = System.Windows.Forms.Button;

/// <summary>Transient grammar decorations and explicit, revision-checked corrections.</summary>
internal sealed partial class AiGrammarCheckControl : Component, IMessageFilter
{
    private readonly TextControl _editor;
    private readonly Func<bool> _canCheck;
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<AiGrammarIssue>>> _check;
    private readonly Func<object, object, bool> _samePart;
    private readonly Action<string> _insert;
    private readonly Func<bool> _canScanIdle;
    private readonly System.Windows.Forms.Timer _pause = new() { Interval = 1600 };
    private readonly System.Windows.Forms.Timer _idle = new() { Interval = 5000 };
    private readonly System.Windows.Forms.Timer _layout = new() { Interval = 100 };
    private readonly ToolTip _tip = new();
    private readonly List<GrammarMark> _marks = [];
    private readonly List<AiGrammarIssue> _issues = [];
    private CancellationTokenSource? _pending;
    private Snapshot? _snapshot;
    private ToolStripDropDown? _popup;
    private AiGrammarCorrectionControl? _view;
    private int _revision, _generation;
    private bool _disposed, _composing;
    private long _lastActivity, _idleRetryAt;
    internal bool IsOpen => _popup is { IsDisposed: false, Visible: true };

    /// <summary>Connects sentence checking, idle scanning, document changes and temporary underline controls.</summary>
    internal AiGrammarCheckControl(TextControl editor, Func<bool> canCheck,
        Func<string, CancellationToken, Task<IReadOnlyList<AiGrammarIssue>>> check,
        Action<string> insert, Func<object, object, bool> samePart, Func<bool> canScanIdle)
    {
        _editor = editor; _canCheck = canCheck; _check = check; _insert = insert; _samePart = samePart;
        _canScanIdle = canScanIdle;
        _pause.Tick += (_, _) => { _pause.Stop(); CheckSentenceAsync(); };
        _idle.Tick += (_, _) => { _idle.Stop(); CheckNextIdleSentenceAsync(); };
        _layout.Tick += (_, _) => { _layout.Stop(); RefreshMarks(); };
        editor.Changed += DocumentChanged;
        editor.ContentsReset += DocumentReset; editor.DocumentLoaded += DocumentReset;
        editor.InputPositionChanged += PositionChanged;
        editor.MouseUp += MouseReleased;
        editor.LostFocus += LostFocus;
        editor.GotFocus += EditorFocused;
        editor.HScroll += ViewChanged; editor.VScroll += ViewChanged;
        editor.Zoomed += ViewChanged; editor.SizeChanged += ViewChanged;
        Application.AddMessageFilter(this);
        // Attachment can happen after the editor already gained focus; do not wait for another focus event.
        Schedule();
    }

    /// <summary>Observes editor keyboard and IME messages before native processing to cancel or accept previews safely.</summary>
    public bool PreFilterMessage(ref Message message)
    {
        if (_disposed || !_editor.ContainsFocus) return false;
        if (message.Msg == 0x010d) { _composing = true; Suspend(); }
        else if (message.Msg == 0x010e) { _composing = false; Schedule(); }
        else if (message.Msg == 0x0100 && (Keys)(int)message.WParam == Keys.Escape)
        {
            if (IsOpen) { ClosePopup(); return true; }
            CancelPending();
        }
        else if (message.Msg is 0x0100 or 0x0102) { CancelPending(); Schedule(); }
        return false;
    }

    /// <summary>Restarts the active-sentence debounce and records activity for idle scanning.</summary>
    private void Schedule()
    {
        _pause.Stop();
        _lastActivity = Environment.TickCount64;
        if (!_disposed && !_composing && _canCheck() && _editor.ContainsFocus && !IsOpen) _pause.Start();
        ArmIdle();
    }

    /// <summary>Cancels pending work and dismisses transient UI owned by this controller.</summary>
    internal void Dismiss()
    {
        Suspend(); ClearMarks(); _checked.Clear(); _parts.Clear();
        _snapshot = null; _issues.Clear(); _idleRetryAt = 0;
    }

    /// <summary>Pauses checks and closes correction UI while retaining unchanged sentence findings.</summary>
    internal void Suspend() { CancelPending(); _layout.Stop(); ClosePopup(); }
    /// <summary>Synchronizes cached ranges and restores visible findings and eligible idle checks.</summary>
    internal void Resume() { SynchronizeParts(); QueueLayout(); ArmIdle(); }
    /// <summary>Invalidates request generations and cancels scheduled or active inference.</summary>
    private void CancelPending() { _generation++; _pause.Stop(); _idle.Stop(); _pending?.Cancel(); }
    /// <summary>Invalidates cached source state when a document is reset or loaded.</summary>
    private void DocumentReset(object? sender, EventArgs e) { _revision++; Dismiss(); Schedule(); }
    /// <summary>Invalidates stale requests and reconciles visual suggestions with the edited document.</summary>
    private void DocumentChanged(object? sender, EventArgs e)
    {
        _revision++; Suspend(); SynchronizeParts(); RefreshMarks(); QueueLayout(); Schedule();
    }
    /// <summary>Reevaluates pending suggestions when the caret moves.</summary>
    private void PositionChanged(object? sender, EventArgs e)
    {
        CancelPending(); ClosePopup();
        SynchronizeParts();
        QueueLayout(); Schedule();
    }
    /// <summary>Stops background requests when the editor loses focus.</summary>
    private void LostFocus(object? sender, EventArgs e) => CancelPending();
    /// <summary>Resynchronizes cached findings and schedules checks when editing resumes.</summary>
    private void EditorFocused(object? sender, EventArgs e) { SynchronizeParts(); QueueLayout(); Schedule(); }
    /// <summary>Repositions or dismisses transient UI after scrolling, zooming or resizing.</summary>
    private void ViewChanged(object? sender, EventArgs e) { ClosePopup(); QueueLayout(); }
    /// <summary>Debounces underline layout while preserving existing marks until redraw.</summary>
    private void QueueLayout() { _layout.Stop(); if (!_disposed) _layout.Start(); }

    /// <summary>Checks revision and native source identity before displaying or applying a suggestion.</summary>
    private bool IsCurrent(Snapshot source) => source.Revision == _revision
        && ReadPartText(source.TextPart, source.Start, source.Text.Length) == source.Text;

    /// <summary>Finds the bounded sentence around the native caret using shared sentence segmentation.</summary>
    private Snapshot? CaptureSentence()
    {
        if (!_editor.ContainsFocus || !_canCheck() || _composing || _editor.Selection.Length != 0
            || _editor.EditMode != EditMode.Edit) return null;
        var state = GetState(_editor.TextParts.GetItem());
        int position = _editor.Selection.Start;
        (int Start, int Length)? selected = null;
        foreach (var range in EnumerateSentences(state.Text, 0))
        {
            if (position < range.Start) { selected ??= range; break; }
            selected = range;
            if (position <= range.Start + range.Length) break;
        }
        if (selected is not { Length: >= 8 and <= 1000 } sentence) return null;
        return new Snapshot(sentence.Start, state.Text.Substring(sentence.Start, sentence.Length), _revision, state.Part, 0);
    }

    /// <summary>Recognizes native text separators that always terminate a sentence.</summary>
    private static bool IsParagraphBreak(char value) => value is '\r' or '\n' or '\u2029' or '\f' or '\0' or '\t';
    /// <summary>Recognizes supported terminal punctuation.</summary>
    private static bool IsSentenceEnd(char value) => value is '.' or '!' or '?';

    /// <summary>Prioritizes the sentence near the caret and skips an unchanged completed check.</summary>
    private async void CheckSentenceAsync()
    {
        CancelPending();
        SynchronizeParts();
        var source = CaptureSentence();
        if (source is not null && !AlreadyChecked(source)) await CheckSourceAsync(source);
        ArmIdle();
    }

    /// <summary>Converts native character bounds into underline rectangles split across wrapped lines.</summary>
    private IReadOnlyList<Rectangle> GetSegments(Snapshot source, AiGrammarIssue issue)
    {
        var lines = new List<Rectangle>();
        var chars = ((IFormattedText)source.TextPart).TextChars;
        for (int index = source.Start + issue.Start + 1; index <= source.Start + issue.Start + issue.Length; index++)
        {
            var rectangle = _editor.DocumentToClient(chars[index].Bounds);
            if (rectangle.Width <= 0 || rectangle.Height <= 0) continue;
            int line = lines.FindIndex(other => Math.Abs(other.Bottom - rectangle.Bottom) <= 3);
            if (line < 0) lines.Add(rectangle); else lines[line] = Rectangle.Union(lines[line], rectangle);
        }
        return lines;
    }

    /// <summary>Rebuilds visible blue underline controls for current cached findings.</summary>
    private void RefreshMarks()
    {
        ClearMarks();
        if (!_canCheck() || _composing) return;
        float scale = _editor.DeviceDpi / 96f;
        foreach (var entry in _checked)
        {
            var source = entry.Source;
            if (source.Revision != _revision) continue;
            foreach (var issue in entry.Issues)
            {
                if (_samePart(source.TextPart, _editor.TextParts.GetItem()) && _editor.Selection.Length > 0
                    && _editor.Selection.Start < source.Start + issue.Start + issue.Length
                    && source.Start + issue.Start < _editor.Selection.Start + _editor.Selection.Length) continue;
                if (!IsVisible(source, issue)) continue;
                foreach (var segment in GetSegments(source, issue))
                {
                    var bounds = Rectangle.Intersect(_editor.ClientRectangle,
                        new Rectangle(segment.Left, segment.Bottom - 1, segment.Width, (int)Math.Ceiling(5 * scale)));
                    if (bounds.Width < 2 || bounds.Height < 2) continue;
                    var mark = new GrammarMark(scale) { Bounds = bounds };
                    mark.Click += (_, _) => OpenFinding(source, issue);
                    _tip.SetToolTip(mark, "Grammar suggestion · Click to review");
                    _editor.Controls.Add(mark); mark.BringToFront(); _marks.Add(mark);
                }
            }
        }
    }

    /// <summary>Disposes all temporary underline controls without touching document formatting.</summary>
    private void ClearMarks()
    {
        foreach (var mark in _marks) { _tip.SetToolTip(mark, null); mark.Dispose(); }
        _marks.Clear();
    }

    /// <summary>Opens a correction only for a click on a marked phrase, preserving drag selections.</summary>
    private void MouseReleased(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _editor.Selection.Length != 0) return;
        foreach (var entry in _checked)
        foreach (var issue in entry.Issues)
        {
            if (GetSegments(entry.Source, issue).Any(rectangle => rectangle.Contains(e.Location)))
            { OpenFinding(entry.Source, issue); return; }
        }
    }

    /// <summary>Opens a correction associated with the most recently checked sentence.</summary>
    private void OpenIssue(AiGrammarIssue issue)
    {
        var source = _snapshot;
        if (source is not null) OpenFinding(source, issue);
    }

    /// <summary>Builds a correction popup retaining the exact sentence and issue that it reviews.</summary>
    private void OpenFinding(Snapshot source, AiGrammarIssue issue)
    {
        if (!IsCurrent(source) || !HasIssue(source, issue) || !_canCheck()) return;
        var segments = GetSegments(source, issue);
        if (segments.Count == 0) return;
        CancelPending(); ClosePopup();
        float scale = _editor.DeviceDpi / 96f;
        _view = new AiGrammarCorrectionControl(issue, scale);
        _popup = new ToolStripDropDown { Padding = new Padding(5), AutoClose = true, BackColor = Color.White };
        _popup.Items.Add(new ToolStripControlHost(_view) { Padding = Padding.Empty, Margin = Padding.Empty, AutoSize = false, Size = _view.Size });
        _view.ApplyRequested += () => ApplyIssue(source, issue);
        _view.IgnoreRequested += () => IgnoreIssue(source, issue);
        _popup.Show(_editor, new Point(segments[0].Left, segments[0].Bottom + (int)(8 * scale)));
    }

    /// <summary>Revalidates the phrase and applies one undoable replacement to the correct native text part.</summary>
    private void ApplyIssue(Snapshot source, AiGrammarIssue issue)
    {
        if (!IsCurrent(source) || !HasIssue(source, issue) || !_canCheck() || _editor.EditMode != EditMode.Edit)
        { ClosePopup(); return; }
        // Compare native characters before selecting, preserving the caret if the source is stale.
        if (ReadPartText(source.TextPart, source.Start + issue.Start, issue.Length) != issue.Original) { ClosePopup(); return; }
        ClosePopup();
        if (!_samePart(source.TextPart, _editor.TextParts.GetItem()) && !_editor.TextParts.Activate(source.TextPart)) return;
        _editor.Select(source.Start + issue.Start, issue.Length);
        try { _insert(issue.Replacement); }
        catch (Exception) { Dismiss(); }
        Schedule();
    }

    /// <summary>Removes a finding from its unchanged sentence cache for the current session.</summary>
    private void IgnoreIssue(Snapshot source, AiGrammarIssue issue)
    {
        if (!IsCurrent(source)) { Dismiss(); return; }
        _checked.FirstOrDefault(entry => entry.Source.Id == source.Id)?.Issues.Remove(issue);
        SyncCurrent(); ClosePopup(); RefreshMarks(); _editor.Focus(); Schedule();
    }

    /// <summary>Closes and disposes the correction popup and its hosted view.</summary>
    private void ClosePopup()
    {
        var popup = _popup;
        _popup = null; _view = null;
        popup?.Close(); popup?.Dispose();
    }

    /// <summary>Releases owned resources and unsubscribes events before delegating to the base lifetime.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true; Dismiss(); Application.RemoveMessageFilter(this);
            _editor.Changed -= DocumentChanged; _editor.InputPositionChanged -= PositionChanged;
            _editor.ContentsReset -= DocumentReset; _editor.DocumentLoaded -= DocumentReset;
            _editor.MouseUp -= MouseReleased; _editor.LostFocus -= LostFocus;
            _editor.GotFocus -= EditorFocused;
            _editor.HScroll -= ViewChanged; _editor.VScroll -= ViewChanged;
            _editor.Zoomed -= ViewChanged; _editor.SizeChanged -= ViewChanged;
            _pause.Dispose(); _idle.Dispose(); _layout.Dispose(); _tip.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed record Snapshot(int Start, string Text, int Revision, object TextPart, long Id);

    private sealed class GrammarMark : Control
    {
        private readonly float _scale;
        /// <summary>Creates a nonselectable underline adorner scaled for the editor DPI.</summary>
        internal GrammarMark(float scale)
        {
            _scale = scale; BackColor = Color.White; Cursor = Cursors.Hand; TabStop = false;
            AccessibleName = "Grammar suggestion. Click to review.";
            SetStyle(ControlStyles.Selectable, false);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        /// <summary>Paints the transient visual without modifying the native document.</summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            using var pen = new Pen(Color.FromArgb(45, 110, 210), Math.Max(1, _scale));
            float step = 2 * _scale;
            for (float x = 0; x < Width; x += 2 * step)
            {
                e.Graphics.DrawLine(pen, x, 2 * _scale, x + step, 0);
                e.Graphics.DrawLine(pen, x + step, 0, x + 2 * step, 2 * _scale);
            }
        }
    }
}
