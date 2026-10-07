using TXTextControl.AISidebar.AI;

namespace TXTextControl.AISidebar.UI;

/// <summary>Immutable task preferences captured when a selection action starts.</summary>
internal sealed record QuickAiRequest(AiAction Action, string? Instructions = null, string? Language = null, string? Tone = null);
/// <summary>Completed popup text and a guarded callback that applies it to its captured source.</summary>
internal sealed record QuickAiResult(string Text, Action Apply);
/// <summary>Runs a task against a previously captured selection, with accumulated previews and cancellation.</summary>
internal delegate Task<QuickAiResult> QuickAiRunner(QuickAiRequest request, Action<string> onUpdate, CancellationToken token);
