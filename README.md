# AI Sidebar for TX Text Control Windows Forms

Add AI writing assistance to a TX Text Control Windows Forms application with the reusable `TXTextControl.AiAssistant` component. The source includes a native TX sidebar, selection actions, typing suggestions, grammar checks and connection settings for an OpenAI-compatible API or a local GGUF model.

## Get started

Requirements: Windows x64, the .NET 10 SDK and a configured, licensed TX Text Control Windows Forms SDK 34.0.5 installation/package source.

Open [tx-winforms-ai.slnx](tx-winforms-ai.slnx) and select `tx-winforms-ai` as the startup project, or run:

```powershell
dotnet build tx-winforms-ai.slnx -c Release
dotnet run --project tx-winforms-ai/Samples/tx-winforms-ai/tx-winforms-ai.csproj -c Release
```

Configure an AI connection using the gear in the sidebar's upper-right corner.

## Use it in your application

Add [AISidebar.csproj](tx-winforms-ai/AISidebar/AISidebar.csproj) to your solution and reference it from your Windows Forms application. Build, drag **AiAssistant** from the toolbox onto your form, and set its **TextControl** property to your editor.

AISidebar is distributed as source and consumed through a project reference.

- [Setup, features and connection guide](tx-winforms-ai/README.md)
- [Component API and integration guide](tx-winforms-ai/AISidebar/README.md)
- [Sample application](tx-winforms-ai/Samples/tx-winforms-ai)
