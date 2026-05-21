using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenAI.Chat;
using OpenAI.Models;
using OpenAI.Responses;

namespace SeeSharp.Models
{
    public sealed class AgentRuntime : IDisposable
    {
        private readonly InferenceCoordinator? _inferenceCoordinator;
        private readonly SubAgentManager? _subAgentManager;

        internal AgentRuntime(
            Agent agent,
            ToolKit toolKit,
            InferenceCoordinator? inferenceCoordinator,
            SubAgentManager? subAgentManager)
        {
            Agent = agent;
            ToolKit = toolKit;
            _inferenceCoordinator = inferenceCoordinator;
            _subAgentManager = subAgentManager;
        }

        public Agent Agent { get; }
        public ToolKit ToolKit { get; }

        public void Dispose()
        {
            _subAgentManager?.Dispose();
            _inferenceCoordinator?.Dispose();
        }
    }

    public static class AgentRuntimeFactory
    {
        public static AgentRuntime Create(
            OpenAIModel model,
            ResolvedConfig config,
            ResponsesClient responsesClient,
            ChatClient? contextualizerChatClient,
            string lmStudioBaseUri,
            string apiKey,
            Func<IReadOnlyCollection<string>, CancellationToken, Task>? keepOnlyModelsLoadedAsync,
            SessionRecorder? sessionRecorder = null,
            string agentId = "parent",
            int depth = 0)
        {
            ToolKit toolKit = new(config);
            InferenceCoordinator? inferenceCoordinator = null;
            SubAgentManager? subAgentManager = null;

            if (config.SubAgentsEnabled)
            {
                inferenceCoordinator = new InferenceCoordinator(
                    strategy: config.SubAgentModelStrategy,
                    parentModelId: model.Id,
                    subAgentModelId: config.SubAgentModelId,
                    lmStudioBaseUri: lmStudioBaseUri,
                    apiKey: apiKey,
                    inferenceTimeout: config.SubAgentInferenceTimeout,
                    modelSwapTimeout: config.SubAgentModelSwapTimeout,
                    keepOnlyModelsLoaded: keepOnlyModelsLoadedAsync);

                subAgentManager = new SubAgentManager(
                    inferenceCoordinator,
                    config,
                    responsesClient,
                    contextualizerChatClient);

                toolKit.SubAgentManager = subAgentManager;

                ThemedConsole.WriteLine(TerminalTone.Reasoning,
                    $"[SubAgents] Enabled: strategy={config.SubAgentModelStrategy}, " +
                    $"subAgentModel={config.SubAgentModelId}, maxConcurrent={config.SubAgentMaxConcurrent}");
            }

            Agent agent = new(model, toolKit, contextualizerChatClient, config)
            {
                SessionRecorder = sessionRecorder,
                InferenceCoordinator = inferenceCoordinator,
                AgentId = agentId,
                Depth = depth
            };

            return new AgentRuntime(agent, toolKit, inferenceCoordinator, subAgentManager);
        }
    }
}
