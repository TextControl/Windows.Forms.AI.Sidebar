# AISidebar component guide

`AISidebar.csproj` builds a Windows Forms class library with the public component `TXTextControl.AiAssistant`. Use a project reference to build it with your application. Supporting configuration types are in `TXTextControl.AISidebar.AI`; sidebar controls are in `TXTextControl.AISidebar.UI`.

## Project setup

Add this project to your solution and reference it from an x64 Windows Forms application targeting `net10.0-windows`.

For example, if your application and the AISidebar directory are siblings:

```xml
<ItemGroup>
  <ProjectReference Include="..\AISidebar\AISidebar.csproj" />
  <PackageReference Include="TXTextControl.TextControl.WinForms.SDK" Version="34.0.5" />
</ItemGroup>
```

The direct SDK reference is needed by the executable's native licensing build targets. Configure the licensed TX SDK package source before restoring.

Build, open your form designer, drag **AiAssistant** onto the form and set **TextControl** to the editor. The component appears in the nonvisual tray; its native TX sidebar is created when the application runs.

## Connect in code

Use the form's standard component container so the assistant is disposed with the form:

```csharp
using TXTextControl;

components ??= new System.ComponentModel.Container();
var assistant = new AiAssistant(components)
{
    TextControl = textControl1
};
```

The form's `Dispose(bool)` implementation must dispose `components`, as a designer-generated form normally does.

Set configuration properties before assigning `TextControl` if the editor is already running. `BeginInit()` and `EndInit()` can defer attachment while assigning several properties. The assistant waits for the editor's native initialization before creating its sidebar.

## Designer properties

| Property | Default | Purpose |
|---|---|---|
| `TextControl` | null | Editor to enhance. Use one assistant per editor. |
| `SidebarHost` | null | Optional container; defaults to the editor's parent. |
| `EnableSelectionActions` | true | Enables the floating selection button and AI context submenu. |
| `ShowSidebarOnStartup` | true | Initial sidebar visibility. |
| `SidebarWidth` | 410 | Initial width in logical 96-DPI pixels; minimum 350. |
| `SidebarTitle` | AI Assistant | Native sidebar caption. |
| `SettingsFilePath` | empty | Optional settings path; set before runtime attachment. |
| `LoadSavedSettings` | true | Loads saved settings unless `Settings` was assigned explicitly. |

Use a Form or Panel with ordinary edge docking for `SidebarHost`. For a TableLayoutPanel or FlowLayoutPanel, provide a dedicated Panel for the editor and sidebar.

The assistant manages its own sidebar and selection tools. The host owns the editor, ribbon, rulers and status bar.

## Configure a connection

Users can configure the connection with the gear in the sidebar's upper-right corner. A host can also assign immutable settings in code.

### Hosted or local API

```csharp
using TXTextControl.AISidebar.AI;

assistant.Settings = new AiConnectionSettings
{
    Provider = AiProvider.RemoteApi,
    Endpoint = "https://your-provider.example/v1",
    Model = "your-model-id",
    ApiKey = Environment.GetEnvironmentVariable("AI_API_KEY") ?? ""
};
```

Use the provider's base URL rather than its chat-completions operation URL. Local servers can use loopback HTTP and `local` as the key when authentication is not required.

### Local GGUF inference

```csharp
using TXTextControl.AISidebar.AI;

assistant.Settings = new AiConnectionSettings
{
    Provider = AiProvider.LocalGguf,
    LocalModelPath = @"C:\Models\my-model.gguf",
    LlamaServerExecutablePath = @"C:\Tools\llama.cpp\llama-server.exe",
    AllowRuntimeDownload = false,
    GpuLayers = -1
};
```

Use a generation-capable GGUF model. Hardware backend, context size and output limits can be configured in Settings. A GPU-layer count of `-1` selects automatic offload; `0` uses CPU. If no executable is supplied, the AI dependency uses its runtime cache. Runtime download requires explicit opt-in.

Each assistant owns its inference runtime and reuses the loaded model. The project does not include model weights.

### Background features and persistence

```csharp
assistant.Settings = assistant.Settings with
{
    SuggestWhileTyping = true,
    CheckGrammarWhileTyping = true,
    CheckGrammarWhenIdle = true
};

// Optional: persist validated settings.
assistant.SaveSettings();
```

Assigning `Settings` cancels current work and changes runtime configuration without saving it. The Settings dialog's Save connection command and `SaveSettings()` persist settings.

By default, settings are saved to `%LOCALAPPDATA%/<application product name>/ai-connection.json`. API keys are excluded from ordinary settings JSON and designer serialization. When `RememberKey` is enabled, the settings store encrypts the key using Windows DPAPI for the current user.

Hosted actions and enabled background features send relevant document text to the configured endpoint. Local GGUF requests are processed on the machine.

## Runtime commands

Call UI commands after the editor and assistant have initialized, on the editor's UI thread:

```csharp
using TXTextControl.AISidebar.AI;

assistant.ShowSidebar();
assistant.HideSidebar();
assistant.ShowSettings();
assistant.RunAction(AiAction.Summarize);
assistant.Cancel();
```

`RunAction` starts a request using the sidebar's scope, tone, language and instructions, then displays the result for review. It returns immediately.

| Member | Purpose |
|---|---|
| `IsAttached` | Indicates that runtime UI has been connected. |
| `IsBusy` | Indicates an explicit request or open Settings dialog. |
| `Sidebar` | Gives access to the owned native TX sidebar after initialization. |
| `SettingsChanged` | Fires when runtime settings are assigned or saved through the dialog. |
| `Error` | Reports settings-load and runtime-cleanup failures. Inference errors appear in the action UI. |

## Reviewing and applying text

Sidebar actions support summarizing, rephrasing, extracting data, finding action items, translation, writing improvements and custom instructions.

Selected text also has independent quick-action popups. Rephrase and Improve writing offer tone selection, retries and an explicit Apply command. Other quick actions display results in a read-only text box.

Typing suggestions use temporary gray overlays: Tab accepts and Escape dismisses. Grammar findings use blue underlines with Apply and Ignore controls. Idle checking can scan additional document sentences after five seconds without edits.

Replacements use `Selection.Text` inside a native undo action. The captured source and document position are checked before applying a result. Replacement text is plain text and may change formatting in the replaced range.

## Lifetime

The assistant subscribes to the editor and owns its AI controls and inference resources. It does not dispose the host editor.

- Assign `TextControl = null` to detach, or another editor to reconnect.
- Use the form's `components` container for normal ownership.
- Call `await assistant.DisposeAsync()` when programmatic removal must also await runtime cleanup.
- Avoid blocking the UI thread with `.Wait()` or `.Result`.

During form closing, the assistant may briefly defer the close while cancelling requests and unloading its runtime, then request closing again. Host close handlers should account for this second event, particularly when displaying save prompts. If the host cancels closing, the assistant resumes.

## Source structure

| File or directory | Responsibility |
|---|---|
| `AiAssistant.cs` | Public component, designer properties and lifetime management. |
| `AiEditorSession.cs` | Native sidebar attachment, action coordination and document edits. |
| `AI/` | Provider configuration, inference, prompts, validation and settings persistence. |
| `UI/` | Sidebar, settings dialog, selection actions, typing overlays and grammar feedback. |

Public members include XML documentation for IntelliSense. Source comments describe native editor interaction, request cancellation and ownership.

See the [repository README](../README.md) for requirements, the sample application and build commands.
