using TXTextControl.AISidebar.AI;

namespace TXTextControl.AISidebar.UI;

/// <summary>Single source for action labels and tone choices shared across sidebar and popups.</summary>
internal static class AiActionCatalog
{
    internal static readonly string[] Tones = ["Professional", "Friendly", "Concise", "Persuasive", "Academic"];
    internal static readonly (AiAction Action, string Title, string Symbol)[] Items =
    [
        (AiAction.Summarize, "Summarize", "≡"),
        (AiAction.Rephrase, "Rephrase", "↻"),
        (AiAction.ExtractData, "Extract data", "{ }"),
        (AiAction.ActionItems, "Action items", "✓"),
        (AiAction.Translate, "Translate", "↔"),
        (AiAction.ImproveWriting, "Improve writing", "✦")
    ];
}
