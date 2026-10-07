using System.ComponentModel;
using System.Runtime.CompilerServices;
using TXTextControl;
using TXTextControl.Windows.Forms;
using TXTextControl.AISidebar.AI;

namespace TXTextControl;

/// <summary>
/// Adds the AI sidebar, selection actions, typing continuations and grammar checks
/// to an existing <see cref="TXTextControl.TextControl"/>. Drop this component onto a form and set
/// <see cref="TextControl"/> in the Properties window; no event wiring or form subclass is required.
/// </summary>
/// <remarks>
/// Use one assistant per editor and access it on the editor's UI thread. The assistant owns its UI,
/// subscriptions and inference runtime; it never owns or disposes the host editor.
/// Runtime UI is deliberately not constructed in the designer. Connection details stay out of
/// designer-generated code; use the Settings dialog or assign <see cref="Settings"/> at runtime.
/// </remarks>
[ToolboxItem(true)]
[ToolboxBitmap(typeof(TextControl))]
[DefaultProperty(nameof(TextControl))]
[DefaultEvent(nameof(SettingsChanged))]
[Description("Adds a complete AI writing assistant to a TX Text Control editor.")]
public sealed class AiAssistant : Component, ISupportInitialize, IAsyncDisposable
{
    // Designer-serialized configuration is separate from runtime state and credentials.
    private TextControl? _textControl;
    private Control? _sidebarHost;
    private AiEditorSession? _session;
    private Form? _form;
    private AiConnectionSettings _settings = new();
    private bool _settingsAssigned;
    private bool _initializing;
    private bool _attachQueued;
    private int _attachmentVersion;
    private bool _disposed;
    private string _settingsFilePath = "";
    private readonly List<Task> _cleanup = [];
    private bool _enableSelectionActions = true;
    private int _sidebarWidth = 410;
    // Weak keys prevent the registry from keeping a disposed editor alive.
    private static readonly ConditionalWeakTable<TextControl, AiAssistant> Attachments = new();
    private static readonly ConditionalWeakTable<Form, HostLifetime> Hosts = new();

    /// <summary>Creates a designer-safe assistant. Set <see cref="TextControl"/> to attach it.</summary>
    public AiAssistant() { }

    /// <summary>Creates an assistant owned and disposed by the form's component container.</summary>
    /// <param name="container">The form's standard components container.</param>
    public AiAssistant(IContainer container) : this()
    {
        ArgumentNullException.ThrowIfNull(container);
        container.Add(this);
    }

    /// <summary>Gets or sets the editor to enhance. This is the only required designer property.</summary>
    [Category("AI Assistant"), Description("The TX Text Control editor to enhance."), DefaultValue(null)]
    public TextControl? TextControl
    {
        get => _textControl;
        set
        {
            ThrowIfDisposed();
            if (ReferenceEquals(value, _textControl)) return;
            Detach();
            if (_textControl is not null)
            {
                _textControl.HandleCreated -= EditorHandleCreated;
                _textControl.ParentChanged -= EditorReady;
                _textControl.Disposed -= EditorDisposed;
            }
            _textControl = value;
            if (value is not null)
            {
                value.HandleCreated += EditorHandleCreated;
                value.ParentChanged += EditorReady;
                value.Disposed += EditorDisposed;
            }
            AttachIfReady();
        }
    }

    /// <summary>Overrides the sidebar container. Null docks beside the editor in its parent container.</summary>
    [Category("Layout"), Description("Optional sidebar container; defaults to the editor's parent."), DefaultValue(null)]
    public Control? SidebarHost
    {
        get => _sidebarHost;
        set
        {
            ThrowIfDisposed();
            if (ReferenceEquals(_sidebarHost, value)) return;
            if (value is TextControl) throw new ArgumentException("Use the editor's parent container as the sidebar host.", nameof(value));
            _sidebarHost = value;
            Detach(); AttachIfReady();
        }
    }

    /// <summary>Gets or sets whether selection shortcuts and the native AI context submenu are enabled.</summary>
    [Category("AI Assistant"), Description("Enables AI actions on selected text."), DefaultValue(true)]
    public bool EnableSelectionActions
    {
        get => _enableSelectionActions;
        set { ThrowIfDisposed(); _enableSelectionActions = value; _session?.RefreshSelectionActions(); }
    }

    /// <summary>Gets or sets whether the sidebar is initially visible when an editor is attached.</summary>
    [Category("Layout"), Description("Shows the sidebar when the component first attaches."), DefaultValue(true)]
    public bool ShowSidebarOnStartup { get; set; } = true;

    /// <summary>Gets or sets the initial sidebar width in device-independent pixels (96 DPI).</summary>
    [Category("Layout"), Description("Initial sidebar width in 96-DPI pixels."), DefaultValue(410)]
    public int SidebarWidth
    {
        get => _sidebarWidth;
        set
        {
            if (value < 350) throw new ArgumentOutOfRangeException(nameof(value), "The sidebar needs at least 350 logical pixels.");
            _sidebarWidth = value;
        }
    }

    /// <summary>Gets or sets the caption used when creating the sidebar.</summary>
    [Category("Appearance"), Description("Caption for the sidebar."), DefaultValue("AI Assistant")]
    public string SidebarTitle { get; set; } = "AI Assistant";

    /// <summary>
    /// Gets or sets the settings file. Empty uses LocalApplicationData/application-name/ai-connection.json.
    /// Set before attaching. API keys are encrypted for the current Windows user when remembered.
    /// </summary>
    [Category("AI Assistant"), Description("Settings file path; empty uses an application-specific local folder."), DefaultValue("")]
    public string SettingsFilePath
    {
        get => _settingsFilePath;
        set
        {
            ThrowIfDisposed();
            if (_session is not null) throw new InvalidOperationException("Set SettingsFilePath before attaching the editor.");
            _settingsFilePath = value ?? "";
        }
    }

    /// <summary>Gets or sets whether saved settings are loaded when no runtime settings have been assigned.</summary>
    [Category("AI Assistant"), Description("Loads saved connection settings when the editor is attached."), DefaultValue(true)]
    public bool LoadSavedSettings { get; set; } = true;

    /// <summary>Gets or sets runtime connection and background-feature settings; never serialized by the designer.</summary>
    /// <remarks>Assignment cancels active work but does not save to disk. Call <see cref="SaveSettings"/> to persist.</remarks>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AiConnectionSettings Settings
    {
        get => _session?.Settings ?? _settings;
        set
        {
            ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(value);
            _settingsAssigned = true;
            _settings = value;
            _session?.SetSettings(value);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Gets the owned native sidebar after runtime attachment, or null before initialization.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Sidebar? Sidebar => _session?.Sidebar;

    /// <summary>Gets whether an editor is currently attached and the runtime UI is ready.</summary>
    [Browsable(false)]
    public bool IsAttached => _session is not null;

    /// <summary>Gets whether an explicit AI operation or the Settings dialog is active.</summary>
    [Browsable(false)]
    public bool IsBusy => _session?.IsBusy == true;

    /// <summary>Occurs after runtime settings are assigned or saved in the Settings dialog.</summary>
    public event EventHandler? SettingsChanged;

    /// <summary>Occurs when loading settings or releasing an inference runtime fails.</summary>
    /// <remarks>The exception excludes document text. Attach a handler to integrate with host logging.</remarks>
    public event EventHandler<AiAssistantErrorEventArgs>? Error;

    /// <summary>Shows the sidebar; call after the editor has loaded.</summary>
    public void ShowSidebar() => RequireSession().ShowAssistant();

    /// <summary>Hides the sidebar; selection actions and enabled background checks remain available.</summary>
    public void HideSidebar() { if (_session is not null) _session.Sidebar.IsShown = false; }

    /// <summary>Opens the built-in hosted/local-model connection dialog owned by the editor's form.</summary>
    public void ShowSettings() => RequireSession().SetupConnection();

    /// <summary>Starts an action using the sidebar's scope, tone, language and instructions.</summary>
    /// <param name="action">The writing action to run. Results require explicit user application.</param>
    public void RunAction(AiAction action) => RequireSession().StartAi(action);

    /// <summary>Cancels current requests and removes pending typing previews.</summary>
    public void Cancel() => _session?.Cancel();

    /// <summary>Validates and persists current settings, honoring the RememberKey preference.</summary>
    public void SaveSettings() => CreateStore().Save(Settings);

    /// <summary>Defers runtime attachment while the Windows Forms designer assigns related properties.</summary>
    public void BeginInit() => _initializing = true;

    /// <summary>Completes property initialization and attaches when the editor has a handle and parent.</summary>
    public void EndInit() { _initializing = false; AttachIfReady(); }

    /// <summary>Detects design-time construction before the component receives its designer Site.</summary>
    private bool IsDesignTime => DesignMode || LicenseManager.UsageMode == LicenseUsageMode.Designtime
        || _textControl?.Site?.DesignMode == true;

    /// <summary>Attaches lazily so designer serialization order and native handle creation are both safe.</summary>
    private void AttachIfReady(bool layoutReady = false, bool defer = false)
    {
        if (_disposed || _initializing || IsDesignTime || _session is not null
            || _textControl is not { IsHandleCreated: true, IsDisposed: false, Parent: not null }) return;
        // HandleCreated fires before TX finishes native initialization, even when the form
        // already reports Visible. Connecting sidebars at that point can reset the page color.
        // Always defer HandleCreated; Visible and Created can both be true inside this event.
        if (!layoutReady && (defer || _attachQueued || _textControl.FindForm() is { Visible: false }))
        {
            if (!_attachQueued)
            {
                _attachQueued = true;
                int version = _attachmentVersion;
                _textControl.BeginInvoke(() =>
                {
                    if (version != _attachmentVersion) return;
                    _attachQueued = false;
                    AttachIfReady(layoutReady: true);
                });
            }
            return;
        }
        var host = _sidebarHost ?? _textControl.Parent;
        if (host.IsDisposed) return;
        var store = CreateStore();
        if (LoadSavedSettings && !_settingsAssigned)
        {
            try { _settings = store.Load(); }
            catch (Exception ex) { ReportError(ex); }
        }
        if (Attachments.TryGetValue(_textControl, out var existing) && !ReferenceEquals(existing, this))
            throw new InvalidOperationException("This TextControl already has an AiAssistant. Use one component per editor.");
        _session = new AiEditorSession(this, _textControl, host, store, _settings);
        Attachments.Add(_textControl, this);
        _form = _textControl.FindForm();
        if (_form is not null)
        {
            Hosts.GetValue(_form, form => new HostLifetime(form)).Add(this);
        }
    }

    /// <summary>Waits until TX has returned from native window creation before connecting companion controls.</summary>
    private void EditorHandleCreated(object? sender, EventArgs e) => AttachIfReady(defer: true);

    /// <summary>Handles parent assignment without duplicating attachment.</summary>
    private void EditorReady(object? sender, EventArgs e)
    {
        if (_session is not null && (_textControl?.FindForm() != _form
            || _session.Sidebar.Parent != (_sidebarHost ?? _textControl?.Parent))) Detach();
        AttachIfReady();
    }

    /// <summary>Disconnects decorators before the native editor is destroyed.</summary>
    private void EditorDisposed(object? sender, EventArgs e) => TextControl = null;

    /// <summary>Uses an application-specific default so independent consuming apps do not share credentials.</summary>
    private AiSettingsStore CreateStore()
    {
        return new AiSettingsStore(string.IsNullOrWhiteSpace(_settingsFilePath) ? null : _settingsFilePath);
    }

    /// <summary>Updates the facade after the Settings dialog has persisted a connection successfully.</summary>
    internal void AcceptSettings(AiConnectionSettings settings)
    {
        _settings = settings; _settingsAssigned = true;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Requires runtime readiness for commands that need a visible native editor.</summary>
    private AiEditorSession RequireSession()
    {
        ThrowIfDisposed(); AttachIfReady();
        return _session ?? throw new InvalidOperationException("Connect TextControl and wait until its form has loaded.");
    }

    /// <summary>Detaches only owned UI and remembers asynchronous runtime cleanup for DisposeAsync.</summary>
    private void Detach()
    {
        _attachmentVersion++;
        _attachQueued = false;
        if (_form is not null)
        {
            if (Hosts.TryGetValue(_form, out var lifetime)) lifetime.Remove(this);
            _form = null;
        }
        if (_session is null) return;
        if (_textControl is not null) Attachments.Remove(_textControl);
        _settings = _session.Settings;
        _session.Dispose();
        _cleanup.Add(ObserveCleanupAsync(_session.Cleanup));
        _session = null;
    }

    /// <summary>Observes fire-and-forget disposal failures when a form uses standard synchronous disposal.</summary>
    private async Task ObserveCleanupAsync(Task cleanup)
    {
        try { await cleanup; }
        catch (Exception ex) { ReportError(ex); }
    }

    /// <summary>Reports lifecycle failures without displaying model details in ordinary editor UI.</summary>
    private void ReportError(Exception error)
    {
        System.Diagnostics.Debug.WriteLine(error);
        Error?.Invoke(this, new AiAssistantErrorEventArgs(error));
    }

    /// <summary>Rejects new attachments or configuration after component disposal.</summary>
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    /// <summary>Detaches controls, timers, message filters and subscriptions without disposing host controls.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            TextControl = null;
            _disposed = true;
        }
        base.Dispose(disposing);
    }

    /// <summary>Disposes the component and awaits release of every inference runtime it has owned.</summary>
    public async ValueTask DisposeAsync()
    {
        Dispose();
        await Task.WhenAll(_cleanup);
    }

    /// <summary>
    /// Coordinates one deferred close per form, even when it contains several enhanced editors.
    /// Independent FormClosing handlers per component would repeatedly cancel one another's close.
    /// </summary>
    private sealed class HostLifetime
    {
        private readonly Form _host;
        private readonly HashSet<AiAssistant> _owners = [];
        private bool _closing;
        private bool _allowClose;

        /// <summary>Subscribes once after host initialization, preserving earlier save/cancel handlers.</summary>
        internal HostLifetime(Form host)
        {
            _host = host;
            host.FormClosing += Closing;
        }

        /// <summary>Includes an attached component in the form's shared shutdown operation.</summary>
        internal void Add(AiAssistant owner) => _owners.Add(owner);

        /// <summary>Unhooks the coordinator when the form no longer has attached AI components.</summary>
        internal void Remove(AiAssistant owner)
        {
            _owners.Remove(owner);
            if (_owners.Count != 0) return;
            _host.FormClosing -= Closing;
            Hosts.Remove(_host);
        }

        /// <summary>Unloads every owned runtime before requesting a single, cancelable second close.</summary>
        private async void Closing(object? sender, FormClosingEventArgs e)
        {
            if (e.Cancel || _allowClose || _owners.Count == 0) return;
            e.Cancel = true;
            if (_closing) return;
            _closing = true;
            var active = _owners.Where(owner => owner._session is not null)
                .Select(owner => (Owner: owner, Session: owner._session!)).ToArray();
            foreach (var entry in active) entry.Session.Cancel();
            try { await Task.WhenAll(active.Select(entry => entry.Session.PrepareForCloseAsync())); }
            catch (Exception ex) { foreach (var entry in active) entry.Owner.ReportError(ex); }
            if (_host.IsDisposed || _owners.Count == 0) return;
            // Posting avoids recursive FormClosing when cleanup completes synchronously.
            _host.BeginInvoke(() =>
            {
                if (_host.IsDisposed || _owners.Count == 0) return;
                _allowClose = true;
                _host.Close();
                if (!_host.IsDisposed)
                {
                    _allowClose = false; _closing = false;
                    foreach (var entry in active) entry.Session.ResumeAfterCanceledClose();
                }
            });
        }
    }
}

/// <summary>Provides a settings or lifecycle error to the consuming application's logging handler.</summary>
public sealed class AiAssistantErrorEventArgs : EventArgs
{
    /// <summary>Creates error event data.</summary>
    /// <param name="exception">The underlying settings or runtime exception.</param>
    public AiAssistantErrorEventArgs(Exception exception) => Exception = exception;

    /// <summary>Gets the underlying exception.</summary>
    public Exception Exception { get; }
}
