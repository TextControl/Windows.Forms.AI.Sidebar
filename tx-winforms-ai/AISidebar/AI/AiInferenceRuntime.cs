using Microsoft.Extensions.AI;
using TXTextControl.AI;

namespace TXTextControl.AISidebar.AI;

/// <summary>Runs bounded hosted or local inference, serializing requests and reusing an owned local runtime.</summary>
public sealed partial class AiAssistantService
{
    private readonly SemaphoreSlim _operationGate = new(1);
    private LocalLanguageModel? _localModel;
    private LocalConfiguration? _localConfiguration;
    private bool _disposed;
    /// <summary>Gets the currently loaded local model path, or null when no model is owned.</summary>
    public string? LoadedModelPath => _localConfiguration?.Path;

    /// <summary>Acquires the service operation gate so loading, inference and unloading cannot race.</summary>
    private async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken);
        if (_disposed) { _operationGate.Release(); throw new ObjectDisposedException(nameof(AiAssistantService)); }
        return new OperationLease(_operationGate);
    }

    /// <summary>Creates a hosted client or reuses a matching local model with a fresh independent session.</summary>
    private async Task<IChatClient> CreateClientAsync(AiConnectionSettings settings, Action<string>? onStatus, CancellationToken cancellationToken)
    {
        if (settings.Provider == AiProvider.RemoteApi)
        {
            await ReleaseModelAsync();
            return OpenAIChatClientFactory.Create(new OpenAIChatClientOptions
            {
                Endpoint = new Uri(settings.Endpoint), Model = settings.Model, ApiKey = settings.ApiKey,
                RequestTimeout = TimeSpan.FromSeconds(settings.TimeoutSeconds)
            });
        }
        var configuration = new LocalConfiguration(Path.GetFullPath(settings.LocalModelPath), settings.LlamaServerExecutablePath,
            settings.AllowRuntimeDownload, settings.ContextSize, settings.GpuLayers, settings.HardwareBackend, settings.TimeoutSeconds);
        if (_localModel is null || configuration != _localConfiguration)
        {
            await ReleaseModelAsync();
            var progress = new Progress<ModelLoadProgress>(update => onStatus?.Invoke($"{update.Stage} · {update.Fraction:P0}"));
            _localModel = await LocalLanguageModel.LoadAsync(configuration.Path, new LocalModelOptions
            {
                LlamaServerExecutablePath = string.IsNullOrWhiteSpace(configuration.Executable) ? null : configuration.Executable,
                RuntimeAcquisition = configuration.Download ? RuntimeAcquisitionPolicy.DownloadIfMissing : RuntimeAcquisitionPolicy.NeverDownload,
                ContextSize = configuration.Context, GpuLayers = configuration.GpuLayers, HardwareBackend = configuration.Backend,
                MaxOutputTokens = settings.MaxOutputTokens, InferenceTimeout = TimeSpan.FromSeconds(settings.TimeoutSeconds),
                EnableReasoning = false
            }, progress, cancellationToken);
            _localConfiguration = configuration;
        }
        onStatus?.Invoke($"Local model ready · {_localModel.Info.Name} · {_localModel.Runtime.Backend}");
        return _localModel.CreateSession();
    }

    /// <summary>Rejects a local request when estimated input plus output exceeds the configured context window.</summary>
    private static void CheckContextBudget(AiConnectionSettings settings, IReadOnlyList<ChatMessage> messages, int? outputTokens = null)
    {
        if (settings.Provider != AiProvider.LocalGguf) return;
        var planner = new TokenAwareContextManager();
        int budget = planner.GetInputBudget(settings.ContextSize, outputTokens ?? settings.MaxOutputTokens, 0, false);
        int estimate = messages.Sum(message => planner.EstimateTokens(message.Text) + 12);
        if (estimate > budget)
            throw new ArgumentException("The prompt exceeds the local model's context budget. Choose a smaller selection, shorten instructions, reduce output tokens or increase the context size.");
    }

    /// <summary>Waits for active inference and releases the owned local runtime.</summary>
    public async Task UnloadModelAsync(CancellationToken cancellationToken = default)
    {
        using var operation = await EnterAsync(cancellationToken);
        await ReleaseModelAsync();
    }

    /// <summary>Clears model identity before asynchronously disposing the process and model resources.</summary>
    private async Task ReleaseModelAsync()
    {
        var model = _localModel;
        _localModel = null; _localConfiguration = null;
        if (model is not null) await model.DisposeAsync();
    }

    /// <summary>Waits for active inference, prevents new operations, and releases the owned runtime.</summary>
    public async ValueTask DisposeAsync()
    {
        await _operationGate.WaitAsync();
        try { if (_disposed) return; _disposed = true; await ReleaseModelAsync(); }
        finally { _operationGate.Release(); }
    }

    private sealed record LocalConfiguration(string Path, string Executable, bool Download, int Context, int GpuLayers,
        HardwareBackend Backend, int Timeout);
    private sealed class OperationLease(SemaphoreSlim gate) : IDisposable { public void Dispose() => gate.Release(); }
}
