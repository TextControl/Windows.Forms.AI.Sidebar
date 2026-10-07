using System.ComponentModel;
using TXTextControl;
using TXTextControl.Windows.Forms;
using TXTextControl.AISidebar.AI;
using TXTextControl.AISidebar.UI;

namespace TXTextControl;

/// <summary>
/// Owns one runtime attachment. Keeping it separate from the toolbox component prevents native
/// editor access, settings reads, timers and inference from running in the Windows Forms designer.
/// </summary>
internal sealed class AiEditorSession : IDisposable
{
    private readonly AiAssistant _owner;
    private readonly TextControl _editor;
    private readonly Sidebar _aiSidebar = new();
    private readonly AiSidebarControl _assistant = new();
    private readonly AiAssistantService _ai = new();
    private readonly AiSettingsStore _settingsStore;
    private readonly Font _titleFont = new("Segoe UI", 10, FontStyle.Bold);
    private AiConnectionSettings _settings;
    private CancellationTokenSource? _request;
    private SourceSnapshot? _source;
    private int _revision;
    private bool _shutdownInProgress;
    private bool _settingsOpen;
    private readonly AiSelectionContextMenu _selectionMenu;
    private readonly AiTypingSuggestionControl _typingSuggestions;
    private readonly AiGrammarCheckControl _grammar;

    internal bool IsDisposed { get; private set; }
    /// <summary>Gets the native sidebar owned by this runtime attachment.</summary>
    internal Sidebar Sidebar => _aiSidebar;
    /// <summary>Gets the current settings used by explicit requests and background checks.</summary>
    internal AiConnectionSettings Settings => _settings;
    internal bool IsBusy => _request is not null || _settingsOpen;
    internal Task Cleanup { get; private set; } = Task.CompletedTask;

    /// <summary>Creates and connects owned UI only after the editor has a native handle and parent.</summary>
    internal AiEditorSession(AiAssistant owner, TextControl editor, Control host, AiSettingsStore store,
        AiConnectionSettings settings)
    {
        _owner = owner; _editor = editor; _settingsStore = store; _settings = settings;
        BuildSidebar(host);
        _assistant.SettingsRequested += SetupConnection;
        _assistant.ActionRequested += StartAi;
        _assistant.CancelRequested += Cancel;
        _assistant.ReplaceRequested += () => GuardUiAction(ReplaceSource);
        _assistant.InsertRequested += () => GuardUiAction(InsertResult);
        _editor.Changed += DocumentChanged;
        _editor.ContentsReset += DocumentReset;
        _editor.DocumentLoaded += DocumentReset;
        _selectionMenu = new AiSelectionContextMenu(_editor,
            () => CanRun && _owner.EnableSelectionActions, CaptureSelectionRequest,
            () => _assistant.Language, () => _assistant.Tone);
        _grammar = new AiGrammarCheckControl(_editor,
            () => CanRun && _settings.CheckGrammarWhileTyping && _settings.IsConfigured && !_selectionMenu.IsOpen,
            (sentence, token) => _ai.CheckGrammarAsync(_settings, sentence, token),
            text => InsertAsUndoAction(text, "Apply grammar correction"), SameTextPart,
            () => _settings.CheckGrammarWhenIdle);
        _typingSuggestions = new AiTypingSuggestionControl(_editor,
            () => CanRun && _settings.SuggestWhileTyping && _settings.IsConfigured && !_selectionMenu.IsOpen && !_grammar.IsOpen,
            (context, token) => _ai.SuggestAsync(_settings, context, token),
            text => InsertAsUndoAction(text, "Accept typing suggestion"), SameTextPart);
    }

    /// <summary>Serializes explicit requests and prevents background work during settings or shutdown.</summary>
    private bool CanRun => !IsDisposed && !_shutdownInProgress && !_settingsOpen && _request is null;

    /// <summary>Hosts the existing custom content inside TX's native pinnable, detachable sidebar.</summary>
    private void BuildSidebar(Control host)
    {
        float scale = _editor.DeviceDpi / 96f;
        _aiSidebar.Dock = DockStyle.Right;
        _aiSidebar.TextControl = _editor;
        _aiSidebar.ContentLayout = Sidebar.SidebarContentLayout.Custom;
        _aiSidebar.Content = _assistant;
        _aiSidebar.Text = _owner.SidebarTitle;
        _aiSidebar.ShowTitle = true;
        _aiSidebar.IsPinned = true;
        _aiSidebar.IsShown = _owner.ShowSidebarOnStartup;
        _aiSidebar.ShowPinButton = _aiSidebar.ShowCloseButton = _aiSidebar.IsSizable = true;
        _aiSidebar.TitleForeColor = UiTheme.Accent;
        _aiSidebar.TitleFont = _titleFont;
        host.Controls.Add(_aiSidebar);
        // WinForms docks in reverse z-order. Put the sidebar immediately after the Fill control,
        // leaving the host's outer ribbon/status/rulers ahead of it in the docking pass.
        var fill = host.Controls.Cast<Control>().FirstOrDefault(control => control.Dock == DockStyle.Fill);
        if (fill is not null) host.Controls.SetChildIndex(_aiSidebar, host.Controls.GetChildIndex(fill) + 1);
        // TX initializes its own size defaults when its handle is created. Apply our logical
        // dimensions afterward, including when the sidebar starts hidden.
        _ = _aiSidebar.Handle;
        _aiSidebar.MinimumSize = new Size((int)(350 * scale), 0);
        _aiSidebar.Width = (int)(_owner.SidebarWidth * scale);
        _aiSidebar.DialogSize = new Size((int)(430 * scale), (int)(860 * scale));
    }

    /// <summary>Invalidates captured replacements on any document edit.</summary>
    private void DocumentChanged(object? sender, EventArgs e)
    {
        _revision++;
        _assistant.InvalidateReplacement();
    }

    /// <summary>Loading/resetting a document cancels results belonging to the previous document.</summary>
    private void DocumentReset(object? sender, EventArgs e)
    {
        _revision++;
        Cancel();
        _source = null;
        _assistant.ClearResult();
    }

    /// <summary>Updates connection settings without writing them unless the host explicitly requests saving.</summary>
    internal void SetSettings(AiConnectionSettings settings)
    {
        Cancel();
        _typingSuggestions.Dismiss();
        _grammar.Dismiss();
        _settings = settings;
        _assistant.SetStatus("Ready.");
        _grammar.Resume();
    }

    /// <summary>Shows the native sidebar without changing the user's current document selection.</summary>
    internal void ShowAssistant() => _aiSidebar.IsShown = true;

    /// <summary>Hides disabled selection affordances immediately and refreshes the eligible shortcut.</summary>
    internal void RefreshSelectionActions()
    {
        _selectionMenu.Dismiss();
        _selectionMenu.RefreshShortcut();
    }

    /// <summary>Pauses automatic inference while the user configures or tests a connection.</summary>
    internal void SetupConnection()
    {
        if (!CanRun) return;
        _settingsOpen = true;
        _typingSuggestions.Dismiss();
        _grammar.Suspend();
        _selectionMenu.Dismiss();
        try
        {
            using var dialog = new AiConnectionDialog(_settings, _ai, _settingsStore);
            if (dialog.ShowDialog(_editor.FindForm()) != DialogResult.OK) return;
            _settings = dialog.Settings;
            _owner.AcceptSettings(_settings);
            _grammar.Dismiss();
            _assistant.SetStatus("Ready.");
        }
        finally { _settingsOpen = false; if (!IsDisposed) _grammar.Resume(); }
    }

    /// <summary>Cancels explicit and background work; partial responses cannot be applied.</summary>
    internal void Cancel()
    {
        _request?.Cancel();
        _typingSuggestions.Dismiss();
        _grammar.Suspend();
    }

    /// <summary>Stops requests and releases an owned local model before the host window closes.</summary>
    internal async Task PrepareForCloseAsync()
    {
        _shutdownInProgress = true;
        Cancel();
        _selectionMenu.Dismiss();
        await _ai.UnloadModelAsync();
    }

    /// <summary>Restores checks if the host vetoes the second close notification.</summary>
    internal void ResumeAfterCanceledClose()
    {
        if (IsDisposed) return;
        _shutdownInProgress = false;
        _grammar.Resume();
        _selectionMenu.RefreshShortcut();
    }

    /// <summary>Captures selection and preferences before a menu interaction can move the native caret.</summary>
    private QuickAiRunner? CaptureSelectionRequest()
    {
        if (!CanRun || !_owner.EnableSelectionActions || _editor.Selection.Length == 0
            || string.IsNullOrWhiteSpace(_editor.Selection.Text)) return null;
        var source = CaptureSourceSnapshot(true);
        var tone = _assistant.Tone;
        var language = _assistant.Language;
        var instructions = _assistant.Instructions;
        return (request, onUpdate, token) => RunQuickAiAsync(source, tone, language, instructions, request, onUpdate, token);
    }

    /// <summary>Runs a popup action and returns a guarded Apply callback without opening the sidebar.</summary>
    private async Task<QuickAiResult> RunQuickAiAsync(SourceSnapshot source, string tone, string language,
        string instructions, QuickAiRequest request, Action<string> onUpdate, CancellationToken token)
    {
        if (!CanRun) throw new InvalidOperationException("The AI assistant is busy or no longer attached.");
        _typingSuggestions.Dismiss();
        _grammar.Suspend();
        if (source.Revision != _revision) throw new InvalidOperationException("The document changed. Select the text again.");
        if (!_settings.IsConfigured) { SetupConnection(); if (!_settings.IsConfigured) throw new InvalidOperationException("Set up AI in Settings before running an action."); }
        token.ThrowIfCancellationRequested();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        _request = cancellation;
        try
        {
            var result = await _ai.GenerateAsync(_settings, request.Action, source.Text, request.Tone ?? tone,
                request.Language ?? language, request.Instructions ?? instructions, onUpdate, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (source.Revision != _revision) throw new InvalidOperationException("The document changed. Select the text again.");
            return new QuickAiResult(result, () => ApplyToSource(source, result, $"AI {request.Action}"));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { throw new InvalidOperationException(AiErrorMessage.For(ex, _settings.ApiKey)); }
        finally { _request = null; if (!IsDisposed) _grammar.Resume(); }
    }

    /// <summary>Streams a sidebar action and enables replacement only while its original source is unchanged.</summary>
    internal async void StartAi(AiAction action)
    {
        if (!CanRun) return;
        _typingSuggestions.Dismiss();
        _grammar.Suspend();
        _selectionMenu.Dismiss();
        ShowAssistant();
        if (!_settings.IsConfigured) { SetupConnection(); if (!_settings.IsConfigured) return; }
        bool completed = false;
        try
        {
            var source = CaptureSource();
            if (source.Revision != _revision)
                throw new InvalidOperationException("The document changed. Select the text again before running an AI action.");
            _source = source;
            _assistant.SetSourceKind(source.IsSelection);
            if (action == AiAction.Translate && string.IsNullOrWhiteSpace(_assistant.Language))
                throw new ArgumentException("Enter a target language for the translation.");
            _assistant.ClearResult();
            _request = new CancellationTokenSource();
            _assistant.SetBusy(true);
            _assistant.SetStatus("Working…");
            var result = await _ai.GenerateAsync(_settings, action, source.Text, _assistant.Tone,
                _assistant.Language, _assistant.Instructions, _assistant.QueuePreview, _request.Token);
            if (IsDisposed) return;
            _assistant.SetBusy(false);
            _assistant.CompleteResult(result);
            completed = true;
            if (source.Revision != _revision || _source != source)
            {
                _assistant.InvalidateReplacement();
                _assistant.SetStatus("Result ready. Source changed; copy or insert the result, or run again to replace.");
            }
            else _assistant.SetStatus("Result ready. Review it, then replace the source, insert, or copy.");
        }
        catch (OperationCanceledException)
        {
            if (!IsDisposed) { _assistant.ClearResult(); _assistant.SetStatus("Request canceled."); }
        }
        catch (Exception ex)
        {
            if (!IsDisposed) { _assistant.ClearResult(); _assistant.SetStatus(AiErrorMessage.For(ex, _settings.ApiKey)); }
        }
        finally
        {
            _request?.Dispose();
            _request = null;
            if (!IsDisposed && !completed) _assistant.SetBusy(false);
            if (!IsDisposed) _selectionMenu.RefreshShortcut();
            if (!IsDisposed) _grammar.Resume();
        }
    }

    /// <summary>Captures source according to the sidebar's scope.</summary>
    private SourceSnapshot CaptureSource() => CaptureSourceSnapshot(false);

    /// <summary>Records text, native range, text part and revision for subsequent stale-source validation.</summary>
    private SourceSnapshot CaptureSourceSnapshot(bool selectionOnly)
    {
        bool selection = (selectionOnly || _assistant.Scope != "Whole document") && _editor.Selection.Length > 0;
        if ((selectionOnly || _assistant.Scope == "Selection only") && !selection)
            throw new ArgumentException("Select text in the editor first, or choose Whole document.");
        var textPart = selection ? _editor.TextParts.GetItem() : _editor.TextParts.GetMainText();
        var source = selection ? _editor.Selection.Text : _editor.Text;
        var start = selection ? _editor.Selection.Start : 0;
        var length = selection ? _editor.Selection.Length : source.Length;
        if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("Select some text or add content to the document first.");
        return new SourceSnapshot(source, start, length, _revision, textPart, selection);
    }

    /// <summary>Applies a completed result to its captured range and disables repeat replacement.</summary>
    private void ReplaceSource()
    {
        if (_source is null || string.IsNullOrWhiteSpace(_assistant.ResultText)) return;
        ApplyToSource(_source, _assistant.ResultText, "Replace with AI result");
        _assistant.InvalidateReplacement();
        _assistant.SetStatus("Source replaced. Use Ctrl+Z to undo.");
    }

    /// <summary>Validates revision and exact source text before activating and replacing the native range.</summary>
    private void ApplyToSource(SourceSnapshot source, string result, string undoName)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (source.Revision != _revision)
            throw new InvalidOperationException("The source document changed. Run the AI action again before replacing it.");
        if (!SameTextPart(source.TextPart, _editor.TextParts.GetItem()) && !_editor.TextParts.Activate(source.TextPart))
            throw new InvalidOperationException("The source text area is no longer available. Run the AI action again.");
        _editor.Select(source.Start, source.Length);
        if (_editor.Selection.Text != source.Text)
            throw new InvalidOperationException("The source text no longer matches. Run the AI action again.");
        InsertAsUndoAction(result, undoName);
    }

    /// <summary>Compares native text-part identity without depending on wrapper object identity.</summary>
    private static bool SameTextPart(object first, object second) => (first, second) switch
    {
        (MainText, MainText) => true,
        (HeaderFooter a, HeaderFooter b) => a.Type == b.Type && a.Section == b.Section,
        (TextFrame a, TextFrame b) => a.ID == b.ID,
        _ => ReferenceEquals(first, second)
    };

    /// <summary>Inserts at the caret without deleting an existing selection.</summary>
    private void InsertResult()
    {
        if (string.IsNullOrWhiteSpace(_assistant.ResultText)) return;
        _editor.Selection.Length = 0;
        InsertAsUndoAction(_assistant.ResultText, "Insert AI result");
        _assistant.SetStatus("Result inserted at the cursor. Use Ctrl+Z to undo.");
    }

    /// <summary>Writes Selection.Text inside one native undo transaction and restores editor focus.</summary>
    private void InsertAsUndoAction(string text, string name)
    {
        _editor.BeginUndoAction(name);
        try { _editor.Selection.Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n"); }
        finally { _editor.EndUndoAction(); }
        _editor.Focus();
    }

    /// <summary>Reports expected UI errors using the editor's host window as the dialog owner.</summary>
    private void GuardUiAction(Action action)
    {
        try { action(); }
        catch (Exception ex) { MessageBox.Show(_editor.FindForm(), ex.Message, _owner.SidebarTitle, MessageBoxButtons.OK, MessageBoxIcon.Information); }
    }

    /// <summary>
    /// Disconnects UI synchronously. Service cleanup remains awaitable because blocking the UI
    /// thread could deadlock a canceled streaming request waiting to resume on that thread.
    /// </summary>
    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        _shutdownInProgress = true;
        _request?.Cancel();
        _editor.Changed -= DocumentChanged;
        _editor.ContentsReset -= DocumentReset;
        _editor.DocumentLoaded -= DocumentReset;
        _typingSuggestions.Dispose();
        _grammar.Dispose();
        _selectionMenu.Dispose();
        _aiSidebar.Dispose();
        _assistant.Dispose();
        _titleFont.Dispose();
        Cleanup = _ai.DisposeAsync().AsTask();
    }

    /// <summary>Captures the exact native range and revision required for an undoable, stale-safe replacement.</summary>
    private sealed record SourceSnapshot(string Text, int Start, int Length, int Revision, object TextPart, bool IsSelection);
}
