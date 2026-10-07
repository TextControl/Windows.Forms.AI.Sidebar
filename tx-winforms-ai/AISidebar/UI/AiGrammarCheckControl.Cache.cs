using System.Text;
using TXTextControl;
using TXTextControl.AISidebar.AI;

namespace TXTextControl.AISidebar.UI;

// Cache and idle scanning are separated from overlay rendering so range reconciliation is readable independently.
internal sealed partial class AiGrammarCheckControl
{
    private readonly List<PartState> _parts = [];
    private readonly List<CheckedSentence> _checked = [];
    private long _nextId;

    /// <summary>Last observed native text and the cursor for incremental idle scanning within one text area.</summary>
    private sealed class PartState(object part, string text)
    {
        internal object Part = part;
        internal string Text = text;
        internal int ScanPosition;
        /// <summary>True after every eligible sentence in this version of the text part has been considered.</summary>
        internal bool Complete;
    }
    /// <summary>A completed check, including empty or ignored findings, retained until its sentence changes.</summary>
    private sealed class CheckedSentence(Snapshot source, IEnumerable<AiGrammarIssue> issues)
    {
        internal Snapshot Source = source;
        internal List<AiGrammarIssue> Issues = issues.ToList();
    }

    /// <summary>Reads native character positions from a specific text part without moving editor focus.</summary>
    private string ReadPartText(object part, int start, int length)
    {
        try
        {
            var chars = ((IFormattedText)part).TextChars;
            if (start < 0 || length < 0 || start > chars.Count - length) return "";
            var text = new StringBuilder(length);
            for (int index = start + 1; index <= start + length; index++) text.Append(chars[index].Char);
            return text.ToString();
        }
        catch (Exception) { return ""; } // Deleted frames or text parts must never leave applicable findings.
    }

    /// <summary>Uses bulk main-text extraction when native offsets agree, otherwise reads native characters.</summary>
    private string ReadWholePart(object part)
    {
        int count = ((IFormattedText)part).TextChars.Count;
        if (part is MainText)
        {
            // Plain document bodies can be read in one native call. Paragraph separators
            // occupy one native position; fall back to native characters for complex content.
            string plain = _editor.Text.Replace("\r\n", "\r");
            if (plain.Length == count) return plain;
        }
        return ReadPartText(part, 0, count);
    }

    /// <summary>Finds or initializes the tracked text and idle-scan cursor for a text part.</summary>
    private PartState GetState(object part)
    {
        var state = _parts.FirstOrDefault(item => _samePart(item.Part, part));
        if (state is null) { state = new PartState(part, ReadWholePart(part)); _parts.Add(state); }
        return state;
    }

    /// <summary>Updates known parts and removes ranges belonging to deleted native text areas.</summary>
    private void SynchronizeParts()
    {
        if (_disposed) return;
        // Avoid collecting document text when grammar has never been enabled or used.
        if (_checked.Count == 0 && _parts.Count == 0 && !_canCheck()) return;
        GetState(_editor.TextParts.GetMainText());
        GetState(_editor.TextParts.GetItem());
        foreach (var state in _parts.ToArray())
        {
            try { UpdateState(state, ReadWholePart(state.Part)); }
            catch (Exception)
            {
                _checked.RemoveAll(entry => _samePart(entry.Source.TextPart, state.Part)); _parts.Remove(state);
            }
        }
        SyncCurrent();
    }

    /// <summary>Shifts unaffected findings after edits and invalidates only changed or resegmented sentences.</summary>
    private void UpdateState(PartState state, string text)
    {
        string previous = state.Text;
        int prefix = 0, suffix = 0;
        if (previous != text)
        {
            while (prefix < previous.Length && prefix < text.Length && previous[prefix] == text[prefix]) prefix++;
            while (suffix < previous.Length - prefix && suffix < text.Length - prefix
                && previous[previous.Length - suffix - 1] == text[text.Length - suffix - 1]) suffix++;
            state.ScanPosition = 0; state.Complete = false;
        }
        int oldEnd = previous.Length - suffix, delta = text.Length - previous.Length;
        var ranges = previous == text ? null : EnumerateSentences(text, 0).ToDictionary(range => range.Start, range => range.Length);
        foreach (var entry in _checked.Where(entry => _samePart(entry.Source.TextPart, state.Part)).ToArray())
        {
            var source = entry.Source;
            int start = source.Start;
            if (previous != text)
            {
                if (start >= oldEnd) start += delta;
                else if (start + source.Text.Length > prefix) { _checked.Remove(entry); continue; }
                // A separator edit can merge/split sentences even when their text survives.
                if (!ranges!.TryGetValue(start, out int length) || length != source.Text.Length
                    || !text.AsSpan(start, length).SequenceEqual(source.Text)) { _checked.Remove(entry); continue; }
            }
            entry.Source = source with { Start = start, Revision = _revision };
        }
        state.Text = text;
    }

    private static IEnumerable<(int Start, int Length)> EnumerateSentences(string text, int from)
    {
        int position = from;
        while (position < text.Length)
        {
            while (position < text.Length && (char.IsWhiteSpace(text[position]) || IsParagraphBreak(text[position]))) position++;
            int start = position;
            while (position < text.Length && !IsParagraphBreak(text[position]))
            {
                char value = text[position++];
                if (IsSentenceEnd(value))
                {
                    int terminator = position;
                    while (terminator < text.Length && text[terminator] is '"' or '\'' or '”' or '’' or '»' or ')' or ']' or '}') terminator++;
                    if (terminator == text.Length || char.IsWhiteSpace(text[terminator])) { position = terminator; break; }
                }
            }
            int end = position;
            while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
            if (end > start) yield return (start, end - start);
        }
    }

    /// <summary>Skips previously checked unchanged sentences, including sentences with no findings.</summary>
    private bool AlreadyChecked(Snapshot source) => _checked.Any(entry => entry.Source.Start == source.Start
        && entry.Source.Text == source.Text && _samePart(entry.Source.TextPart, source.TextPart));
    /// <summary>Checks that a popup still refers to a cached finding at the same revision.</summary>
    private bool HasIssue(Snapshot source, AiGrammarIssue issue) => _checked.Any(entry => entry.Source.Id == source.Id
        && entry.Source.Revision == source.Revision && entry.Issues.Contains(issue));

    /// <summary>Refreshes the most recently checked sentence view after cache changes.</summary>
    private void SyncCurrent()
    {
        var entry = _checked.FirstOrDefault(entry => entry.Source.Id == _snapshot?.Id);
        _snapshot = entry?.Source;
        _issues.Clear(); if (entry is not null) _issues.AddRange(entry.Issues);
    }

    /// <summary>Commits a validated response only when its request generation and source remain current.</summary>
    private async Task CheckSourceAsync(Snapshot source)
    {
        int generation = _generation;
        using var cancellation = new CancellationTokenSource();
        _pending = cancellation;
        try
        {
            var issues = await _check(source.Text, cancellation.Token);
            if (_disposed || cancellation.IsCancellationRequested || generation != _generation || !IsCurrent(source)
                || !_editor.ContainsFocus || !_canCheck() || _composing || AlreadyChecked(source)) return;
            source = source with { Id = ++_nextId };
            var entry = new CheckedSentence(source, issues);
            _idleRetryAt = 0;
            _checked.Add(entry); _snapshot = source; SyncCurrent(); RefreshMarks();
        }
        catch (OperationCanceledException) { }
        catch (Exception) { _idleRetryAt = Environment.TickCount64 + 30000; }
        finally { if (ReferenceEquals(_pending, cancellation)) _pending = null; }
    }

    /// <summary>Schedules idle scanning only when the editor is quiet and no explicit interaction has priority.</summary>
    private void ArmIdle(bool next = false)
    {
        _idle.Stop();
        if (_disposed || _composing || !_canCheck() || !_canScanIdle() || IsOpen || !_editor.ContainsFocus
            || _editor.Selection.Length != 0 || _editor.EditMode != EditMode.Edit || _pending is not null) return;
        long remaining = Math.Max(_lastActivity + 5000, _idleRetryAt) - Environment.TickCount64;
        _idle.Interval = (int)Math.Clamp(Math.Max(next ? 300 : 1, remaining), 1, int.MaxValue);
        _idle.Start();
    }

    /// <summary>Checks one uncached sentence, yields to the UI, then schedules the next eligible sentence.</summary>
    private async void CheckNextIdleSentenceAsync()
    {
        _idle.Stop();
        if (_disposed || _composing || !_canCheck() || !_canScanIdle() || IsOpen || !_editor.ContainsFocus
            || _editor.Selection.Length != 0 || _editor.EditMode != EditMode.Edit || _pending is not null) return;
        if (Environment.TickCount64 < Math.Max(_lastActivity + 5000, _idleRetryAt)) { ArmIdle(); return; }
        SynchronizeParts();
        foreach (object part in _editor.TextParts)
            if (part is IFormattedText) GetState(part);
        foreach (var state in _parts)
        {
            if (state.Complete) continue;
            foreach (var range in EnumerateSentences(state.Text, state.ScanPosition))
            {
                state.ScanPosition = range.Start + range.Length;
                if (range.Length is < 8 or > 1000) continue;
                var source = new Snapshot(range.Start, state.Text.Substring(range.Start, range.Length), _revision, state.Part, 0);
                if (AlreadyChecked(source)) continue;
                int generation = _generation;
                await CheckSourceAsync(source);
                if (_disposed || generation != _generation) { state.ScanPosition = 0; return; }
                if (!AlreadyChecked(source)) state.ScanPosition = range.Start;
                ArmIdle(true); return; // One bounded request at a time, yielding between sentences.
            }
            state.Complete = true;
        }
        // All unchanged text has been checked; activity restarts only when needed.
    }

    /// <summary>Culls offscreen findings before constructing their underline segments.</summary>
    private bool IsVisible(Snapshot source, AiGrammarIssue issue)
    {
        var chars = ((IFormattedText)source.TextPart).TextChars;
        var first = _editor.DocumentToClient(chars[source.Start + issue.Start + 1].Bounds);
        var last = _editor.DocumentToClient(chars[source.Start + issue.Start + issue.Length].Bounds);
        return Math.Max(first.Bottom, last.Bottom) >= 0 && Math.Min(first.Top, last.Top) < _editor.ClientSize.Height;
    }
}
