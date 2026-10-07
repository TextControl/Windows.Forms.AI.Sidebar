using Microsoft.Extensions.AI;
using TXTextControl.AI;

namespace TXTextControl.AISidebar.AI;

/// <summary>Runs bounded hosted or local inference, serializing requests and reusing an owned local runtime.</summary>
public sealed partial class AiAssistantService : IAsyncDisposable
{
    /// <summary>Generates a complete result, emitting accumulated previews and rejecting truncated or invalid structured output.</summary>
    public async Task<string> GenerateAsync(AiConnectionSettings settings, AiAction action, string source,
        string tone, string language, string instructions, Action<string> onUpdate, CancellationToken cancellationToken,
        Action<string>? onStatus = null)
    {
        settings.Validate();
        if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("Select some text or add content to the document first.");
        if (source.Length > settings.MaxInputCharacters)
            throw new ArgumentException($"The source contains {source.Length:N0} characters. Select a smaller passage or raise the input limit in Connection settings (currently {settings.MaxInputCharacters:N0}).");
        using var operation = await EnterAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        List<ChatMessage> messages = [new(ChatRole.System, AiPrompts.SystemPrompt),
            new(ChatRole.User, AiPrompts.Create(action, source, tone, language, instructions))];
        var result = new System.Text.StringBuilder();
        try
        {
            using var client = await CreateClientAsync(settings, onStatus, timeout.Token);
            CheckContextBudget(settings, messages);
            await foreach (var update in client.GetStreamingResponseAsync(messages,
                new ChatOptions { MaxOutputTokens = settings.MaxOutputTokens }, timeout.Token))
            {
                result.Append(update.Text);
                onUpdate(result.ToString());
                if (update.FinishReason == ChatFinishReason.Length)
                    throw new InvalidOperationException("The response reached the output token limit. Increase the limit in Connection settings or request a shorter result.");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"The AI service did not complete within {settings.TimeoutSeconds} seconds. Try a smaller selection or increase the timeout.");
        }
        if (string.IsNullOrWhiteSpace(result.ToString()))
            throw new InvalidOperationException("The AI service returned no text. Check the model and connection settings.");
        if (action == AiAction.ExtractData)
        {
            try
            {
                using var json = System.Text.Json.JsonDocument.Parse(result.ToString());
                if (json.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                    throw new System.Text.Json.JsonException();
                foreach (var key in new[] { "people", "organizations", "dates", "amounts", "keyFacts" })
                    if (!json.RootElement.TryGetProperty(key, out var value) || value.ValueKind != System.Text.Json.JsonValueKind.Array)
                        throw new System.Text.Json.JsonException();
            }
            catch (System.Text.Json.JsonException)
            {
                throw new InvalidOperationException("The model did not return the requested JSON structure. Try Extract data again or use a different model.");
            }
        }
        return result.ToString();
    }

    /// <summary>Checks the configured provider with a short prompt containing no document text.</summary>
    public async Task TestConnectionAsync(AiConnectionSettings settings, CancellationToken cancellationToken, Action<string>? onStatus = null)
    {
        settings.Validate();
        using var operation = await EnterAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.Provider == AiProvider.LocalGguf ? settings.TimeoutSeconds : Math.Min(settings.TimeoutSeconds, 30)));
        using var client = await CreateClientAsync(settings, onStatus, timeout.Token);
        var result = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Reply with OK.")],
            new ChatOptions { MaxOutputTokens = 128 }, timeout.Token);
        if (string.IsNullOrWhiteSpace(result.Text)) throw new InvalidOperationException("The service returned no text.");
    }

}
