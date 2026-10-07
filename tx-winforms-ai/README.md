# AISidebar for TX Text Control

Add AI writing tools to a TX Text Control Windows Forms application with the reusable `TXTextControl.AiAssistant` component.

The component hosts its UI in a native `TXTextControl.Windows.Forms.Sidebar`. It provides writing actions, selection popups, typing suggestions, grammar checks and a connection settings dialog. Inference can use an OpenAI-compatible API or a local GGUF language model.

## Requirements

- Windows x64 and the .NET 10 SDK.
- Visual Studio with .NET 10 Windows Forms support for designer integration.
- TX Text Control Windows Forms SDK 34.0.5, with its package source and development license configured.
- An AI service connection or a compatible local GGUF language model to run AI features.

The projects reference `TXTextControl.AI` 0.1.0-beta.3 and `Microsoft.Extensions.AI` 10.9.0. Restore obtains these dependencies from your configured NuGet sources. TX Text Control licensing is configured separately.

## Build and run

Clone or download this repository, then open [AISidebar.slnx](AISidebar.slnx). Set `tx-winforms-ai` as the startup project.

From the directory containing this README:

```powershell
dotnet restore AISidebar.slnx
dotnet build AISidebar.slnx -c Release
dotnet run --project Samples/tx-winforms-ai/tx-winforms-ai.csproj -c Release
```

The sample contains the native editor, ribbon, rulers and status bar. It loads the included `test.rtf` document and connects the AI component through the form designer. Use the gear in the sidebar's upper-right corner to configure AI.

[Samples/tx-winforms-ai](Samples/tx-winforms-ai) contains the complete sample application and its designer-generated component connection.

## Add AISidebar to your application

AISidebar is distributed as source. Add [AISidebar/AISidebar.csproj](AISidebar/AISidebar.csproj) to your solution and add a project reference from your Windows Forms application.

1. Build the solution and open your form in the Windows Forms designer.
2. Drag **AiAssistant** from the toolbox onto the form.
3. Set its **TextControl** property to your editor.
4. Run the application and open the sidebar's **Settings** dialog.

If the toolbox does not show the component, enable **Automatically Populate Toolbox** in Visual Studio's Windows Forms Designer options, rebuild and reopen the designer.

The equivalent code, using the form's component container, is:

```csharp
using TXTextControl;

components ??= new System.ComponentModel.Container();
var assistant = new AiAssistant(components)
{
    TextControl = textControl1
};
```

The component creates the sidebar at runtime and the form's container owns its lifetime. No AI-specific ribbon or event wiring is needed.

Keep a direct `TXTextControl.TextControl.WinForms.SDK` reference in the host executable project: its build targets handle the executable's native licensing. The sample project demonstrates this setup.

See the [component guide](AISidebar/README.md) for properties, programmatic configuration and lifecycle details.

## Features

| Feature | Behavior |
|---|---|
| Writing actions | Summarize, rephrase, extract structured data, identify action items, translate, improve writing or provide custom instructions. |
| Selection tools | Use the floating selection button or the editor's AI Assistant context submenu. |
| Rewrite preview | Change tone, generate alternatives and choose Apply when ready. |
| Typing suggestions | Gray preview text appears after a pause. Tab accepts it; Escape dismisses it. |
| Grammar checks | Blue wavy underlines mark findings. Review corrections before applying or ignoring them. Unchanged findings remain as typing continues. |
| Idle checking | Optionally checks additional sentences after five seconds of inactivity. |
| Settings | Configure the provider, local model and background features using the sidebar's gear button. |

Text replacements are undoable. Generated text is plain text, so replacing a formatted selection may change its formatting. Review generated content before applying it.

## Configure AI

### OpenAI-compatible API

Choose the API provider in Settings and enter the API base URL, model identifier and API key. Include `/v1` if your provider requires it; do not append `/chat/completions`.

A local API server can use a loopback URL such as `http://127.0.0.1:1234/v1`. Use `local` as the key if the server does not require authentication. Remote endpoints require HTTPS.

### Local GGUF model

Choose **Local GGUF model**, then select an existing generation-capable `.gguf` file. Configure the hardware backend, GPU layers and context size to fit your model and machine.

You can supply `llama-server.exe` or enable the optional inference-runtime download. Model weights are supplied by the user. The model loads on demand and is reused until the connection changes, the model is unloaded or the application closes.

### Settings and document data

Hosted inference sends the text needed for each action to the configured endpoint. Enabled background typing and grammar features also send relevant document passages. Local GGUF inference runs on the machine.

The component stores settings under `%LOCALAPPDATA%/<application product name>/ai-connection.json` by default. The main sample uses `%LOCALAPPDATA%/TX AI Writer/connection.json`. Remembered API keys are encrypted for the current Windows user using DPAPI; otherwise they remain in memory for the session.

## Repository layout

| Path | Purpose |
|---|---|
| [AISidebar](AISidebar) | Reusable class library and commented source. Builds `AISidebar.dll`. |
| [Samples/tx-winforms-ai](Samples/tx-winforms-ai) | Sample application, document and project reference. |
| [Form1.Designer.cs](Samples/tx-winforms-ai/Form1.Designer.cs) | Sample's native editor layout and component connection. |

Build outputs, local development files, settings, model weights and generated packages are excluded by `.gitignore`.
