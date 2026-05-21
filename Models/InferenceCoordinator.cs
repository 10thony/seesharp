using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SeeSharp.Models
{
    public enum ModelSchedulingStrategy
    {
        SameModel,
        TieredModels,
        DedicatedSwap
    }

    /// <summary>
    /// GPU access gate for single-GPU inference scheduling. Ensures only one agent
    /// talks to LM Studio at a time and handles model swaps between parent/subagent turns.
    /// </summary>
    public sealed class InferenceCoordinator : IDisposable
    {
        private readonly SemaphoreSlim _inferenceGate = new(1, 1);
        private readonly ModelSchedulingStrategy _strategy;
        private string _currentLoadedModelId;
        private readonly string _lmStudioBaseUri;
        private readonly string _apiKey;
        private readonly TimeSpan _inferenceTimeout;
        private readonly TimeSpan _modelSwapTimeout;
        private readonly ConcurrentDictionary<string, AgentInferenceMetrics> _agentMetrics = new();

        private readonly Func<IReadOnlyCollection<string>, CancellationToken, Task>? _keepOnlyModelsLoaded;

        public string CurrentLoadedModelId => _currentLoadedModelId;
        public ModelSchedulingStrategy Strategy => _strategy;

        public InferenceCoordinator(
            ModelSchedulingStrategy strategy,
            string parentModelId,
            string subAgentModelId,
            string lmStudioBaseUri,
            string apiKey,
            TimeSpan inferenceTimeout,
            TimeSpan modelSwapTimeout,
            Func<IReadOnlyCollection<string>, CancellationToken, Task>? keepOnlyModelsLoaded = null)
        {
            if (string.IsNullOrWhiteSpace(parentModelId))
                throw new ArgumentException("Parent model ID is required.", nameof(parentModelId));
            if (string.IsNullOrWhiteSpace(subAgentModelId))
                throw new ArgumentException("Subagent model ID is required.", nameof(subAgentModelId));

            _strategy = strategy;
            _currentLoadedModelId = parentModelId;
            _lmStudioBaseUri = lmStudioBaseUri;
            _apiKey = apiKey;
            _inferenceTimeout = inferenceTimeout;
            _modelSwapTimeout = modelSwapTimeout;
            _keepOnlyModelsLoaded = keepOnlyModelsLoaded;
        }

        /// <summary>
        /// Acquires exclusive inference access for an agent. Performs model swap if
        /// the required model differs from the currently loaded model.
        /// Returns a disposable slot release handle.
        /// </summary>
        public async Task<InferenceSlot> AcquireInferenceSlotAsync(
            string requestingAgentId,
            string requiredModelId,
            CancellationToken ct)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_inferenceTimeout);

            var metrics = _agentMetrics.GetOrAdd(requestingAgentId, _ => new AgentInferenceMetrics());
            var sw = Stopwatch.StartNew();

            try
            {
                await _inferenceGate.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Agent '{requestingAgentId}' timed out waiting for inference slot after {_inferenceTimeout.TotalSeconds}s.");
            }

            metrics.TotalWaitTime += sw.Elapsed;
            metrics.AcquisitionCount++;

            try
            {
                if (_strategy != ModelSchedulingStrategy.SameModel &&
                    !string.Equals(_currentLoadedModelId, requiredModelId, StringComparison.OrdinalIgnoreCase))
                {
                    await SwapModelAsync(requiredModelId, ct).ConfigureAwait(false);
                }
            }
            catch
            {
                _inferenceGate.Release();
                throw;
            }

            metrics.LastAcquiredAt = DateTimeOffset.UtcNow;
            return new InferenceSlot(_inferenceGate, requestingAgentId, this);
        }

        /// <summary>
        /// Called by Agent when entering GPU-free tool execution (BASH, WEB_CALL).
        /// Allows metrics tracking and priority hinting.
        /// </summary>
        public void NotifyToolExecutionStarted(string agentId)
        {
            if (_agentMetrics.TryGetValue(agentId, out var metrics))
            {
                metrics.ToolExecutionsStarted++;
                metrics.LastToolStartedAt = DateTimeOffset.UtcNow;
            }
        }

        /// <summary>
        /// Called by Agent when tool execution completes.
        /// </summary>
        public void NotifyToolExecutionCompleted(string agentId)
        {
            if (_agentMetrics.TryGetValue(agentId, out var metrics))
            {
                metrics.ToolExecutionsCompleted++;
            }
        }

        public AgentInferenceMetrics? GetMetrics(string agentId)
        {
            return _agentMetrics.TryGetValue(agentId, out var m) ? m : null;
        }

        private async Task SwapModelAsync(string targetModelId, CancellationToken ct)
        {
            var swapSw = Stopwatch.StartNew();
            ThemedConsole.WriteLine(TerminalTone.Reasoning,
                $"[InferenceCoordinator] Swapping model: {_currentLoadedModelId} -> {targetModelId}");

            try
            {
                using var swapCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                swapCts.CancelAfter(_modelSwapTimeout);

                if (_keepOnlyModelsLoaded is not null)
                {
                    await _keepOnlyModelsLoaded(
                        new[] { targetModelId },
                        swapCts.Token).ConfigureAwait(false);
                }
                else
                {
                    await UnloadModelAsync(_currentLoadedModelId, swapCts.Token).ConfigureAwait(false);
                    await LoadModelAsync(targetModelId, swapCts.Token).ConfigureAwait(false);
                }

                _currentLoadedModelId = targetModelId;
                ThemedConsole.WriteLine(TerminalTone.Reasoning,
                    $"[InferenceCoordinator] Model swap complete in {swapSw.ElapsedMilliseconds}ms. Active: {targetModelId}");
            }
            catch (Exception ex)
            {
                ThemedConsole.WriteLine(TerminalTone.Error,
                    $"[InferenceCoordinator] Model swap failed: {ex.Message}");
                throw new InvalidOperationException(
                    $"Model swap to '{targetModelId}' failed: {ex.Message}", ex);
            }
        }

        private async Task UnloadModelAsync(string modelId, CancellationToken ct)
        {
            bool unloaded = await TryPostModelLifecycleAsync(
                modelId,
                new[] { "models/unload", "../api/v0/models/unload" },
                TimeSpan.FromSeconds(10),
                ct).ConfigureAwait(false);
            if (!unloaded)
            {
                throw new InvalidOperationException($"Unable to unload model '{modelId}'.");
            }
        }

        private async Task LoadModelAsync(string modelId, CancellationToken ct)
        {
            bool loaded = await TryPostModelLifecycleAsync(
                modelId,
                new[] { "models/load", "../api/v0/models/load" },
                TimeSpan.FromSeconds(30),
                ct).ConfigureAwait(false);
            if (!loaded)
            {
                throw new InvalidOperationException($"Unable to load model '{modelId}'.");
            }
        }

        private async Task<bool> TryPostModelLifecycleAsync(
            string modelId,
            IReadOnlyCollection<string> routes,
            TimeSpan timeout,
            CancellationToken ct)
        {
            Uri baseUri = new Uri(_lmStudioBaseUri.TrimEnd('/') + "/", UriKind.Absolute);
            using HttpClient http = new HttpClient { Timeout = timeout };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            foreach (string route in routes)
            {
                try
                {
                    Uri requestUri = new Uri(baseUri, route);
                    using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
                    {
                        Content = new StringContent(
                            JsonSerializer.Serialize(new { model = modelId }),
                            Encoding.UTF8,
                            "application/json")
                    };
                    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                        .ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        return true;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                }
            }

            return false;
        }

        public void Dispose()
        {
            _inferenceGate.Dispose();
        }
    }

    /// <summary>
    /// RAII handle returned by <see cref="InferenceCoordinator.AcquireInferenceSlotAsync"/>.
    /// Disposing releases the semaphore.
    /// </summary>
    public sealed class InferenceSlot : IDisposable
    {
        private readonly SemaphoreSlim _gate;
        private readonly string _agentId;
        private readonly InferenceCoordinator _coordinator;
        private int _disposed;

        internal InferenceSlot(SemaphoreSlim gate, string agentId, InferenceCoordinator coordinator)
        {
            _gate = gate;
            _agentId = agentId;
            _coordinator = coordinator;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _gate.Release();
            }
        }
    }

    public sealed class AgentInferenceMetrics
    {
        public int AcquisitionCount { get; set; }
        public TimeSpan TotalWaitTime { get; set; }
        public DateTimeOffset? LastAcquiredAt { get; set; }
        public DateTimeOffset? LastToolStartedAt { get; set; }
        public int ToolExecutionsStarted { get; set; }
        public int ToolExecutionsCompleted { get; set; }
    }
}
