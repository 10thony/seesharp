using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenAI.Chat;
using OpenAI.Responses;

namespace SeeSharp.Models
{
    public enum SubAgentStatus
    {
        Queued,
        WaitingForInference,
        Running,
        Completed,
        Failed,
        Killed
    }

    public sealed class SubAgentHandle
    {
        public string Id { get; init; } = "";
        public string Task { get; init; } = "";
        public string WorkspacePath { get; init; } = "";
        public string ModelId { get; init; } = "";
        public SubAgentStatus Status { get; set; }
        public string? Result { get; set; }
        public string? Error { get; set; }
        public int Depth { get; init; }
        public int TurnsElapsed { get; set; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset? CompletedAt { get; set; }
        public CancellationTokenSource Cts { get; init; } = new();
        public Task? AgentTask { get; set; }
    }

    /// <summary>
    /// Manages subagent lifecycle: spawn, check, kill. Coordinates with
    /// <see cref="InferenceCoordinator"/> to schedule subagent LLM turns
    /// during parent idle windows.
    /// </summary>
    public sealed class SubAgentManager : IDisposable
    {
        private readonly InferenceCoordinator _coordinator;
        private readonly ResolvedConfig _config;
        private readonly ConcurrentDictionary<string, SubAgentHandle> _agents = new();
        private readonly ResponsesClient _responsesClient;
        private readonly ChatClient? _contextualizerChatClient;
        private readonly object _repoContextLock = new();
        private string _parentRepoContext;
        private int _nextId;

        public SubAgentManager(
            InferenceCoordinator coordinator,
            ResolvedConfig config,
            ResponsesClient responsesClient,
            ChatClient? contextualizerChatClient = null,
            string? parentRepoContext = null)
        {
            _coordinator = coordinator;
            _config = config;
            _responsesClient = responsesClient;
            _contextualizerChatClient = contextualizerChatClient;
            _parentRepoContext = parentRepoContext ?? "";
        }

        public void SetSharedRepoContext(string repoContext)
        {
            if (string.IsNullOrWhiteSpace(repoContext))
            {
                return;
            }

            lock (_repoContextLock)
            {
                _parentRepoContext = repoContext;
            }
        }

        public int ActiveCount => _agents.Values.Count(a =>
            a.Status is SubAgentStatus.Queued or SubAgentStatus.WaitingForInference or SubAgentStatus.Running);

        /// <summary>
        /// Spawns a new subagent with the given task. Returns a handle immediately;
        /// the agent begins work in the background taking inference slots when available.
        /// </summary>
        public SubAgentHandle Spawn(
            string task,
            string? workspacePath,
            string? modelOverride,
            int parentDepth,
            CancellationToken parentCt)
        {
            int newDepth = parentDepth + 1;

            if (newDepth > _config.SubAgentMaxDepth)
            {
                throw new InvalidOperationException(
                    $"Cannot spawn subagent: max depth {_config.SubAgentMaxDepth} exceeded (current depth: {parentDepth}).");
            }

            if (ActiveCount >= _config.SubAgentMaxConcurrent)
            {
                throw new InvalidOperationException(
                    $"Cannot spawn subagent: max concurrent limit {_config.SubAgentMaxConcurrent} reached ({ActiveCount} active).");
            }

            string id = $"sa-{Interlocked.Increment(ref _nextId):D3}";
            string modelId = modelOverride ?? _config.SubAgentModelId;
            string workspace = ResolveWorkspacePath(workspacePath);

            var handle = new SubAgentHandle
            {
                Id = id,
                Task = task,
                WorkspacePath = workspace,
                ModelId = modelId,
                Status = SubAgentStatus.Queued,
                Depth = newDepth,
                CreatedAt = DateTimeOffset.UtcNow,
                Cts = CancellationTokenSource.CreateLinkedTokenSource(parentCt)
            };

            _agents[id] = handle;

            handle.AgentTask = Task.Run(async () =>
            {
                try
                {
                    await RunSubAgentAsync(handle).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    handle.Status = SubAgentStatus.Killed;
                    handle.Error = "Cancelled";
                }
                catch (Exception ex)
                {
                    handle.Status = SubAgentStatus.Failed;
                    handle.Error = ex.Message;
                }
                finally
                {
                    handle.CompletedAt = DateTimeOffset.UtcNow;
                }
            });

            ThemedConsole.WriteLine(TerminalTone.Reasoning,
                $"[SubAgentManager] Spawned {id} (depth={newDepth}, model={modelId}): {Truncate(task, 80)}");

            return handle;
        }

        public SubAgentHandle? Check(string id)
        {
            return _agents.TryGetValue(id, out var handle) ? handle : null;
        }

        public bool Kill(string id)
        {
            if (!_agents.TryGetValue(id, out var handle))
                return false;

            if (handle.Status is SubAgentStatus.Completed or SubAgentStatus.Failed or SubAgentStatus.Killed)
                return false;

            handle.Cts.Cancel();
            handle.Status = SubAgentStatus.Killed;
            handle.CompletedAt = DateTimeOffset.UtcNow;

            ThemedConsole.WriteLine(TerminalTone.Reasoning,
                $"[SubAgentManager] Killed {id}");
            return true;
        }

        public Dictionary<string, object> SpawnAsToolResult(SubAgentHandle handle)
        {
            return new Dictionary<string, object>
            {
                ["subagent_id"] = handle.Id,
                ["status"] = handle.Status.ToString().ToLowerInvariant(),
                ["model"] = handle.ModelId,
                ["depth"] = handle.Depth
            };
        }

        public Dictionary<string, object> CheckAsToolResult(string id)
        {
            var handle = Check(id);
            if (handle is null)
            {
                return new Dictionary<string, object>
                {
                    ["error"] = $"No subagent found with id: {id}"
                };
            }

            var result = new Dictionary<string, object>
            {
                ["subagent_id"] = handle.Id,
                ["status"] = handle.Status.ToString().ToLowerInvariant(),
                ["turns_elapsed"] = handle.TurnsElapsed,
                ["model"] = handle.ModelId
            };

            if (handle.Status == SubAgentStatus.Completed && handle.Result is not null)
            {
                result["result"] = handle.Result;
            }

            if (handle.Status == SubAgentStatus.Failed && handle.Error is not null)
            {
                result["error"] = handle.Error;
            }

            return result;
        }

        public Dictionary<string, object> KillAsToolResult(string id)
        {
            bool killed = Kill(id);
            return new Dictionary<string, object>
            {
                ["subagent_id"] = id,
                ["killed"] = killed,
                ["status"] = killed ? "killed" : (_agents.ContainsKey(id) ? "already_terminated" : "not_found")
            };
        }

        private async Task RunSubAgentAsync(SubAgentHandle handle)
        {
            handle.Status = SubAgentStatus.Running;
            var subAgentConfig = CreateSubAgentConfig(handle);

            bool stripSubAgentTools = handle.Depth >= _config.SubAgentMaxDepth;
            if (stripSubAgentTools)
            {
                subAgentConfig.DisabledTools.Add(AgentDefaults.SPAWN_SUBAGENT_TOOL_NAME);
                subAgentConfig.DisabledTools.Add(AgentDefaults.CHECK_SUBAGENT_TOOL_NAME);
                subAgentConfig.DisabledTools.Add(AgentDefaults.KILL_SUBAGENT_TOOL_NAME);
            }

            using (AgentUtilities.PushWorkspaceRoot(handle.WorkspacePath))
            {
                var toolKit = new ToolKit(subAgentConfig);
                if (!stripSubAgentTools)
                {
                    toolKit.SubAgentManager = this;
                }

                var agent = new Agent(
                    model: null,
                    toolRegistry: toolKit,
                    contextualizerChatClient: null,
                    config: subAgentConfig)
                {
                    ModelId = handle.ModelId,
                    InferenceCoordinator = _coordinator,
                    AgentId = handle.Id,
                    Depth = handle.Depth
                };

                string parentRepoContext = GetSharedRepoContext();
                if (_config.SubAgentShareRepoContext && !string.IsNullOrWhiteSpace(parentRepoContext))
                {
                    agent.SharedRepoContext = parentRepoContext;
                }

                var taskList = new List<string> { handle.Task };
                var result = await agent.AgentLoop(
                    _responsesClient,
                    taskList,
                    handle.Cts.Token).ConfigureAwait(false);

                handle.TurnsElapsed = agent.TotalTurnsExecuted;
                handle.Result = result.ToString();
                handle.Status = SubAgentStatus.Completed;
            }
        }

        private ResolvedConfig CreateSubAgentConfig(SubAgentHandle handle)
        {
            return new ResolvedConfig
            {
                AgentName = $"SubAgent-{handle.Id}",
                PreferredModel = handle.ModelId,
                MaxAgentTurnsPerTask = _config.SubAgentMaxTurnsPerAgent,
                MaxToolCallsPerTurn = _config.MaxToolCallsPerTurn,
                MaxSuccessfulToolExecutionsPerTask = _config.SubAgentMaxToolExecutionsPerAgent,
                BashCommandTimeout = _config.BashCommandTimeout,
                ResponsesApiCallTimeout = _config.ResponsesApiCallTimeout,
                ContextualizerCallTimeout = _config.ContextualizerCallTimeout,
                MaxNoProgressTurns = Math.Min(_config.MaxNoProgressTurns, 2),
                MaxResponseRetries = Math.Min(_config.MaxResponseRetries, 2),
                SystemPromptPrepend = _config.SystemPromptPrepend,
                SystemPromptAppend = _config.SystemPromptAppend,
                SystemPromptReplace = _config.SystemPromptReplace,
                ContextualizerMaxFilesToRead = _config.ContextualizerMaxFilesToRead,
                ContextualizerMaxCharsPerFile = _config.ContextualizerMaxCharsPerFile,
                ContextualizerExcludedDirectories = new List<string>(_config.ContextualizerExcludedDirectories),
                ContextualizerPinnedFiles = new List<string>(_config.ContextualizerPinnedFiles),
                ContextualizerPreferredModel = _config.ContextualizerPreferredModel,
                CustomTools = new List<CustomToolDefinition>(_config.CustomTools),
                DisabledTools = new HashSet<string>(_config.DisabledTools, StringComparer.OrdinalIgnoreCase),
                AgentColor = _config.AgentColor,
                ToolColor = _config.ToolColor,
                ErrorColor = _config.ErrorColor,
                ReasoningColor = _config.ReasoningColor,
                UserColor = _config.UserColor,
                ActionColor = _config.ActionColor,
                SubAgentsEnabled = _config.SubAgentsEnabled,
                SubAgentModelStrategy = _config.SubAgentModelStrategy,
                SubAgentModelId = _config.SubAgentModelId,
                SubAgentMaxConcurrent = _config.SubAgentMaxConcurrent,
                SubAgentMaxDepth = _config.SubAgentMaxDepth,
                SubAgentMaxTurnsPerAgent = _config.SubAgentMaxTurnsPerAgent,
                SubAgentMaxToolExecutionsPerAgent = _config.SubAgentMaxToolExecutionsPerAgent,
                SubAgentShareRepoContext = _config.SubAgentShareRepoContext,
                SubAgentInferenceTimeout = _config.SubAgentInferenceTimeout,
                SubAgentModelSwapTimeout = _config.SubAgentModelSwapTimeout,
                GlobalConfigPath = _config.GlobalConfigPath,
                WorkspaceConfigPath = _config.WorkspaceConfigPath
            };
        }

        private string GetSharedRepoContext()
        {
            lock (_repoContextLock)
            {
                return _parentRepoContext;
            }
        }

        private static string ResolveWorkspacePath(string? workspacePath)
        {
            string root = AgentUtilities.ResolveWorkspaceRoot();
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return root;
            }

            return Path.GetFullPath(Path.IsPathRooted(workspacePath)
                ? workspacePath
                : Path.Combine(root, workspacePath));
        }

        private static string Truncate(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= maxLen ? s : s[..maxLen] + "...";
        }

        public void Dispose()
        {
            foreach (var handle in _agents.Values)
            {
                if (handle.Status is SubAgentStatus.Queued or SubAgentStatus.WaitingForInference or SubAgentStatus.Running)
                {
                    handle.Cts.Cancel();
                }
                handle.Cts.Dispose();
            }
        }
    }
}
