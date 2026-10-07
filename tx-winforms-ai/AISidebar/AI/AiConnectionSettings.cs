using System.Text.Json.Serialization;
using TXTextControl.AI;

namespace TXTextControl.AISidebar.AI;

/// <summary>Supported hosted and local inference modes.</summary>
public enum AiProvider
{
    /// <summary>A hosted or loopback OpenAI-compatible endpoint.</summary>
    RemoteApi,
    /// <summary>A local GGUF language model and owned inference runtime.</summary>
    LocalGguf
}

/// <summary>Immutable connection and feature settings. Assign a copy with modified values to reconfigure the assistant.</summary>
public sealed record AiConnectionSettings
{
    /// <summary>Gets the hosted or local provider type.</summary>
    public AiProvider Provider { get; init; }
    /// <summary>Gets the OpenAI-compatible API base URL, including /v1 when required.</summary>
    public string Endpoint { get; init; } = "https://api.openai.com/v1";
    /// <summary>Gets the provider's chat model identifier.</summary>
    public string Model { get; init; } = "";
    /// <summary>Gets the session API key; excluded from JSON and designer serialization.</summary>
    [JsonIgnore]
    public string ApiKey { get; init; } = "";
    /// <summary>Gets whether the settings store encrypts and remembers the API key.</summary>
    public bool RememberKey { get; init; }
    /// <summary>Gets whether automatic gray continuations are enabled.</summary>
    public bool SuggestWhileTyping { get; init; } = true;
    /// <summary>Gets whether the sentence near the caret is checked after a pause.</summary>
    public bool CheckGrammarWhileTyping { get; init; } = true;
    /// <summary>Gets whether unchanged, unchecked document sentences are scanned while idle.</summary>
    public bool CheckGrammarWhenIdle { get; init; } = true;
    /// <summary>Gets the request/load timeout in seconds.</summary>
    public int TimeoutSeconds { get; init; } = 120;
    /// <summary>Gets the maximum output-token budget for explicit writing tasks.</summary>
    public int MaxOutputTokens { get; init; } = 2048;
    /// <summary>Gets the maximum source character count for explicit requests.</summary>
    public int MaxInputCharacters { get; init; } = 48000;
    /// <summary>Gets the existing local GGUF language-model file path.</summary>
    public string LocalModelPath { get; init; } = "";
    /// <summary>Gets an optional llama-server executable path; empty uses the package runtime cache.</summary>
    public string LlamaServerExecutablePath { get; init; } = "";
    /// <summary>Gets whether the AI package may acquire a missing verified inference runtime.</summary>
    public bool AllowRuntimeDownload { get; init; }
    /// <summary>Gets the local model context-token capacity.</summary>
    public int ContextSize { get; init; } = 32768;
    /// <summary>Gets local GPU offload layers; -1 selects automatic offload and 0 uses CPU.</summary>
    public int GpuLayers { get; init; } = -1;
    /// <summary>Gets the local inference hardware backend.</summary>
    public HardwareBackend HardwareBackend { get; init; } = HardwareBackend.Auto;

    /// <summary>Gets whether the basic provider fields are populated; call Validate before inference.</summary>
    [JsonIgnore]
    public bool IsConfigured => Provider == AiProvider.LocalGguf
        ? !string.IsNullOrWhiteSpace(LocalModelPath)
        : !string.IsNullOrWhiteSpace(Model) && !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>Gets a human-readable model identifier for connection setup and diagnostics.</summary>
    [JsonIgnore]
    public string DisplayName => Provider == AiProvider.LocalGguf ? Path.GetFileNameWithoutExtension(LocalModelPath) : Model;

    /// <summary>Rejects incomplete provider configuration, nonexistent local files and unsupported request limits.</summary>
    public void Validate()
    {
        if (!Enum.IsDefined(Provider)) throw new ArgumentException("Choose a supported AI provider.");
        if (Provider == AiProvider.LocalGguf)
        {
            if (!File.Exists(LocalModelPath) || !Path.GetExtension(LocalModelPath).Equals(".gguf", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choose an existing GGUF model file.");
            if (!string.IsNullOrWhiteSpace(LlamaServerExecutablePath) && !File.Exists(LlamaServerExecutablePath))
                throw new ArgumentException("The selected llama-server executable does not exist.");
            if (ContextSize is < 2048 or > 131072 || MaxOutputTokens >= ContextSize || GpuLayers is < -1 or > 999 || !Enum.IsDefined(HardwareBackend))
                throw new ArgumentException("Check the local context size, output limit and hardware settings. Output tokens must be less than the context size.");
        }
        else ValidateRemote();
        if (TimeoutSeconds is < 10 or > 600 || MaxOutputTokens is < 128 or > 16384 || MaxInputCharacters is < 1000 or > 500000)
            throw new ArgumentException("The request limits are outside the supported range.");
    }

    /// <summary>Validates the API base URL and requires a model and authentication value.</summary>
    private void ValidateRemote()
    {
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var endpoint))
            throw new ArgumentException("Enter a valid API base URL, including /v1 if required.");
        OpenAIChatClientFactory.ValidateEndpoint(endpoint);
        if (endpoint.AbsolutePath.TrimEnd('/').EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Enter the API base URL, not the /chat/completions operation URL.");
        if (string.IsNullOrWhiteSpace(Model))
            throw new ArgumentException("Enter the model identifier provided by your AI service.");
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new ArgumentException("Enter an API key. For a local server without authentication, use 'local'.");
    }

}
