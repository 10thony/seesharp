using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace SeeSharp.Models
{
    /// <summary>
    /// Records all session events (messages, tool calls, results, outcomes) to a JSONL file.
    /// Each line is a timestamped event that, when replayed in order, reconstructs the full
    /// session for fine-tuning data extraction.
    /// 
    /// Thread-safe: multiple agent tasks can record concurrently. Flush is crash-safe —
    /// events are written incrementally so partial sessions survive unexpected termination.
    /// </summary>
    public sealed class SessionRecorder : IDisposable
    {
        private readonly string _outputPath;
        private readonly StreamWriter _writer;
        private readonly object _writeLock = new();
        private readonly string _sessionId;
        private readonly DateTime _startedAtUtc;
        private bool _disposed;

        private static readonly JsonSerializerOptions s_jsonOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public string SessionId => _sessionId;
        public string OutputPath => _outputPath;

        public SessionRecorder(string outputDirectory)
        {
            _sessionId = Guid.NewGuid().ToString("N")[..12];
            _startedAtUtc = DateTime.UtcNow;

            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            string timestamp = _startedAtUtc.ToString("yyyyMMdd_HHmmss");
            string fileName = $"session_{timestamp}_{_sessionId}.jsonl";
            _outputPath = Path.Combine(outputDirectory, fileName);

            _writer = new StreamWriter(_outputPath, append: false, encoding: System.Text.Encoding.UTF8)
            {
                AutoFlush = true
            };
        }

        public void RecordSessionStart(string modelId, string workspaceRoot, string? contextualizerModelId = null)
        {
            WriteEvent(new SessionEvent
            {
                Type = "session_start",
                SessionId = _sessionId,
                Timestamp = DateTime.UtcNow,
                Data = new Dictionary<string, object?>
                {
                    ["model_id"] = modelId,
                    ["workspace_root"] = workspaceRoot,
                    ["contextualizer_model_id"] = contextualizerModelId,
                    ["machine_name"] = Environment.MachineName,
                    ["os"] = System.Runtime.InteropServices.RuntimeInformation.OSDescription
                }
            });
        }

        public void RecordSystemPrompt(string systemPrompt)
        {
            WriteEvent(new SessionEvent
            {
                Type = "system_prompt",
                SessionId = _sessionId,
                Timestamp = DateTime.UtcNow,
                Data = new Dictionary<string, object?>
                {
                    ["content"] = systemPrompt
                }
            });
        }

        public void RecordRepoContext(string repoContext)
        {
            WriteEvent(new SessionEvent
            {
                Type = "repo_context",
                SessionId = _sessionId,
                Timestamp = DateTime.UtcNow,
                Data = new Dictionary<string, object?>
                {
                    ["content"] = repoContext
                }
            });
        }

        public void RecordUserTask(string task, int taskIndex)
        {
            WriteEvent(new SessionEvent
            {
                Type = "user_task",
                SessionId = _sessionId,
                Timestamp = DateTime.UtcNow,
                Data = new Dictionary<string, object?>
                {
                    ["task"] = task,
                    ["task_index"] = taskIndex
                }
            });
        }

        public void RecordAssistantResponse(string responseText, int turnNumber, string? responseId = null)
        {
            WriteEvent(new SessionEvent
            {
                Type = "assistant_response",
                SessionId = _sessionId,
                Timestamp = DateTime.UtcNow,
                Data = new Dictionary<string, object?>
                {
                    ["content"] = responseText,
                    ["turn"] = turnNumber,
                    ["response_id"] = responseId
                }
            });
        }

        public void RecordToolCalls(
            IReadOnlyList<(string toolName, Dictionary<string, object?> args)> invocations,
            int turnNumber)
        {
            var serializable = new List<Dictionary<string, object?>>(invocations.Count);
            foreach (var (toolName, args) in invocations)
            {
                serializable.Add(new Dictionary<string, object?>
                {
                    ["tool_name"] = toolName,
                    ["arguments"] = args
                });
            }

            WriteEvent(new SessionEvent
            {
                Type = "tool_calls",
                SessionId = _sessionId,
                Timestamp = DateTime.UtcNow,
                Data = new Dictionary<string, object?>
                {
                    ["invocations"] = serializable,
                    ["turn"] = turnNumber
                }
            });
        }

        public void RecordToolResults(
            IReadOnlyList<ToolExecutionRecord> results,
            int turnNumber)
        {
            WriteEvent(new SessionEvent
            {
                Type = "tool_results",
                SessionId = _sessionId,
                Timestamp = DateTime.UtcNow,
                Data = new Dictionary<string, object?>
                {
                    ["results"] = results,
                    ["turn"] = turnNumber
                }
            });
        }

        public void RecordTurnBridge(string bridgeMessage, int turnNumber)
        {
            WriteEvent(new SessionEvent
            {
                Type = "turn_bridge",
                SessionId = _sessionId,
                Timestamp = DateTime.UtcNow,
                Data = new Dictionary<string, object?>
                {
                    ["content"] = bridgeMessage,
                    ["turn"] = turnNumber
                }
            });
        }

        public void RecordTaskOutcome(string task, int taskIndex, string status, string? finalAnswer = null)
        {
            WriteEvent(new SessionEvent
            {
                Type = "task_outcome",
                SessionId = _sessionId,
                Timestamp = DateTime.UtcNow,
                Data = new Dictionary<string, object?>
                {
                    ["task"] = task,
                    ["task_index"] = taskIndex,
                    ["status"] = status,
                    ["final_answer"] = finalAnswer
                }
            });
        }

        public void RecordSessionEnd(string reason = "normal")
        {
            WriteEvent(new SessionEvent
            {
                Type = "session_end",
                SessionId = _sessionId,
                Timestamp = DateTime.UtcNow,
                Data = new Dictionary<string, object?>
                {
                    ["reason"] = reason,
                    ["duration_seconds"] = (DateTime.UtcNow - _startedAtUtc).TotalSeconds
                }
            });
        }

        public void RecordError(string context, string errorMessage)
        {
            WriteEvent(new SessionEvent
            {
                Type = "error",
                SessionId = _sessionId,
                Timestamp = DateTime.UtcNow,
                Data = new Dictionary<string, object?>
                {
                    ["context"] = context,
                    ["message"] = errorMessage
                }
            });
        }

        private void WriteEvent(SessionEvent evt)
        {
            if (_disposed)
                return;

            try
            {
                string json = JsonSerializer.Serialize(evt, s_jsonOptions);
                lock (_writeLock)
                {
                    _writer.WriteLine(json);
                }
            }
            catch (Exception ex)
            {
                ThemedConsole.WriteLine(TerminalTone.Error,
                    $"[SessionRecorder] Failed to write event: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            try
            {
                lock (_writeLock)
                {
                    _writer.Flush();
                    _writer.Dispose();
                }
            }
            catch
            {
            }
        }
    }

    internal sealed class SessionEvent
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("session_id")]
        public string SessionId { get; set; } = "";

        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonPropertyName("data")]
        public Dictionary<string, object?>? Data { get; set; }
    }

    /// <summary>
    /// Serializable record of a single tool execution (call + result) for JSONL export.
    /// </summary>
    public sealed class ToolExecutionRecord
    {
        [JsonPropertyName("tool_name")]
        public string ToolName { get; set; } = "";

        [JsonPropertyName("arguments")]
        public Dictionary<string, object?>? Arguments { get; set; }

        [JsonPropertyName("result")]
        public Dictionary<string, object>? Result { get; set; }
    }
}
