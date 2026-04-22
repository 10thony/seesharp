using OpenAI.Chat;
using OpenAI.Models;
using OpenAI.Responses;
using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SeeSharp.Models
{

    public class LMStudioAgent(OpenAIModel model, ToolKit toolRegistry, ChatClient contextualizerChatClient)
        : Agent(model, toolRegistry, contextualizerChatClient)
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
    public class Agent
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
        private const int MaxSerializedToolOutputChars = 12_000;
        private const int MaxToolStringValueChars = 4_000;
        private const int MaxNoProgressTurns = 3;

        public OpenAIModel Model { get; set; }
        private ToolKit ToolKit { get; set;  }
        private readonly ChatClient? _contextualizerChatClient;
        
        public Agent(OpenAIModel model,
                     ToolKit toolRegistry,
                     ChatClient? contextualizerChatClient = null)
        {
            this.Model = model;
            this.ToolKit = toolRegistry;
            _contextualizerChatClient = contextualizerChatClient;
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
                ThemedConsole.WriteLine(TerminalTone.Error, $"[Contextualizer] Skipped: {ex.Message}");

                //add resiliant context generating here. if it fails we should indicate and retry atleast thrice
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
                    ThemedConsole.WriteLine(TerminalTone.Error, "[Contextualizer] ERROR CONTEXTUALIZING AGENT");
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
            string currentInput = task;
            string lastAssistantText = "";
            IReadOnlyList<ToolExecutionEnvelope> lastToolOutputs = Array.Empty<ToolExecutionEnvelope>();
            bool shouldRehydrateRepoContext = false;
            bool hasContextResetAttempted = false;
            int noProgressTurns = 0;
            string lastProgressSignature = "";
            HashSet<string> seenToolSignatures = new(StringComparer.OrdinalIgnoreCase);
            int successfulToolExecutions = 0;
            int consecutiveAllSkippedTurns = 0;

            for (int turn = 0; turn < MaxAgentTurnsPerTask; turn++)
            {
                string? perTurnRepoContext = (turn == 0 || shouldRehydrateRepoContext) ? repoContext : null;
                shouldRehydrateRepoContext = false;
                CreateResponseResult assistantResponse;
                try
                {
                    assistantResponse = await CreateResponseWithRetryAsync(
                        modelId: this.Model.Id,
                        input: currentInput,
                        lmStudioResponsesClient: responsesClient,
                        repoContext: perTurnRepoContext,
                        cancellation: cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsContextWindowExceeded(ex) && !hasContextResetAttempted)
                {
                    hasContextResetAttempted = true;
                    shouldRehydrateRepoContext = true;
                    currentInput = BuildResumeAfterContextResetInput(task, lastAssistantText, lastToolOutputs);
                    ThemedConsole.WriteLine(TerminalTone.Error,
                        "[Agent] Context window exceeded. Resetting context and resubmitting current task.");
                    continue;
                }

                string assistantText = assistantResponse.ResponseContentText?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(assistantText))
                {
                    return "[Assistant returned empty output.]";
                }
                lastAssistantText = assistantText;

                List<(string toolName, Dictionary<string, object?> args)> invocations =
                    await ParseToolInvocationsWithSmallModelAsync(
                        responsesClient,
                        task,
                        repoContext,
                        assistantText,
                        cancellationToken).ConfigureAwait(false);

                if (invocations.Count == 0)
                {
                    return assistantText;
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
                lastToolOutputs = toolOutputs;

                successfulToolExecutions += CountSuccessfulToolExecutions(toolOutputs);
                if (successfulToolExecutions > MaxSuccessfulToolExecutionsPerTask)
                {
                    return "Stopped: tool execution budget for this task was reached. Summarize what you have so far " +
                        "and say what is still missing, without requesting more tools.";
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

                if (consecutiveAllSkippedTurns >= 2)
                {
                    return "Stopped: the model requested only duplicate tools already run this task. " +
                        "Use the tool JSON already returned in the conversation to answer, or state what is blocking you.";
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
                    return "Stopped early because tool execution stopped making progress (repeated duplicate/unchanged results).";
                }

                currentInput = await BuildToolBridgeMessageAsync(
                    responsesClient,
                    task,
                    assistantText,
                    toolOutputs,
                    turnNumberOneBased: turn + 1,
                    successfulToolExecutions,
                    allSkippedThisTurn,
                    cancellationToken).ConfigureAwait(false);
            }

            return "Stopped after max turns while resolving tool calls.";
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

            CreateResponseResult parsed = await CreateResponseWithRetryAsync(
                modelId: DefaultToolInvocationHandlerModel,
                input: parserPrompt.ToString(),
                lmStudioResponsesClient: responsesClient,
                repoContext: repoContext,
                cancellation: cancellationToken).ConfigureAwait(false);

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
            nextInput.AppendLine("For WEB_CALL, usually one primary page is enough unless the first body clearly lacks the answer.");
            if (successfulToolExecutions >= 10)
            {
                nextInput.AppendLine("You have already run many tools; strongly prefer a final answer now unless one specific fact is still missing.");
            }

            if (allSkippedThisTurn)
            {
                nextInput.AppendLine();
                nextInput.AppendLine(
                    "IMPORTANT: Every tool call in this round was skipped as a duplicate of an earlier run. " +
                    "Do not emit those same tools again. Answer from the JSON already in this thread, " +
                    "or explain what is still unknown without re-running identical commands.");
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
                if (compactToolState.Length > 8_000)
                {
                    compactToolState = compactToolState[..8_000] + "\n[...truncated...]";
                }

                sb.AppendLine("Most recent tool results:");
                sb.AppendLine(compactToolState);
            }

            return sb.ToString();
        }

        private static bool IsContextWindowExceeded(Exception ex)
        {
            for (Exception? current = ex; current is not null; current = current.InnerException)
            {
                string message = current.Message ?? "";
                if (message.Contains("n_keep", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("n_ctx", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("context length", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("context window", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("token limit", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        const int ContextualizerMaxListLines = 2500;
        const int ContextualizerMaxFilesToRead = 15;
        const int ContextualizerMaxCharsPerFile = 48_000;
        static readonly JsonSerializerOptions s_contextualizerJson = new() 
        { PropertyNameCaseInsensitive = true };

        private async Task<string> ContextualizeAgentAsync(
            string? userTask, 
            CancellationToken cancellationToken)
        {
            if (_contextualizerChatClient is null)
                return "";

            IReadOnlyList<string> allRelPaths = AgentUtilities.ListWorkspaceSourceFilesRelative();
            if (allRelPaths.Count == 0)
                return "";

            string workspaceRoot = AgentUtilities.ResolveWorkspaceRoot();
            string listBlock = AgentUtilities.BuildFileListUserBlock(allRelPaths, userTask);

            string pickRelevantFilesUserMessage = 
                "You choose which source files matter for understanding this repository." +
                " Reply with a single JSON object only, no markdown, no explanation. " +
                " Schema: {\"paths\":[\"relative/path.cs\",...]} with at most 15 paths." +
                " Prefer: *.csproj, Program.cs, entry points, core Models/, README, and" +
                " files most relevant to the user's task if given. Use forward slashes as in the list.";

            string summarizeFileUserMessage = 
                "Summarize the important information in this file for a coding assistant. Focus on facts," +
                " not opinions. If the file contains code, summarize what the code does." +
                " If the file contains text, summarize the key points." +
                " Keep it concise; a few lines at most. No preamble.";

            string mergeContextTogetherUserMessage = 
                "Merge the following per-file notes into one concise repository overview for " +
                "the main coding assistant. Keep facts only; max ~20 lines. No preamble.";

            string pickRaw = await CompleteContextualizerChatAsync(
               pickRelevantFilesUserMessage,
                listBlock,
                new ChatCompletionOptions { Temperature = 0.2f, MaxOutputTokenCount = 1024 },
                cancellationToken).ConfigureAwait(false);

            List<string> picked = AgentUtilities.ParsePickedPaths(pickRaw, workspaceRoot, allRelPaths);
            if (picked.Count == 0)
                return "";

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
                    continue;
                }

                string content = contentObj.ToString() ?? "";
                if (content.Length > ContextualizerMaxCharsPerFile)
                {
                content = content[..ContextualizerMaxCharsPerFile] + "\n\n[... truncated ...]";
                }

                string user = "File: " + rel + "\n\n" + content;
                string summary = await CompleteContextualizerChatAsync(
                    summarizeFileUserMessage,
                    user,
                    new ChatCompletionOptions { Temperature = 0.35f, MaxOutputTokenCount = 768 },
                    cancellationToken).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(summary))
                    perFileSummaries.Add($"### {rel}\n{summary.Trim()}");
            }

            if (perFileSummaries.Count == 0)
                return "";

            string mergeUser = string.Join("\n\n", perFileSummaries);
            string merged = await CompleteContextualizerChatAsync(
                mergeContextTogetherUserMessage,
                mergeUser,
                new ChatCompletionOptions { Temperature = 0.2f, MaxOutputTokenCount = 2048 },
                cancellationToken).ConfigureAwait(false);

            return merged.Trim();
        }
        async Task<string> CompleteContextualizerChatAsync(
            string system,
            string user,
            ChatCompletionOptions options,
            CancellationToken cancellationToken)
        {
            if (_contextualizerChatClient is null)
                return "";

            List<ChatMessage> messages =
            [
                new SystemChatMessage(system),
                new UserChatMessage(user)
            ];

            ClientResult<ChatCompletion> clientResult =
                await _contextualizerChatClient.CompleteChatAsync(messages, options, cancellationToken).ConfigureAwait(false);
            ChatCompletion completion = clientResult.Value;

            if (completion.FinishReason != ChatFinishReason.Stop)
            {
                ThemedConsole.WriteLine(TerminalTone.Error, $"[Contextualizer] finish_reason={completion.FinishReason}");
            }

            return AgentUtilities.GetChatCompletionText(completion);
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
            sb.AppendLine("- For WEB_CALL: start with one canonical URL (often the homepage). Add a second fetch only if the first page does not contain the answer.");
            sb.AppendLine("- When tool JSON already answers the user, respond in plain language with NO tool: line.");
            sb.AppendLine();
            sb.AppendLine("TOOL TURNS: tool requests must be one or more lines that each start with " +
                "\"tool:\" and nothing else on that line (no markdown, no preamble, no code fences). " +
                "Prefer a single tool: line; at most two tool lines per reply.");
            sb.AppendLine("Format: tool: TOOL_NAME({\"argName\":\"value\"}) with valid JSON inside the parentheses.");
            sb.AppendLine("When BASH command text contains quotes, escape inner double-quotes as \\\" or use single quotes in the shell command.");
            sb.AppendLine("Example bash read: tool: BASH({\"command\":\"Get-Content Program.cs\"})");
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
                options.InputItems.Add(ResponseItem.CreateDeveloperMessageItem(GenerateSystemPrompt()));
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
            CancellationToken cancellation)
        {
            Exception? last = null;
            for (int attempt = 1; attempt <= MaxResponseRetries; attempt++)
            {
                try
                {
                    return await StartNewResponseAsync(
                        modelId: modelId,
                        input: input,
                        lmStudioResponsesClient: lmStudioResponsesClient,
                        repoContext: repoContext,
                        cancellation: cancellation).ConfigureAwait(false);
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
