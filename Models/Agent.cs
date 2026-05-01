using OpenAI.Chat;
using OpenAI.Models;
using OpenAI.Responses;
using SeeSharp.Models.Persistence;
using System;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace SeeSharp.Models
{

    public class LMStudioAgent(
        OpenAIModel model,
        ToolKit toolRegistry,
        ChatClient contextualizerChatClient,
        ConvexService? convexService = null)
        : Agent(model, toolRegistry, contextualizerChatClient, convexService)
    {
    }

    /// <summary>
    /// Our base openai compliant Agent class
    /// this will have built in responses / chat completions functionality that is openai compliant
    /// this is so that the agent can be later wrapped to support different standards.
    /// 
    /// each method represents an action that our agents are expected to support.
    /// e.g. 
    /// </summary>
    public abstract class Agent
    {
        public const string DefaultContextualizerModelId = "qwen/qwen3.5-2b";
        public const string DefaultToolInvocationHandlerModel = "qwen/qwen3.5-4b";
        // Local models often need many one-tool turns (read → locate → read → edit → verify).
        private const int MaxAgentTurnsPerTask = 28;
        /// <summary>Caps how many tool lines we execute from a single assistant message (prevents parallel spam).</summary>
        private const int MaxToolCallsPerTurn = 2;
        /// <summary>Hard cap on successful (non-skipped) tool executions per user task.</summary>
        private const int MaxSuccessfulToolExecutionsPerTask = 22;
        private const int MaxResponseRetries = 3;
        private const int MaxCompletionValidationRetries = 2;
        private const int MaxSerializedToolOutputChars = 12_000;
        private const int MaxToolStringValueChars = 4_000;
        private const int MaxNoProgressTurns = 3;
        private const int MaxContextResetAttemptsPerTask = 3;

        public OpenAIModel Model { get; set; }
        private ToolKit ToolKit { get; set;  }
        private readonly ChatClient? _contextualizerChatClient;
        private readonly ConvexService? _convexService;
        
        public Agent(OpenAIModel model,
                     ToolKit toolRegistry,
                     ChatClient? contextualizerChatClient = null,
                     ConvexService? convexService = null)
        {
            this.Model = model;
            this.ToolKit = toolRegistry;
            _contextualizerChatClient = contextualizerChatClient;
            _convexService = convexService;
        }

        public async Task<StringBuilder> AgentLoop(
            ResponsesClient lmStudioResponsesClient,
            List<string> tasks,
            CancellationToken cancellationToken = default)
        {

            if(tasks.Count() == 0)
            {
                return new StringBuilder("No tasks provided to agent.");
            }

            StringBuilder sb = new StringBuilder();
            string repoContext = "";

            try
            {
                sb.AppendLine($"Contextualizing Agent with local project." +
                    $"; Running model {this.Model.Id}");

                ThemedConsole.WriteLine(TerminalTone.Reasoning, sb.ToString());
                repoContext = await ContextualizeAgentAsync(tasks.First(), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ThemedConsole.WriteLine(TerminalTone.Error, "[Contextualizer] Exception during contextualization:");
                ThemedConsole.WriteLine(TerminalTone.Error, FormatExceptionDetail(ex));
                //add resiliant context generating here. if it fails we should indicate and retry atleast thrice
            }

            if (string.IsNullOrEmpty(repoContext))
            {
                ThemedConsole.WriteLine(TerminalTone.Error,
                    "[Contextualizer] No repository context was produced. " +
                    "See preceding [Contextualizer] debug lines (empty model output, bad JSON paths, or no file summaries).");
            }

            sb.AppendLine($"[Contextualizer] {repoContext}");

            ThemedConsole.WriteLine(TerminalTone.Reasoning, $"[Contextualizer] {repoContext}");


            foreach (string task in tasks)
            {
                if (task.Contains("exit", StringComparison.OrdinalIgnoreCase))
                {
                    ThemedConsole.WriteLine(TerminalTone.Default, "Exiting the program. Goodbye!");
                    break;
                }

                if (String.IsNullOrEmpty(repoContext))
                {
                    ThemedConsole.WriteLine(TerminalTone.Error,
                        "[Contextualizer] ERROR CONTEXTUALIZING AGENT — cannot run tasks without repo context.");
                    break;
                }

                ThemedConsole.WriteLine(TerminalTone.User, $"You: {task}");
                sb.AppendLine($"You: {task}");

                string finalAnswer = await ExecuteTaskWithToolLoopAsync(
                    lmStudioResponsesClient,
                    task,
                    repoContext,
                    cancellationToken).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(finalAnswer))
                {
                    ThemedConsole.WriteLine(TerminalTone.Agent, $" {finalAnswer}");
                }

            }

            return sb;
        }

        private async Task<string> ExecuteTaskWithToolLoopAsync(
            ResponsesClient responsesClient,
            string task,
            string repoContext,
            CancellationToken cancellationToken)
        {
            string taskRunId = Guid.NewGuid().ToString("N");
            long startedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await TryPersistTaskRunStartAsync(taskRunId, task, repoContext, startedAt, cancellationToken)
                .ConfigureAwait(false);

            async Task<string> CompleteAndReturnAsync(string finalText, string status)
            {
                await TryPersistTaskRunCompletionAsync(
                        taskRunId,
                        status,
                        finalText,
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        cancellationToken)
                    .ConfigureAwait(false);
                return finalText;
            }

            string currentInput = task;
            string lastAssistantText = "";
            bool shouldRehydrateRepoContext = false;
            bool lowContextMode = false;
            int contextResetAttempts = 0;
            int noProgressTurns = 0;
            string lastProgressSignature = "";
            HashSet<string> seenToolSignatures = new(StringComparer.OrdinalIgnoreCase);
            int successfulToolExecutions = 0;
            int consecutiveAllSkippedTurns = 0;
            int completionValidationRetriesUsed = 0;
            List<ToolExecutionEnvelope> cumulativeToolOutputs = new();

            for (int turn = 0; turn < MaxAgentTurnsPerTask; turn++)
            {
                string? perTurnRepoContext = (turn == 0 || shouldRehydrateRepoContext) ? repoContext : null;
                shouldRehydrateRepoContext = false;
                CreateResponseResult assistantResponse;
                try
                {
                    assistantResponse = await WithPerCallTimeoutAndTelemetryAsync(
                        "[Agent] main model",
                        TerminalTone.Reasoning,
                        AgentDefaults.ResponsesApiCallTimeout,
                        ct => CreateResponseWithRetryAsync(
                            modelId: this.Model.Id,
                            input: currentInput,
                            lmStudioResponsesClient: responsesClient,
                            repoContext: perTurnRepoContext,
                            compactPrompt: lowContextMode,
                            cancellation: ct),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }

                    ThemedConsole.WriteLine(TerminalTone.Error, "[Agent] Main model request timed out.");
                    return await CompleteAndReturnAsync(
                        "Stopped: the model did not respond within the per-request time limit.",
                        "timed_out").ConfigureAwait(false);
                }
                catch (Exception ex) when (IsContextWindowExceeded(ex))
                {
                    contextResetAttempts++;
                    lowContextMode = true;
                    if (contextResetAttempts > MaxContextResetAttemptsPerTask)
                    {
                        return await CompleteAndReturnAsync(
                            "Stopped gracefully: context limit was exceeded repeatedly. " +
                            "Run again with a larger model context window or fewer tasks per run.",
                            "context_limit").ConfigureAwait(false);
                    }

                    // "Fresh agent + in-memory context": do not re-attach full repo context after overflow.
                    shouldRehydrateRepoContext = false;
                    currentInput = BuildResumeAfterContextResetInput(task, lastAssistantText, cumulativeToolOutputs);
                    ThemedConsole.WriteLine(
                        TerminalTone.Error,
                        $"[Agent] Context window exceeded. Resetting to compact in-memory state and retrying " +
                        $"({contextResetAttempts}/{MaxContextResetAttemptsPerTask}).");
                    continue;
                }

                string assistantText = assistantResponse.ResponseContentText?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(assistantText))
                {
                    return await CompleteAndReturnAsync("[Assistant returned empty output.]", "empty")
                        .ConfigureAwait(false);
                }
                lastAssistantText = assistantText;

                List<(string toolName, Dictionary<string, object?> args)> invocations;
                try
                {
                    invocations = await ParseToolInvocationsWithSmallModelAsync(
                        responsesClient,
                        task,
                        repoContext,
                        assistantText,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }

                    ThemedConsole.WriteLine(
                        TerminalTone.Error,
                        "[Agent] Tool-call parser model request timed out.");
                    return await CompleteAndReturnAsync(
                        "Stopped: the tool parser did not respond within the per-request time limit.",
                        "timed_out").ConfigureAwait(false);
                }

                if (invocations.Count == 0)
                {
                    await TryPersistAgentLoopTurnAsync(
                            taskRunId,
                            turn + 1,
                            assistantText,
                            Array.Empty<(string toolName, Dictionary<string, object?> args)>(),
                            Array.Empty<ToolExecutionEnvelope>(),
                            successfulToolExecutions,
                            contextResetAttempts,
                            cancellationToken)
                        .ConfigureAwait(false);

                    TaskCompletionAssessment assessment = await AssessTaskCompletionAsync(
                        responsesClient,
                        task,
                        assistantText,
                        cumulativeToolOutputs,
                        repoContext,
                        cancellationToken).ConfigureAwait(false);

                    if (assessment.IsComplete)
                    {
                        return await CompleteAndReturnAsync(assistantText, "completed")
                            .ConfigureAwait(false);
                    }

                    if (completionValidationRetriesUsed >= MaxCompletionValidationRetries)
                    {
                        return await CompleteAndReturnAsync(
                            assistantText +
                            "\n\n[Validation note] Task may be incomplete: " +
                            (string.IsNullOrWhiteSpace(assessment.Gap)
                                ? "No explicit completion evidence was found."
                                : assessment.Gap),
                            "incomplete").ConfigureAwait(false);
                    }

                    completionValidationRetriesUsed++;
                    shouldRehydrateRepoContext = true;
                    currentInput = BuildCompletionRetryInput(
                        task,
                        assistantText,
                        assessment,
                        cumulativeToolOutputs,
                        completionValidationRetriesUsed,
                        MaxCompletionValidationRetries);
                    continue;
                }

                invocations = invocations.Take(MaxToolCallsPerTurn).ToList();
                List<ToolExecutionEnvelope> toolOutputs = new List<ToolExecutionEnvelope>(invocations.Count);
                foreach (var (toolName, args) in invocations)
                {
                    string signature = BuildToolInvocationSignature(toolName, args);
                    Dictionary<string, object> toolResult;
                    if (!seenToolSignatures.Add(signature))
                    {
                        toolResult = new Dictionary<string, object>
                        {
                            { "skipped", true },
                            { "reason", "Duplicate tool call skipped for this task to prevent loops." },
                            { "tool_signature", signature }
                        };
                    }
                    else
                    {
                        toolResult = ToolKit.ExecuteToolInvocation(toolName, args);
                    }

                    toolResult = SanitizeToolResult(toolResult);
                    toolOutputs.Add(new ToolExecutionEnvelope
                    {
                        ToolName = toolName,
                        Args = args,
                        Result = toolResult
                    });
                }
                cumulativeToolOutputs.AddRange(toolOutputs);
                await TryPersistAgentLoopTurnAsync(
                        taskRunId,
                        turn + 1,
                        assistantText,
                        invocations,
                        toolOutputs,
                        successfulToolExecutions,
                        contextResetAttempts,
                        cancellationToken)
                    .ConfigureAwait(false);
                await TryPersistToolExecutionsAsync(taskRunId, turn + 1, toolOutputs, cancellationToken)
                    .ConfigureAwait(false);

                successfulToolExecutions += CountSuccessfulToolExecutions(toolOutputs);
                if (successfulToolExecutions > MaxSuccessfulToolExecutionsPerTask)
                {
                    return await CompleteAndReturnAsync(
                        "Stopped: tool execution budget for this task was reached. Summarize what you have so far " +
                        "and say what is still missing, without requesting more tools.",
                        "budget_exceeded").ConfigureAwait(false);
                }

                bool allSkippedThisTurn = toolOutputs.Count > 0 &&
                    toolOutputs.All(o => IsSkippedToolResult(o.Result));
                if (allSkippedThisTurn)
                {
                    consecutiveAllSkippedTurns++;
                }
                else
                {
                    consecutiveAllSkippedTurns = 0;
                }

                // Two consecutive "all duplicate" turns often means the model is re-emitting the same
                // tool: lines; require three to allow one recovery turn after a bad parse.
                if (consecutiveAllSkippedTurns >= 3)
                {
                    return await CompleteAndReturnAsync(
                        "Stopped: the model requested only duplicate tools already run this task. " +
                        "Use the tool JSON already returned in the conversation to answer, or state what is blocking you.",
                        "stalled").ConfigureAwait(false);
                }

                string progressSignature = BuildProgressSignature(toolOutputs);
                if (string.Equals(progressSignature, lastProgressSignature, StringComparison.Ordinal))
                {
                    noProgressTurns++;
                }
                else
                {
                    noProgressTurns = 0;
                }
                lastProgressSignature = progressSignature;
                if (noProgressTurns >= MaxNoProgressTurns)
                {
                    return await CompleteAndReturnAsync(
                        "Stopped early because tool execution stopped making progress (repeated duplicate/unchanged results).",
                        "stalled").ConfigureAwait(false);
                }

                currentInput = await BuildToolBridgeMessageAsync(
                    responsesClient,
                    task,
                    assistantText,
                    toolOutputs,
                    turnNumberOneBased: turn + 1,
                    successfulToolExecutions,
                    allSkippedThisTurn,
                    seenToolsBridgeHint: BuildSeenToolsBridgeHint(seenToolSignatures),
                    cancellationToken).ConfigureAwait(false);
            }

            return await CompleteAndReturnAsync(
                "Stopped after max turns while resolving tool calls.",
                "max_turns").ConfigureAwait(false);
        }

        private async Task TryPersistTaskRunStartAsync(
            string taskRunId,
            string taskText,
            string repoContext,
            long startedAtUnixMs,
            CancellationToken cancellationToken)
        {
            if (_convexService is null)
            {
                return;
            }

            string repoSummary = string.IsNullOrWhiteSpace(repoContext)
                ? ""
                : (repoContext.Length <= 2_000 ? repoContext : repoContext[..2_000] + "\n[...truncated...]");

            try
            {
                await _convexService.SaveTaskRunStartAsync(
                    new TaskRunRecord
                    {
                        TaskRunId = taskRunId,
                        ModelId = Model.Id,
                        TaskText = taskText,
                        Status = "running",
                        RepoContextSummary = repoSummary,
                        StartedAtUnixMs = startedAtUnixMs
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ThemedConsole.WriteLine(TerminalTone.Error, $"[Convex] Failed to persist task run start: {ex.Message}");
            }
        }

        private async Task TryPersistTaskRunCompletionAsync(
            string taskRunId,
            string status,
            string finalAssistantText,
            long completedAtUnixMs,
            CancellationToken cancellationToken)
        {
            if (_convexService is null)
            {
                return;
            }

            string trimmedFinalText = string.IsNullOrWhiteSpace(finalAssistantText)
                ? ""
                : (finalAssistantText.Length <= 6_000
                    ? finalAssistantText
                    : finalAssistantText[..6_000] + "\n[...truncated...]");

            try
            {
                await _convexService.CompleteTaskRunAsync(
                    new TaskRunRecord
                    {
                        TaskRunId = taskRunId,
                        ModelId = Model.Id,
                        TaskText = "",
                        Status = status,
                        RepoContextSummary = "",
                        StartedAtUnixMs = 0,
                        CompletedAtUnixMs = completedAtUnixMs,
                        FinalAssistantText = trimmedFinalText
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ThemedConsole.WriteLine(TerminalTone.Error, $"[Convex] Failed to persist task run completion: {ex.Message}");
            }
        }

        private async Task TryPersistAgentLoopTurnAsync(
            string taskRunId,
            int turnNumber,
            string assistantText,
            IReadOnlyList<(string toolName, Dictionary<string, object?> args)> invocations,
            IReadOnlyList<ToolExecutionEnvelope> toolOutputs,
            int successfulToolExecutionsSoFar,
            int contextResetCount,
            CancellationToken cancellationToken)
        {
            if (_convexService is null)
            {
                return;
            }

            try
            {
                string toolCallsJson = JsonSerializer.Serialize(invocations.Select(i => new
                {
                    toolName = i.toolName,
                    args = i.args
                }));
                string toolResultsJson = JsonSerializer.Serialize(toolOutputs.Select(o => new
                {
                    toolName = o.ToolName,
                    result = o.Result
                }));

                await _convexService.SaveAgentLoopTurnAsync(
                    new AgentLoopTurnRecord
                    {
                        TaskRunId = taskRunId,
                        TurnNumber = turnNumber,
                        AssistantText = assistantText,
                        ToolCallsJson = TruncateForPersistence(toolCallsJson, 8_000),
                        ToolResultsJson = TruncateForPersistence(toolResultsJson, 16_000),
                        SuccessfulToolExecutionsSoFar = successfulToolExecutionsSoFar,
                        ContextResetCount = contextResetCount,
                        CreatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ThemedConsole.WriteLine(TerminalTone.Error, $"[Convex] Failed to persist loop turn {turnNumber}: {ex.Message}");
            }
        }

        private async Task TryPersistToolExecutionsAsync(
            string taskRunId,
            int turnNumber,
            IReadOnlyList<ToolExecutionEnvelope> toolOutputs,
            CancellationToken cancellationToken)
        {
            if (_convexService is null || toolOutputs.Count == 0)
            {
                return;
            }

            foreach (ToolExecutionEnvelope output in toolOutputs)
            {
                bool isSuccess = IsSuccessfulToolResult(output.Result);
                try
                {
                    await _convexService.SaveToolExecutionAsync(
                        new ToolExecutionRecord
                        {
                            TaskRunId = taskRunId,
                            TurnNumber = turnNumber,
                            ToolName = output.ToolName,
                            ArgsJson = TruncateForPersistence(JsonSerializer.Serialize(output.Args), 8_000),
                            ResultJson = TruncateForPersistence(JsonSerializer.Serialize(output.Result), 16_000),
                            Ok = isSuccess,
                            CreatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                        },
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ThemedConsole.WriteLine(
                        TerminalTone.Error,
                        $"[Convex] Failed to persist tool execution {output.ToolName} (turn {turnNumber}): {ex.Message}");
                }
            }
        }

        private static string TruncateForPersistence(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
            {
                return text;
            }

            return text[..maxChars] + "\n[...truncated for persistence size...]";
        }

        private static async Task LlmProgressLoopAsync(
            TerminalTone tone,
            string messagePrefix,
            DateTime startUtc,
            TimeSpan maxDuration,
            CancellationToken telemetryStopToken,
            CancellationToken requestToken)
        {
            while (!telemetryStopToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(AgentDefaults.LlmTelemetryInterval, telemetryStopToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (requestToken.IsCancellationRequested)
                {
                    return;
                }

                double elapsed = (DateTime.UtcNow - startUtc).TotalSeconds;
                ThemedConsole.WriteLine(
                    tone,
                    $"{messagePrefix} still waiting for model… ({elapsed:F0}s / {maxDuration.TotalMinutes:F0}m max)");
            }
        }

        private async Task<T> WithPerCallTimeoutAndTelemetryAsync<T>(
            string messagePrefix,
            TerminalTone tone,
            TimeSpan perCallTimeout,
            Func<CancellationToken, Task<T>> operation,
            CancellationToken outerCancellation,
            bool logCompletionDuration = true)
        {
            using CancellationTokenSource timeoutCts = new(perCallTimeout);
            using CancellationTokenSource linkedCts = CancellationTokenSource
                .CreateLinkedTokenSource(outerCancellation, timeoutCts.Token);
            CancellationToken linked = linkedCts.Token;
            DateTime start = DateTime.UtcNow;
            ThemedConsole.WriteLine(tone, $"{messagePrefix} (max {perCallTimeout.TotalMinutes:F0}m)…");
            using CancellationTokenSource telemetryCts = new();
            Task telemetryTask = Task.Run(
                () => LlmProgressLoopAsync(
                    tone, messagePrefix, start, perCallTimeout, telemetryCts.Token, linked),
                CancellationToken.None);
            try
            {
                T result = await operation(linked).ConfigureAwait(false);
                if (logCompletionDuration)
                {
                    ThemedConsole.WriteLine(
                        tone,
                        $"{messagePrefix} completed in {(DateTime.UtcNow - start).TotalSeconds:F1}s");
                }

                return result;
            }
            finally
            {
                telemetryCts.Cancel();
                try
                {
                    await telemetryTask.ConfigureAwait(false);
                }
                catch
                {
                }
            }
        }

        private static string BuildSeenToolsBridgeHint(HashSet<string> seenToolSignatures)
        {
            if (seenToolSignatures.Count == 0)
            {
                return "";
            }

            var webUrls = new List<string>();
            const string webPrefix = "WEB_CALL::";
            foreach (string s in seenToolSignatures)
            {
                if (s.Length > webPrefix.Length && s.StartsWith(webPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    webUrls.Add(s[webPrefix.Length..]);
                }
            }

            if (webUrls.Count == 0)
            {
                return "";
            }

            return "Already-fetched WEB_CALL URLs (do not request these again with tool:): " +
                string.Join("; ", webUrls);
        }

        private static string BuildToolInvocationSignature(string toolName, Dictionary<string, object?> args)
        {
            string normalizedTool = (toolName ?? "").Trim().ToUpperInvariant();
            string keyMaterial;
            if (normalizedTool == AgentDefaults.WEB_CALL_TOOL_NAME && TryGetArgAsString(args, out string? url, "url"))
            {
                keyMaterial = (url ?? "").Trim();
            }
            else if (normalizedTool == AgentDefaults.BASH_TOOL_NAME && TryGetArgAsString(args, out string? command, "command"))
            {
                keyMaterial = (command ?? "").Trim();
            }
            else
            {
                keyMaterial = JsonSerializer.Serialize(args);
            }

            return normalizedTool + "::" + keyMaterial;
        }

        private static bool TryGetArgAsString(
            Dictionary<string, object?> args,
            out string? value,
            params string[] keys)
        {
            foreach (string key in keys)
            {
                if (!args.TryGetValue(key, out object? raw) || raw is null)
                {
                    continue;
                }

                if (raw is JsonElement json)
                {
                    value = json.ValueKind == JsonValueKind.String ? json.GetString() : json.ToString();
                    return true;
                }

                value = raw.ToString();
                return true;
            }

            value = null;
            return false;
        }

        private static Dictionary<string, object> SanitizeToolResult(Dictionary<string, object> result)
        {
            Dictionary<string, object> sanitized = new(result.Count, StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, object> kvp in result)
            {
                if (kvp.Value is string s && s.Length > MaxToolStringValueChars)
                {
                    sanitized[kvp.Key] = s[..MaxToolStringValueChars] + "\n[...truncated...]";
                }
                else
                {
                    sanitized[kvp.Key] = kvp.Value;
                }
            }

            return sanitized;
        }

        private static string BuildProgressSignature(IReadOnlyList<ToolExecutionEnvelope> outputs)
        {
            StringBuilder sb = new StringBuilder();
            foreach (ToolExecutionEnvelope output in outputs)
            {
                // Include the requested command/URL/path material so distinct WEB/BASH calls never
                // look "stuck" when sanitized HTTP bodies or stdout prefixes collide.
                sb.Append(BuildToolInvocationSignature(output.ToolName, output.Args)).Append('|');
                if (output.Result.TryGetValue("error", out object? error) && error is not null)
                {
                    sb.Append("ERR:").Append(error.ToString());
                }
                else if (output.Result.TryGetValue("skipped", out object? skipped) && skipped is not null)
                {
                    sb.Append("SKIP:").Append(skipped.ToString());
                }
                else
                {
                    // Success was previously a bare "OK", so every successful BASH/WEB_CALL looked identical
                    // across turns and tripped MaxNoProgressTurns even when stdout/body changed.
                    sb.Append("OK:").Append(FingerprintSuccessfulToolResult(output.Result));
                }
                sb.Append(';');
            }
            return sb.ToString();
        }

        /// <summary>
        /// Stable, compact fingerprint of tool output so distinct successful runs do not look "stuck".
        /// </summary>
        private static string FingerprintSuccessfulToolResult(Dictionary<string, object> result)
        {
            IEnumerable<string> keys = result.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase);
            StringBuilder canonical = new StringBuilder();
            foreach (string key in keys)
            {
                if (!result.TryGetValue(key, out object? val) || val is null)
                {
                    continue;
                }

                string s = val.ToString() ?? "";
                if (s.Length > 4096)
                {
                    s = s[..4096] + $"\n[truncated; totalChars={val.ToString()!.Length}]";
                }

                canonical.Append(key).Append('=').Append(s).Append('\n');
            }

            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
            return Convert.ToHexString(hash.AsSpan(0, 8));
        }

        private async Task<List<(string toolName, Dictionary<string, object?> args)>> ParseToolInvocationsWithSmallModelAsync(
            ResponsesClient responsesClient,
            string task,
            string repoContext,
            string assistantText,
            CancellationToken cancellationToken)
        {
            // Source of truth: explicit `tool:` lines from assistant output.
            List<(string toolName, Dictionary<string, object?> args)> deterministicCalls =
                ToolKit.ExtractToolCallInvocations(assistantText);
            if (deterministicCalls.Count > 0)
            {
                return deterministicCalls;
            }

            StringBuilder parserPrompt = new StringBuilder();
            parserPrompt.AppendLine("You are a tool-call parser.");
            parserPrompt.AppendLine("Extract tool calls from the assistant text.");
            parserPrompt.AppendLine("Return ONLY JSON in this schema:");
            parserPrompt.AppendLine("{\"calls\":[{\"toolName\":\"NAME\",\"args\":{}}]}");
            parserPrompt.AppendLine("If no tool call is present return: {\"calls\":[]}");
            parserPrompt.AppendLine("Allowed tool names:");
            parserPrompt.AppendLine(AgentDefaults.BASH_TOOL_NAME);
            parserPrompt.AppendLine(AgentDefaults.WEB_CALL_TOOL_NAME);
            parserPrompt.AppendLine(AgentDefaults.READ_TOOL_NAME);
            parserPrompt.AppendLine(AgentDefaults.LIST_FILE_TOOL_NAME);
            parserPrompt.AppendLine(AgentDefaults.EDIT_FILE_TOOL_NAME);
            parserPrompt.AppendLine();
            parserPrompt.AppendLine("Assistant text:");
            parserPrompt.AppendLine(assistantText);

            CreateResponseResult parsed = await WithPerCallTimeoutAndTelemetryAsync(
                "[Agent] tool-call parser",
                TerminalTone.Reasoning,
                AgentDefaults.ResponsesApiCallTimeout,
                ct => CreateResponseWithRetryAsync(
                    modelId: DefaultToolInvocationHandlerModel,
                    input: parserPrompt.ToString(),
                    lmStudioResponsesClient: responsesClient,
                    repoContext: repoContext,
                    compactPrompt: false,
                    cancellation: ct),
                cancellationToken).ConfigureAwait(false);

            string json = parsed.ResponseContentText?.Trim() ?? "";
            List<(string toolName, Dictionary<string, object?> args)> parsedCalls =
                TryParseToolCallsFromJson(json);

            if (parsedCalls.Count == 0)
            {
                return parsedCalls;
            }

            // Guardrail: parser-model calls must be grounded in assistant output.
            // This prevents tool hallucinations (e.g. injecting unrelated BASH calls).
            HashSet<string> declaredTools = ExtractDeclaredToolNamesFromAssistantText(assistantText);
            return parsedCalls
                .Where(call => IsParserCallGroundedInAssistantText(call, assistantText, declaredTools))
                .ToList();
        }

        private static HashSet<string> ExtractDeclaredToolNamesFromAssistantText(string assistantText)
        {
            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(assistantText))
            {
                return names;
            }

            string[] lines = assistantText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (!line.StartsWith("tool:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string after = line["tool:".Length..].Trim();
                int openParen = after.IndexOf('(');
                if (openParen <= 0)
                {
                    continue;
                }

                string toolName = after[..openParen].Trim();
                if (!string.IsNullOrWhiteSpace(toolName))
                {
                    names.Add(toolName);
                }
            }

            return names;
        }

        private static bool IsParserCallGroundedInAssistantText(
            (string toolName, Dictionary<string, object?> args) call,
            string assistantText,
            HashSet<string> declaredTools)
        {
            if (string.IsNullOrWhiteSpace(call.toolName))
            {
                return false;
            }

            if (!declaredTools.Contains(call.toolName))
            {
                return false;
            }

            // Ensure at least one arg key from parser output appears in assistant text.
            // This allows parser repair for malformed JSON while rejecting fabricated calls.
            if (call.args.Count == 0)
            {
                return true;
            }

            foreach (string key in call.args.Keys)
            {
                if (assistantText.Contains($"\"{key}\"", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<(string toolName, Dictionary<string, object?> args)> TryParseToolCallsFromJson(string payload)
        {
            var result = new List<(string toolName, Dictionary<string, object?> args)>();
            if (string.IsNullOrWhiteSpace(payload))
            {
                return result;
            }

            string json = payload.Trim();
            if (json.StartsWith("```", StringComparison.Ordinal))
            {
                int firstNl = json.IndexOf('\n');
                if (firstNl >= 0)
                {
                    json = json[(firstNl + 1)..];
                }
                int fence = json.LastIndexOf("```", StringComparison.Ordinal);
                if (fence >= 0)
                {
                    json = json[..fence];
                }
                json = json.Trim();
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("calls", out JsonElement calls) ||
                    calls.ValueKind != JsonValueKind.Array)
                {
                    return result;
                }

                foreach (JsonElement call in calls.EnumerateArray())
                {
                    if (!call.TryGetProperty("toolName", out JsonElement nameEl))
                    {
                        continue;
                    }

                    string? toolName = nameEl.GetString();
                    if (string.IsNullOrWhiteSpace(toolName))
                    {
                        continue;
                    }

                    Dictionary<string, object?> args = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    if (call.TryGetProperty("args", out JsonElement argsEl) &&
                        argsEl.ValueKind == JsonValueKind.Object)
                    {
                        foreach (JsonProperty prop in argsEl.EnumerateObject())
                        {
                            args[prop.Name] = prop.Value.Clone();
                        }
                    }

                    result.Add((toolName, args));
                }
            }
            catch
            {
                return new List<(string toolName, Dictionary<string, object?> args)>();
            }

            return result;
        }

        private Task<string> BuildToolBridgeMessageAsync(
            ResponsesClient responsesClient,
            string task,
            string assistantText,
            IReadOnlyList<ToolExecutionEnvelope> toolOutputs,
            int turnNumberOneBased,
            int successfulToolExecutions,
            bool allSkippedThisTurn,
            string? seenToolsBridgeHint,
            CancellationToken cancellationToken)
        {
            _ = responsesClient;
            _ = cancellationToken;
            string outputsJson = JsonSerializer.Serialize(toolOutputs);
            if (outputsJson.Length > MaxSerializedToolOutputChars)
            {
                outputsJson = outputsJson[..MaxSerializedToolOutputChars] + "\n[...truncated for context window safety...]";
            }

            // Avoid lossy bridge-model summarization. Feed the original task + exact tool outputs directly
            // so the main model can continue deterministically from the same objective.
            StringBuilder nextInput = new StringBuilder();
            nextInput.AppendLine("Continue the same user task using the tool results below.");
            nextInput.AppendLine("Do not ask for the task again.");
            nextInput.AppendLine();
            nextInput.AppendLine(
                $"Tool-loop turn {turnNumberOneBased}. Non-skipped tool runs so far this task: {successfulToolExecutions} " +
                $"(budget {MaxSuccessfulToolExecutionsPerTask}).");
            nextInput.AppendLine("Be frugal: prefer one well-chosen tool per turn. Do not repeat listing the repo, " +
                "re-read the same file, or issue overlapping find/dir/Get-ChildItem variants for the same purpose.");
            nextInput.AppendLine("For WEB_CALL: if you already fetched a URL in this task, do not request it again; " +
                "answer from the JSON you have, or fetch a *different* URL only if the first page truly lacks the answer.");
            if (!string.IsNullOrWhiteSpace(seenToolsBridgeHint))
            {
                nextInput.AppendLine(seenToolsBridgeHint);
            }
            if (successfulToolExecutions >= 10)
            {
                nextInput.AppendLine("You have already run many tools; strongly prefer a final answer now unless one specific fact is still missing.");
            }

            if (allSkippedThisTurn)
            {
                nextInput.AppendLine();
                nextInput.AppendLine(
                    "IMPORTANT: Every tool call in this round was skipped as a duplicate of an earlier run. " +
                    "Your **next** message must be a normal answer (no tool: lines) using the tool results below, " +
                    "unless you need a genuinely *new* URL or command you have not used before this task.");
            }

            nextInput.AppendLine();
            nextInput.AppendLine("Original task:");
            nextInput.AppendLine(task);
            nextInput.AppendLine();
            nextInput.AppendLine("Your previous response:");
            nextInput.AppendLine(assistantText);
            nextInput.AppendLine();
            nextInput.AppendLine("Tool execution results (JSON):");
            nextInput.AppendLine(outputsJson);
            nextInput.AppendLine();
            nextInput.AppendLine("If the task can be answered from the JSON above, reply with a normal answer (no tool: line).");
            nextInput.AppendLine("If one more distinct tool is truly required, emit a single tool: line (at most two tools only if both are necessary).");
            return Task.FromResult(nextInput.ToString());
        }

        private static int CountSuccessfulToolExecutions(IReadOnlyList<ToolExecutionEnvelope> outputs)
        {
            int n = 0;
            foreach (ToolExecutionEnvelope o in outputs)
            {
                if (!IsSkippedToolResult(o.Result))
                {
                    n++;
                }
            }

            return n;
        }

        private static bool IsSkippedToolResult(Dictionary<string, object> result) =>
            result.TryGetValue("skipped", out object? v) && v is true;

        private static string BuildResumeAfterContextResetInput(
            string originalTask,
            string lastAssistantText,
            IReadOnlyList<ToolExecutionEnvelope> toolOutputs)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("We exceeded the model context window and manually reset the conversation.");
            sb.AppendLine("Treat this as the first turn with repository context reattached.");
            sb.AppendLine("Resume and complete the original task using the latest tool evidence below.");
            sb.AppendLine();
            sb.AppendLine("Original task:");
            sb.AppendLine(originalTask);
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(lastAssistantText))
            {
                sb.AppendLine("Last assistant output before reset:");
                sb.AppendLine(lastAssistantText);
                sb.AppendLine();
            }

            if (toolOutputs.Count > 0)
            {
                string compactToolState = JsonSerializer.Serialize(toolOutputs);
                if (compactToolState.Length > 2_500)
                {
                    compactToolState = compactToolState[..2_500] + "\n[...truncated for compact context mode...]";
                }

                sb.AppendLine("Most recent tool results:");
                sb.AppendLine(compactToolState);
            }

            return sb.ToString();
        }

        private static string FormatExceptionDetail(Exception ex)
        {
            var sb = new StringBuilder();
            int depth = 0;
            for (Exception? e = ex; e is not null; e = e.InnerException)
            {
                if (depth++ > 0)
                    sb.AppendLine("--- Inner exception ---");
                sb.AppendLine($"{e.GetType().FullName}: {e.Message}");
            }

            if (!string.IsNullOrWhiteSpace(ex.StackTrace))
            {
                sb.AppendLine("Stack trace:");
                sb.AppendLine(ex.StackTrace);
            }

            return sb.ToString().TrimEnd();
        }

        private sealed class TaskCompletionAssessment
        {
            public bool IsComplete { get; set; }
            public string Gap { get; set; } = "";
            public string NextActionHint { get; set; } = "";
        }

        private sealed class EvidenceState
        {
            public bool AnyToolAttempted { get; set; }
            public bool AnyToolSucceeded { get; set; }

            public bool FileWriteAttempted { get; set; }
            public bool FileWriteSucceeded { get; set; }
            public bool FileReadSucceeded { get; set; }

            public bool ServiceActionAttempted { get; set; }
            public bool ServiceActionSucceeded { get; set; }
            public bool ServiceStatusSucceeded { get; set; }

            public bool NeedsVerification { get; set; }
            public bool Verified { get; set; }
            public string Gap { get; set; } = "";
            public string NextActionHint { get; set; } = "";
            public string Summary { get; set; } = "";
        }

        private async Task<TaskCompletionAssessment> AssessTaskCompletionAsync(
            ResponsesClient responsesClient,
            string task,
            string assistantAnswer,
            IReadOnlyList<ToolExecutionEnvelope> latestToolOutputs,
            string repoContext,
            CancellationToken cancellationToken)
        {
            EvidenceState evidence = AnalyzeEvidenceState(latestToolOutputs);
            if (evidence.NeedsVerification && !evidence.Verified)
            {
                return new TaskCompletionAssessment
                {
                    IsComplete = false,
                    Gap = evidence.Gap,
                    NextActionHint = evidence.NextActionHint
                };
            }

            string outputsJson = JsonSerializer.Serialize(latestToolOutputs);
            if (outputsJson.Length > 8_000)
            {
                outputsJson = outputsJson[..8_000] + "\n[...truncated for validation safety...]";
            }

            StringBuilder validatorPrompt = new StringBuilder();
            validatorPrompt.AppendLine("You are a strict task-completion validator.");
            validatorPrompt.AppendLine("Decide whether the assistant actually completed the user's task intent.");
            validatorPrompt.AppendLine("Use the task, assistant answer, and tool evidence.");
            validatorPrompt.AppendLine("Return ONLY valid JSON with this exact schema:");
            validatorPrompt.AppendLine("{\"isComplete\":true|false,\"gap\":\"...\",\"nextActionHint\":\"...\"}");
            validatorPrompt.AppendLine("Rules:");
            validatorPrompt.AppendLine("- Mark false if commands failed (non-zero exits) and success wasn't later proven.");
            validatorPrompt.AppendLine("- Mark false if the answer claims completion without concrete evidence.");
            validatorPrompt.AppendLine("- For file creation/edit tasks, require evidence that file exists or was read after write.");
            validatorPrompt.AppendLine("- For service/container tasks, require evidence service is up/healthy.");
            validatorPrompt.AppendLine("- Keep gap and nextActionHint short and actionable.");
            validatorPrompt.AppendLine();
            validatorPrompt.AppendLine("Task:");
            validatorPrompt.AppendLine(task);
            validatorPrompt.AppendLine();
            validatorPrompt.AppendLine("Assistant answer:");
            validatorPrompt.AppendLine(assistantAnswer);
            validatorPrompt.AppendLine();
            validatorPrompt.AppendLine("Deterministic evidence summary:");
            validatorPrompt.AppendLine(evidence.Summary);
            validatorPrompt.AppendLine();
            validatorPrompt.AppendLine("Latest tool outputs JSON:");
            validatorPrompt.AppendLine(outputsJson);

            try
            {
                CreateResponseResult parsed = await WithPerCallTimeoutAndTelemetryAsync(
                    "[Agent] completion validator",
                    TerminalTone.Reasoning,
                    AgentDefaults.ResponsesApiCallTimeout,
                    ct => CreateResponseWithRetryAsync(
                        modelId: DefaultToolInvocationHandlerModel,
                        input: validatorPrompt.ToString(),
                        lmStudioResponsesClient: responsesClient,
                        repoContext: repoContext,
                        compactPrompt: false,
                        cancellation: ct),
                    cancellationToken).ConfigureAwait(false);

                string json = parsed.ResponseContentText?.Trim() ?? "";
                if (json.StartsWith("```", StringComparison.Ordinal))
                {
                    int firstNl = json.IndexOf('\n');
                    if (firstNl >= 0)
                    {
                        json = json[(firstNl + 1)..];
                    }

                    int fence = json.LastIndexOf("```", StringComparison.Ordinal);
                    if (fence >= 0)
                    {
                        json = json[..fence];
                    }

                    json = json.Trim();
                }

                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                bool isComplete = root.TryGetProperty("isComplete", out JsonElement completeEl) &&
                    completeEl.ValueKind == JsonValueKind.True;
                string gap = root.TryGetProperty("gap", out JsonElement gapEl)
                    ? (gapEl.ToString() ?? "")
                    : "";
                string next = root.TryGetProperty("nextActionHint", out JsonElement nextEl)
                    ? (nextEl.ToString() ?? "")
                    : "";

                return new TaskCompletionAssessment
                {
                    IsComplete = isComplete,
                    Gap = gap.Trim(),
                    NextActionHint = next.Trim()
                };
            }
            catch
            {
                return new TaskCompletionAssessment
                {
                    IsComplete = !evidence.NeedsVerification || evidence.Verified,
                    Gap = string.IsNullOrWhiteSpace(evidence.Gap)
                        ? "Validator response was malformed and completion could not be confirmed."
                        : evidence.Gap,
                    NextActionHint = string.IsNullOrWhiteSpace(evidence.NextActionHint)
                        ? "Run one concrete verification command and report result."
                        : evidence.NextActionHint
                };
            }
        }

        private static EvidenceState AnalyzeEvidenceState(IReadOnlyList<ToolExecutionEnvelope> outputs)
        {
            EvidenceState state = new();
            foreach (ToolExecutionEnvelope output in outputs)
            {
                if (IsSkippedToolResult(output.Result))
                {
                    continue;
                }

                state.AnyToolAttempted = true;
                bool success = IsSuccessfulToolResult(output.Result);
                if (success)
                {
                    state.AnyToolSucceeded = true;
                }

                string command = TryGetToolCommand(output);
                if (string.IsNullOrWhiteSpace(command))
                {
                    continue;
                }

                if (LooksLikeFileWriteCommand(command))
                {
                    state.FileWriteAttempted = true;
                    if (success)
                    {
                        state.FileWriteSucceeded = true;
                    }
                }

                if (LooksLikeFileReadCommand(command) && success)
                {
                    state.FileReadSucceeded = true;
                }

                if (LooksLikeServiceActionCommand(command))
                {
                    state.ServiceActionAttempted = true;
                    if (success)
                    {
                        state.ServiceActionSucceeded = true;
                    }
                }

                if (LooksLikeServiceStatusCommand(command) && success)
                {
                    state.ServiceStatusSucceeded = true;
                }
            }

            if (state.ServiceActionAttempted)
            {
                state.NeedsVerification = true;
                state.Verified = state.ServiceActionSucceeded && state.ServiceStatusSucceeded;
                if (!state.Verified)
                {
                    state.Gap = "Service action was attempted but running/healthy status was not verified.";
                    state.NextActionHint = "Run one status/health command for the same service and report successful output.";
                }
            }
            else if (state.FileWriteAttempted)
            {
                state.NeedsVerification = true;
                state.Verified = state.FileWriteSucceeded && state.FileReadSucceeded;
                if (!state.Verified)
                {
                    state.Gap = "File write was attempted but existence/content verification evidence is missing.";
                    state.NextActionHint = "Read back the written file path and confirm expected content.";
                }
            }
            else
            {
                // Generic command tasks still need at least one successful run if tools were used.
                state.NeedsVerification = state.AnyToolAttempted;
                state.Verified = !state.AnyToolAttempted || state.AnyToolSucceeded;
                if (state.AnyToolAttempted && !state.AnyToolSucceeded)
                {
                    state.Gap = "Tools were executed but none completed successfully.";
                    state.NextActionHint = "Fix the failing command and rerun one successful verification command.";
                }
            }

            state.Summary = BuildEvidenceSummary(state);
            return state;
        }

        private static string BuildEvidenceSummary(EvidenceState state)
        {
            return $"anyAttempted={state.AnyToolAttempted}; anySucceeded={state.AnyToolSucceeded}; " +
                $"fileWriteAttempted={state.FileWriteAttempted}; fileWriteSucceeded={state.FileWriteSucceeded}; fileReadSucceeded={state.FileReadSucceeded}; " +
                $"serviceActionAttempted={state.ServiceActionAttempted}; serviceActionSucceeded={state.ServiceActionSucceeded}; serviceStatusSucceeded={state.ServiceStatusSucceeded}; " +
                $"needsVerification={state.NeedsVerification}; verified={state.Verified}; " +
                $"gap='{state.Gap}'; next='{state.NextActionHint}'";
        }

        private static bool IsSuccessfulToolResult(Dictionary<string, object> result)
        {
            if (result.TryGetValue("ok", out object? okObj) &&
                bool.TryParse(okObj?.ToString(), out bool okParsed))
            {
                return okParsed;
            }

            if (result.TryGetValue("exit_code", out object? exitObj) &&
                int.TryParse(exitObj?.ToString(), out int exitCode))
            {
                return exitCode == 0;
            }

            return !result.ContainsKey("error");
        }

        private static string TryGetToolCommand(ToolExecutionEnvelope output)
        {
            if (output.Result.TryGetValue("command", out object? cmdObj) &&
                !string.IsNullOrWhiteSpace(cmdObj?.ToString()))
            {
                return cmdObj!.ToString()!;
            }

            if (output.Args.TryGetValue("command", out object? argCmdObj))
            {
                if (argCmdObj is JsonElement je)
                {
                    return je.ValueKind == JsonValueKind.String ? (je.GetString() ?? "") : je.ToString();
                }
                return argCmdObj?.ToString() ?? "";
            }

            return "";
        }

        private static bool LooksLikeServiceActionCommand(string command) =>
            Regex.IsMatch(command, @"\b(docker\s+compose|docker\s+container|docker\s+service|kubectl|systemctl|service)\b", RegexOptions.IgnoreCase) &&
            Regex.IsMatch(command, @"\b(up|start|stop|restart|down|exec)\b", RegexOptions.IgnoreCase);

        private static bool LooksLikeServiceStatusCommand(string command) =>
            Regex.IsMatch(command, @"\bpg_isready\b", RegexOptions.IgnoreCase) ||
            (Regex.IsMatch(command, @"\b(docker\s+compose|docker\s+container|docker\s+service|kubectl|systemctl|service)\b", RegexOptions.IgnoreCase) &&
             Regex.IsMatch(command, @"\b(ps|status|logs|inspect|get|describe|health|ready)\b", RegexOptions.IgnoreCase));

        private static bool LooksLikeFileWriteCommand(string command) =>
            Regex.IsMatch(command, @"(>\s*\S+|>>\s*\S+|\b(Set-Content|Add-Content|Out-File|New-Item\s+-ItemType\s+File|tee|touch|cp|mv)\b)", RegexOptions.IgnoreCase);

        private static bool LooksLikeFileReadCommand(string command) =>
            Regex.IsMatch(command, @"\b(Get-Content|cat|type|less|more|head|tail|ls|dir|Get-ChildItem|Test-Path|stat)\b", RegexOptions.IgnoreCase);

        private static string BuildCompletionRetryInput(
            string originalTask,
            string lastAssistantText,
            TaskCompletionAssessment assessment,
            IReadOnlyList<ToolExecutionEnvelope> toolOutputs,
            int retryNumber,
            int maxRetries)
        {
            string outputsJson = JsonSerializer.Serialize(toolOutputs);
            if (outputsJson.Length > 8_000)
            {
                outputsJson = outputsJson[..8_000] + "\n[...truncated for context window safety...]";
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Your prior answer was judged likely incomplete for the current task intent.");
            sb.AppendLine($"Validation retry {retryNumber}/{maxRetries}.");
            if (!string.IsNullOrWhiteSpace(assessment.Gap))
            {
                sb.AppendLine("Gap to fix: " + assessment.Gap);
            }

            if (!string.IsNullOrWhiteSpace(assessment.NextActionHint))
            {
                sb.AppendLine("Suggested next action: " + assessment.NextActionHint);
            }

            sb.AppendLine("Do not restate assumptions. Verify completion with concrete evidence.");
            sb.AppendLine("If tools are needed, emit one precise tool: line.");
            sb.AppendLine("If complete, provide a short final answer that cites the evidence.");
            sb.AppendLine();
            sb.AppendLine("Original task:");
            sb.AppendLine(originalTask);
            sb.AppendLine();
            sb.AppendLine("Previous assistant answer:");
            sb.AppendLine(lastAssistantText);
            sb.AppendLine();
            sb.AppendLine("Most recent tool outputs JSON:");
            sb.AppendLine(outputsJson);
            return sb.ToString();
        }

        private static bool IsContextWindowExceeded(Exception ex)
        {
            for (Exception? current = ex; current is not null; current = current.InnerException)
            {
                if (TextIndicatesLlmContextLimit(current.Message))
                {
                    return true;
                }
            }

            if (ex is ClientResultException cre
                && TextIndicatesLlmContextLimit(TryGetClientErrorBodyString(cre)))
            {
                return true;
            }

            return false;
        }

        private static bool TextIndicatesLlmContextLimit(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            return text.Contains("n_keep", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("n_ctx", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("context length", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("context window", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("token limit", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("larger context", StringComparison.OrdinalIgnoreCase);
        }

        private static string? TryGetClientErrorBodyString(ClientResultException ex)
        {
            try
            {
                PipelineResponse? raw = ex.GetRawResponse();
                raw?.BufferContent();
                if (raw?.Content is { Length: > 0 } bd)
                {
                    return bd.ToString();
                }
            }
            catch
            {
            }

            return null;
        }

        const int ContextualizerMaxListLines = 2500;
        const int ContextualizerMaxFilesToRead = 15;
        const int ContextualizerMaxCharsPerFile = 48_000;
        /// <summary>Pick step: huge repo lists can exceed local server limits.</summary>
        const int ContextualizerMaxPickUserChars = 120_000;
        /// <summary>Per-file summary: the full user message (path + file body) for /v1/chat/completions.
        /// Kept moderate so system + user stay under small local n_ctx (e.g. 4096).</summary>
        const int ContextualizerMaxSummaryUserChars = 4_000;
        /// <summary>Merge step: combined per-file notes.</summary>
        const int ContextualizerMaxMergeUserChars = 12_000;
        /// <summary>When the server says n_keep/n_ctx exceeded, force the next attempt this small (chars in user only).</summary>
        const int ContextualizerNCtxRetryTargetUserChars = 2_200;
        const int ContextualizerHttpRetryMaxAttempts = 6;
        const int ContextualizerHttpRetryMinUserChars = 500;
        static readonly JsonSerializerOptions s_contextualizerJson = new() 
        { PropertyNameCaseInsensitive = true };

        private async Task<string> ContextualizeAgentAsync(
            string? userTask, 
            CancellationToken cancellationToken)
        {
            if (_contextualizerChatClient is null)
            {
                ThemedConsole.WriteLine(TerminalTone.Error,
                    "[Contextualizer] Chat client is null; contextualization is disabled.");
                return "";
            }

            IReadOnlyList<string> allRelPaths = AgentUtilities.ListWorkspaceSourceFilesRelative();
            string workspaceRoot = AgentUtilities.ResolveWorkspaceRoot();
            ThemedConsole.WriteLine(TerminalTone.Reasoning,
                $"[Contextualizer] Workspace root: {workspaceRoot}; indexed source files: {allRelPaths.Count}");

            if (allRelPaths.Count == 0)
            {
                ThemedConsole.WriteLine(TerminalTone.Error,
                    "[Contextualizer] No workspace source files found (empty listing). " +
                    "Check HARNESS_WORKSPACE_ROOT / working directory and exclude rules in AgentUtilities.");
                return "";
            }

            string listBlock = AgentUtilities.BuildFileListUserBlock(allRelPaths, userTask);

            string pickRelevantFilesUserMessage = 
                "You choose which source files matter for understanding this repository." +
                " Reply with a single JSON object only, no markdown, no explanation. " +
                " Schema: {\"paths\":[\"relative/path.ext\",...]} with at most 15 paths." +
                " CRITICAL: Every string in \"paths\" MUST be copied EXACTLY from the file list below " +
                "(including folder names). Do not reference README.md, LICENSE, or other common names" +
                " unless that exact line appears in the list — many projects omit them, and" +
                " some folders (e.g. third-party) are not listed." +
                " Prefer: entries near the top of the list (workspace manifests, app entry points, config, and core source files)," +
                " and paths most relevant to the user's task. " +
                "Favor a smaller, high-signal set (e.g. project file, program entry, core abstractions) over" +
                " long lists of similar files. Use forward slashes as in the list.";

            string summarizeFileUserMessage = 
                "Summarize the important information in this file for a coding assistant. Focus on facts," +
                " not opinions. If the file contains code, summarize what the code does." +
                " If the file contains text, summarize the key points." +
                " Keep it concise; a few lines at most. No preamble.";

            string mergeContextTogetherUserMessage = 
                "Merge the following per-file notes into one concise repository overview for " +
                "the main coding assistant. Keep facts only; max ~20 lines. No preamble.";

            var pickOptions = new ChatCompletionOptions { Temperature = 0.2f, MaxOutputTokenCount = 1024 };
            string pickRaw = await CompleteContextualizerChatWithRetriesAsync(
                pickRelevantFilesUserMessage,
                listBlock,
                pickOptions,
                cancellationToken,
                "pick (choose files)",
                ContextualizerMaxPickUserChars).ConfigureAwait(false);

            ThemedConsole.WriteLine(TerminalTone.Reasoning,
                $"[Contextualizer] Pick-step raw length={pickRaw?.Length ?? 0}. " +
                $"Preview: {AgentUtilities.TruncateForLog(pickRaw ?? "", 500)}");

            List<string> picked = AgentUtilities.ParsePickedPaths(
                pickRaw ?? "",
                workspaceRoot,
                allRelPaths,
                out string? pickDiagnostic);
            if (picked.Count == 0)
            {
                if (!string.IsNullOrEmpty(pickDiagnostic))
                    ThemedConsole.WriteLine(TerminalTone.Error, "[Contextualizer] " + pickDiagnostic);
                List<string> fallbackPicks = AgentUtilities.GetContextualizerFallbackPicks(allRelPaths);
                if (fallbackPicks.Count > 0)
                {
                    ThemedConsole.WriteLine(TerminalTone.Reasoning,
                        "[Contextualizer] Model pick failed; using fallback entry-point files: " +
                        string.Join(", ", fallbackPicks));
                    picked = fallbackPicks;
                }
                else
                {
                    ThemedConsole.WriteLine(TerminalTone.Error,
                        "[Contextualizer] Path pick produced zero usable files and no fallback candidates exist.");
                    return "";
                }
            }

            ThemedConsole.WriteLine(TerminalTone.Reasoning,
                $"[Contextualizer] Picked {picked.Count} file(s): {string.Join(", ", picked)}");

            var perFileSummaries = new List<string>(picked.Count);
            foreach (string rel in picked)
            {
                cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable CS0618
                Dictionary<string, object> readResult = ToolKit.ReadFile_Tool
                    (AgentUtilities.ResolveAbsPath(rel));
#pragma warning restore CS0618

                if (!readResult.TryGetValue("content", out object? contentObj) || contentObj is null)
                {
                    ThemedConsole.WriteLine(TerminalTone.Error,
                        $"[Contextualizer] ReadFile_Tool returned no content for '{rel}' (keys: {string.Join(", ", readResult.Keys)}).");
                    continue;
                }

                string content = contentObj.ToString() ?? "";
                if (content.Length > ContextualizerMaxCharsPerFile)
                {
                content = content[..ContextualizerMaxCharsPerFile] + "\n\n[... truncated ...]";
                }

                string user = "File: " + rel + "\n\n" + content;
                user = AgentUtilities.TruncateMiddleForModel(user, ContextualizerMaxSummaryUserChars);
                string summary = await CompleteContextualizerChatWithRetriesAsync(
                    summarizeFileUserMessage,
                    user,
                    new ChatCompletionOptions { Temperature = 0.35f, MaxOutputTokenCount = 768 },
                    cancellationToken,
                    $"summarize: {rel}",
                    ContextualizerMaxSummaryUserChars).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(summary))
                {
                    ThemedConsole.WriteLine(TerminalTone.Error,
                        $"[Contextualizer] Empty summary for '{rel}' after summarize step.");
                }
                else
                {
                    perFileSummaries.Add($"### {rel}\n{summary.Trim()}");
                }
            }

            if (perFileSummaries.Count == 0)
            {
                ThemedConsole.WriteLine(TerminalTone.Error,
                    "[Contextualizer] No per-file summaries were collected (all reads failed or all summaries empty).");
                return "";
            }

            string mergeUser = string.Join("\n\n", perFileSummaries);
            mergeUser = AgentUtilities.TruncateMiddleForModel(mergeUser, ContextualizerMaxMergeUserChars);
            string merged = await CompleteContextualizerChatWithRetriesAsync(
                mergeContextTogetherUserMessage,
                mergeUser,
                new ChatCompletionOptions { Temperature = 0.2f, MaxOutputTokenCount = 2048 },
                cancellationToken,
                "merge (final overview)",
                ContextualizerMaxMergeUserChars).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(merged))
            {
                merged = BuildUnmergedContextFallback(perFileSummaries);
                if (string.IsNullOrWhiteSpace(merged))
                {
                    ThemedConsole.WriteLine(TerminalTone.Error,
                        "[Contextualizer] Merge step failed and unmerged fallback is empty.");
                    return "";
                }

                ThemedConsole.WriteLine(TerminalTone.Reasoning,
                    "[Contextualizer] Using unmerged per-file notes as repo context (merge call failed or returned empty).");
            }

            return merged.Trim();
        }

        static string BuildUnmergedContextFallback(IReadOnlyList<string> perFileSummaries)
        {
            if (perFileSummaries is null || perFileSummaries.Count == 0)
                return "";
            const int max = 50_000;
            string body = "Repository context (unmerged; merge did not return text):\n\n" +
                string.Join("\n\n", perFileSummaries);
            return body.Length > max
                ? AgentUtilities.TruncateMiddleForModel(body, max)
                : body;
        }

        private static bool IsRetryableContextualizerRequest(ClientResultException ex) =>
            ex.Status is 400 or 413 or 429 or 500 or 502 or 503;

        private static void LogContextualizerClientFailure(string step, Exception ex)
        {
            if (ex is ClientResultException cre)
            {
                ThemedConsole.WriteLine(TerminalTone.Error,
                    $"[Contextualizer] {step}: HTTP {cre.Status} — {cre.Message}");
                try
                {
                    PipelineResponse? raw = cre.GetRawResponse();
                    raw?.BufferContent();
                    if (raw?.Content is { Length: > 0 } bd)
                    {
                        string s = bd.ToString();
                        if (s.Length > 1800)
                            s = s[..1800] + "…";
                        ThemedConsole.WriteLine(TerminalTone.Error, "[Contextualizer] Server response body: " + s);
                    }
                }
                catch
                {
                }
            }
            else
            {
                ThemedConsole.WriteLine(TerminalTone.Error, $"[Contextualizer] {step}: {ex.Message}");
            }
        }

        private async Task<string> CompleteContextualizerSingleChatAsync(
            string system,
            string user,
            ChatCompletionOptions options,
            CancellationToken cancellationToken,
            string step)
        {
            if (_contextualizerChatClient is null)
                return "";

            return await WithPerCallTimeoutAndTelemetryAsync(
                $"[Contextualizer] {step}",
                TerminalTone.Reasoning,
                AgentDefaults.ContextualizerCallTimeout,
                async linkedCt =>
                {
                    List<ChatMessage> messages =
                    [
                        new SystemChatMessage(system),
                        new UserChatMessage(user)
                    ];

                    ClientResult<ChatCompletion> clientResult = await _contextualizerChatClient
                        .CompleteChatAsync(messages, options, linkedCt)
                        .ConfigureAwait(false);
                    ChatCompletion completion = clientResult.Value;

                    if (completion.FinishReason != ChatFinishReason.Stop)
                    {
                        ThemedConsole.WriteLine(TerminalTone.Error,
                            $"[Contextualizer] {step}: finish_reason={completion.FinishReason} (content may be partial or empty).");
                    }

                    string text = AgentUtilities.GetChatCompletionText(completion);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        ThemedConsole.WriteLine(TerminalTone.Error,
                            $"[Contextualizer] {step}: no text in completion (finish_reason={completion.FinishReason}).");
                    }

                    return text;
                },
                cancellationToken,
                logCompletionDuration: false).ConfigureAwait(false);
        }

        /// <summary>
        /// LM Studio and small local models often return 400 when the request body is too large.
        /// Pre-truncates to <paramref name="preTruncateUserToMaxChars"/> and halves the user
        /// message on retryable HTTP errors.
        /// </summary>
        private async Task<string> CompleteContextualizerChatWithRetriesAsync(
            string system,
            string user,
            ChatCompletionOptions options,
            CancellationToken cancellationToken,
            string step,
            int preTruncateUserToMaxChars)
        {
            if (_contextualizerChatClient is null)
                return "";

            if (user.Length > preTruncateUserToMaxChars)
            {
                ThemedConsole.WriteLine(TerminalTone.Reasoning,
                    $"[Contextualizer] {step}: pre-shrinking user message {user.Length} → {preTruncateUserToMaxChars} chars");
                user = AgentUtilities.TruncateMiddleForModel(user, preTruncateUserToMaxChars);
            }

            string current = user;
            for (int attempt = 0; attempt < ContextualizerHttpRetryMaxAttempts; attempt++)
            {
                try
                {
                    return await CompleteContextualizerSingleChatAsync(
                        system,
                        current,
                        options,
                        cancellationToken,
                        step).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    ThemedConsole.WriteLine(
                        TerminalTone.Error,
                        $"[Contextualizer] {step}: request timed out after {AgentDefaults.ContextualizerCallTimeout.TotalMinutes:F0}m.");
                    return "";
                }
                catch (ClientResultException ex) when (IsRetryableContextualizerRequest(ex)
                    && current.Length > ContextualizerHttpRetryMinUserChars)
                {
                    LogContextualizerClientFailure($"{step} (attempt {attempt + 1})", ex);
                    string? errBody = TryGetClientErrorBodyString(ex);
                    bool nCtxPromptTooLarge = TextIndicatesLlmContextLimit(errBody);
                    if (nCtxPromptTooLarge)
                    {
                        int cap = Math.Min(current.Length, ContextualizerNCtxRetryTargetUserChars);
                        cap = Math.Max(ContextualizerHttpRetryMinUserChars, cap);
                        current = AgentUtilities.TruncateMiddleForModel(current, cap) +
                            "\n\n[... truncated for local model n_ctx / n_keep limit ...]\n";
                        ThemedConsole.WriteLine(TerminalTone.Reasoning,
                            $"[Contextualizer] {step}: n_ctx/n_keep limit in server body; retrying with ~{current.Length} user chars " +
                            "(raise LM Studio context length or use a larger contextualizer model if this persists).");
                    }
                    else
                    {
                        int n = Math.Max(
                            ContextualizerHttpRetryMinUserChars,
                            (current.Length * 1) / 2);
                        current = current[..n] + "\n\n[... truncated for server limits after HTTP error ...]\n";
                        ThemedConsole.WriteLine(TerminalTone.Reasoning,
                            $"[Contextualizer] {step}: retrying with ~{current.Length} chars in user message");
                    }
                }
                catch (Exception ex)
                {
                    LogContextualizerClientFailure(step, ex);
                    if (ex is not ClientResultException)
                        ThemedConsole.WriteLine(TerminalTone.Error, FormatExceptionDetail(ex));
                    return "";
                }
            }

            ThemedConsole.WriteLine(TerminalTone.Error,
                $"[Contextualizer] {step}: request failed after {ContextualizerHttpRetryMaxAttempts} attempt(s) with a retryable error.");
            return "";
        }

        private sealed class ContextualizerPickDto
        {
            public List<string>? Paths { get; set; }
        }

        private string GenerateSystemPrompt()
        {

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("You are a coding assistant whose goal is to help solve coding tasks with reliable tool usage.");
            sb.AppendLine("You have access to a series of tools you can execute. Here are the tools you can execute:");
            sb.AppendLine(ToolKit.GenerateToolRegistryAsString());
            sb.AppendLine();
            sb.AppendLine("Prefer BASH for reading, searching, and listing files.");
            sb.AppendLine("Use EDIT_FILE for writing/changing file contents.");
            sb.AppendLine("Use WEB_CALL for fetching web content.");
            sb.AppendLine("READ_FILE/LIST_FILE remain deprecated compatibility tools.");
            sb.AppendLine();
            sb.AppendLine("DISCIPLINE (avoid redundant tools):");
            sb.AppendLine("- Default to at most ONE tool: line per reply. Use two only if you must touch two different resources in parallel.");
            sb.AppendLine("- Do not repeat the same exploration pattern (e.g. multiple ls/dir/Get-ChildItem/find variants for the same directory).");
            sb.AppendLine("- Do not re-read the same file path in one task unless the first read failed, was empty, or was clearly truncated.");
            sb.AppendLine("- For WEB_CALL: start with one canonical URL (often the homepage). Add a different URL only if the first page does not contain the answer.");
            sb.AppendLine("- Never emit a tool: line for a WEB_CALL URL you already successfully fetched in this task; the host will skip it as a duplicate.");
            sb.AppendLine("- When tool JSON already answers the user, respond in plain language with NO tool: line.");
            sb.AppendLine();
            sb.AppendLine("TOOL TURNS: tool requests must be one or more lines that each start with " +
                "\"tool:\" and nothing else on that line (no markdown, no preamble, no code fences). " +
                "Prefer a single tool: line; at most two tool lines per reply.");
            sb.AppendLine("Format: tool: TOOL_NAME({\"argName\":\"value\"}) with valid JSON inside the parentheses.");
            sb.AppendLine("When BASH command text contains quotes, escape inner double-quotes as \\\" or use single quotes in the shell command.");
            sb.AppendLine("BASH SHELL AWARENESS:");
            sb.AppendLine("- On Windows, BASH runs in PowerShell (`pwsh`/`powershell`). Use native PowerShell commands (Get-ChildItem, Get-Content, Select-String, New-Item, Set-Content).");
            sb.AppendLine("- On macOS/Linux, BASH runs in a POSIX shell (`bash`/`zsh`/`sh`). Use native shell commands (ls, cat, grep/rg, mkdir -p, printf, tee).");
            sb.AppendLine("- Do not use bash heredocs (`cat <<EOF`) on Windows. Use PowerShell here-strings (`@'...'@`) piped to Set-Content.");
            sb.AppendLine("- Before writing a file under a subdirectory, create the parent folder first (PowerShell: New-Item -ItemType Directory -Force; POSIX: mkdir -p).");
            sb.AppendLine("- After creating or editing a file, immediately verify it with one read command and include the resolved file path in your next normal-language reply.");
            sb.AppendLine("- Never send raw SQL text as a shell command. For SQL tasks, first create/update a `.sql` file, then optionally run it with `psql` or `docker compose exec`.");
            sb.AppendLine("- For SQL file tasks, write files in the current workspace (no `/tmp`), unless the user explicitly asks for a temp path.");
            sb.AppendLine("- SQL files must contain RAW SQL text with real line breaks, not escaped `\\n` literals inside a quoted string.");
            sb.AppendLine("- Prefer single-write multi-line file creation.");
            sb.AppendLine("  - Windows PowerShell: use a here-string piped to `Set-Content`.");
            sb.AppendLine("  - macOS/Linux: use heredoc (`cat <<'EOF' > file.sql`) or `printf` with actual newlines.");
            sb.AppendLine("- For Docker Compose, use service names from docker-compose.yml (not container_name) with commands like `docker compose up -d <service>`.");
            sb.AppendLine("- If any compose command reports unknown service, immediately run `docker compose config --services`, then retry with one exact service name from that list.");
            sb.AppendLine("Example Windows read: tool: BASH({\"command\":\"Get-Content Program.cs\"})");
            sb.AppendLine("Example POSIX read: tool: BASH({\"command\":\"cat Program.cs\"})");
            sb.AppendLine("Example web call: tool: WEB_CALL({\"url\":\"https://www.cobec.com/\"})");
            sb.AppendLine();
            sb.AppendLine("When tool results are provided, consume them and continue until the task is complete.");
            sb.AppendLine("FINAL TURN: provide a normal language answer only after required tools are done.");

            return sb.ToString();
        }

        private async Task<CreateResponseResult> StartNewResponseAsync(
        string modelId,
        string input,
        ResponsesClient lmStudioResponsesClient,
        string? repoContext = null,
        bool compactPrompt = false,
        CancellationToken cancellation = default)
            {
                if (string.IsNullOrWhiteSpace(modelId))
                {
                    throw new ArgumentException("Model ID is required.", nameof(modelId));
                }

                if (string.IsNullOrWhiteSpace(input))
                {
                    throw new ArgumentException("Input is required.", nameof(input));
                }

                CreateResponseOptions options = new CreateResponseOptions { Model = modelId };
                // Keep each model call in a fresh context window.
                options.InputItems.Add(ResponseItem.CreateDeveloperMessageItem(
                    compactPrompt ? GenerateCompactSystemPrompt() : GenerateSystemPrompt()));
                if (!string.IsNullOrWhiteSpace(repoContext))
                    options.InputItems.Add(ResponseItem.CreateDeveloperMessageItem(
                        "Repository context (prepared offline):\n" + repoContext));
                options.InputItems.Add(ResponseItem.CreateUserMessageItem(input));

                ClientResult<ResponseResult> clientResult =
                    await lmStudioResponsesClient.CreateResponseAsync(options, cancellation).ConfigureAwait(false);

                ResponseResult response = clientResult.Value;
                return new CreateResponseResult
                {
                    ResponseId = response.Id ?? "",
                    ResponseContentText = response.GetOutputText() ?? ""
                };
            }

        private async Task<CreateResponseResult> CreateResponseWithRetryAsync(
            string modelId,
            string input,
            ResponsesClient lmStudioResponsesClient,
            string? repoContext,
            bool compactPrompt,
            CancellationToken cancellation)
        {
            Exception? last = null;
            string currentInput = input;
            string? currentRepoContext = repoContext;
            bool useCompactPrompt = compactPrompt;
            for (int attempt = 1; attempt <= MaxResponseRetries; attempt++)
            {
                try
                {
                    return await StartNewResponseAsync(
                        modelId: modelId,
                        input: currentInput,
                        lmStudioResponsesClient: lmStudioResponsesClient,
                        repoContext: currentRepoContext,
                        compactPrompt: useCompactPrompt,
                        cancellation: cancellation).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (IsContextWindowExceeded(ex))
                {
                    last = ex;
                    // Progressive context shedding: compact prompt, shorter user message, then drop repo context.
                    useCompactPrompt = true;
                    if (currentInput.Length > 2800)
                    {
                        currentInput = AgentUtilities.TruncateMiddleForModel(currentInput, 2800) +
                            "\n\n[...truncated due to model context limit...]\n";
                    }
                    else if (!string.IsNullOrWhiteSpace(currentRepoContext))
                    {
                        currentRepoContext = null;
                    }

                    if (attempt == MaxResponseRetries)
                    {
                        break;
                    }

                    continue;
                }
                catch (Exception ex)
                {
                    last = ex;
                    if (attempt == MaxResponseRetries)
                    {
                        break;
                    }

                    int backoffMs = (int)Math.Pow(2, attempt - 1) * 500;
                    await Task.Delay(backoffMs, cancellation).ConfigureAwait(false);
                }
            }

            throw new InvalidOperationException($"Response failed after {MaxResponseRetries} attempts.", last);
        }

        private string GenerateCompactSystemPrompt()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("You are a coding assistant. Use tools reliably and keep outputs concise.");
            sb.AppendLine("Available tools:");
            sb.AppendLine(ToolKit.GenerateToolRegistryAsString());
            sb.AppendLine("Rules:");
            sb.AppendLine("- Prefer BASH; emit at most one tool line unless truly necessary.");
            sb.AppendLine("- Use valid shell syntax for current OS.");
            sb.AppendLine("- For SQL tasks, write raw multiline SQL into .sql files (no escaped \\n literals).");
            sb.AppendLine("- After writing a file, verify by reading it back.");
            sb.AppendLine("- For docker compose, use service names from docker-compose.yml.");
            sb.AppendLine("Tool line format: tool: TOOL_NAME({\"arg\":\"value\"})");
            return sb.ToString();
        }

        private sealed class ToolExecutionEnvelope
        {
            public string ToolName { get; set; } = "";
            public Dictionary<string, object?> Args { get; set; } = new();
            public Dictionary<string, object> Result { get; set; } = new();
        }

        public async Task StartNewResponseWithStreamingAndReasoning(
        string modelId,
        string userInput,
        ResponsesClient responseClient,
        ResponseReasoningEffortLevel reasoningLevel,
        CancellationToken cancellationToken = default)
        {
            CreateResponseOptions responseOption = new CreateResponseOptions
            {
                Model = modelId,
                ReasoningOptions = new ResponseReasoningOptions
                {
                    ReasoningEffortLevel = reasoningLevel
                },
            };
            responseOption.InputItems.Add(ResponseItem.CreateDeveloperMessageItem(GenerateSystemPrompt()));
            responseOption.InputItems.Add(ResponseItem.CreateUserMessageItem(userInput));

            ResponseResult response = await responseClient.CreateResponseAsync(responseOption, cancellationToken);

            CreateResponseOptions streamOptions = new CreateResponseOptions
            {
                Model = modelId,
                ReasoningOptions = new ResponseReasoningOptions
                {
                    ReasoningEffortLevel = reasoningLevel
                },
                StreamingEnabled = true
            };

            streamOptions.InputItems.Add(ResponseItem.CreateDeveloperMessageItem(GenerateSystemPrompt()));
            streamOptions.InputItems.Add(ResponseItem.CreateUserMessageItem(userInput));

            await foreach (StreamingResponseUpdate update
                in responseClient.CreateResponseStreamingAsync(streamOptions))
            {
                if (update is StreamingResponseOutputItemAddedUpdate itemUpdate &&
                    itemUpdate.Item is ReasoningResponseItem reasoningItem)
                {
                    ThemedConsole.WriteLine(TerminalTone.Reasoning, $"[Reasoning Status]: {reasoningItem.Status}");
                    ThemedConsole.WriteLine(TerminalTone.Reasoning, $"[Reasoning Status]: {reasoningItem.GetSummaryText()}");
                }
                else if (update is StreamingResponseOutputItemAddedUpdate itemDone &&
                            itemDone.Item is ReasoningResponseItem reasoningDone)
                {

                    ThemedConsole.WriteLine(TerminalTone.Reasoning, $"[Reasoning Done]: {reasoningDone.Status}");
                    ThemedConsole.WriteLine(TerminalTone.Reasoning, $"[Reasoning Done]: {reasoningDone.GetSummaryText()}");
                }
                else if (update is StreamingResponseOutputTextDeltaUpdate textDelta)
                {
                    ThemedConsole.Write(TerminalTone.Agent, textDelta.Delta);
                }
            }

            ThemedConsole.Reset();
        }
        

    }
}
