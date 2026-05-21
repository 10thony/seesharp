using System;
using System.Collections.Generic;
using System.Text;

namespace SeeSharp.Models
{
    public static class AgentDefaults
    {
        /// <summary>
        /// Active resolved configuration. Set once during startup by Program.cs.
        /// All accessors below read from this first, falling back to compiled constants.
        /// </summary>
        public static ResolvedConfig? ActiveConfig { get; set; }

        // --- Compiled constants (fallback of last resort) ---

        private static readonly TimeSpan DefaultBashCommandTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan DefaultResponsesApiCallTimeout = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan DefaultContextualizerCallTimeout = TimeSpan.FromMinutes(10);

        /// <summary>How often to log BASH progress while a command is still running.</summary>
        public static readonly TimeSpan BashTelemetryInterval = TimeSpan.FromSeconds(2);
        /// <summary>How often to log "still waiting" while a model request is in flight.</summary>
        public static readonly TimeSpan LlmTelemetryInterval = TimeSpan.FromSeconds(3);

        public static readonly ConsoleColor ResetColor = ConsoleColor.White;

        public const string LIST_FILE_TOOL_NAME = "LIST_FILE";
        public const string EDIT_FILE_TOOL_NAME = "EDIT_FILE";
        public const string READ_TOOL_NAME = "READ_FILE";
        public const string WEB_CALL_TOOL_NAME = "WEB_CALL";
        public const string BASH_TOOL_NAME = "BASH";
        public const string CONFIG_EDIT_TOOL_NAME = "CONFIG_EDIT";
        public const string SPAWN_SUBAGENT_TOOL_NAME = "SPAWN_SUBAGENT";
        public const string CHECK_SUBAGENT_TOOL_NAME = "CHECK_SUBAGENT";
        public const string KILL_SUBAGENT_TOOL_NAME = "KILL_SUBAGENT";

        // SubAgent defaults
        public const int DefaultSubAgentMaxConcurrent = 2;
        public const int DefaultSubAgentMaxDepth = 2;
        public const int DefaultSubAgentMaxTurns = 12;
        public const int DefaultSubAgentMaxToolExecutions = 10;
        public static readonly TimeSpan DefaultSubAgentInferenceTimeout = TimeSpan.FromSeconds(120);
        public static readonly TimeSpan DefaultSubAgentModelSwapTimeout = TimeSpan.FromSeconds(15);

        // --- Config-aware accessors ---

        /// <summary>Wall-clock cap for a single shell command (agent BASH tool).</summary>
        public static TimeSpan BashCommandTimeout =>
            ActiveConfig?.BashCommandTimeout ?? DefaultBashCommandTimeout;

        /// <summary>Per-request cap for Responses API create-response calls.</summary>
        public static TimeSpan ResponsesApiCallTimeout =>
            ActiveConfig?.ResponsesApiCallTimeout ?? DefaultResponsesApiCallTimeout;

        /// <summary>Per-request cap for contextualizer chat completion calls.</summary>
        public static TimeSpan ContextualizerCallTimeout =>
            ActiveConfig?.ContextualizerCallTimeout ?? DefaultContextualizerCallTimeout;

        public static ConsoleColor YouColor =>
            ActiveConfig?.UserColor ?? ConsoleColor.Magenta;

        public static ConsoleColor AgentColor =>
            ActiveConfig?.AgentColor ?? ConsoleColor.DarkCyan;

        public static ConsoleColor ErrorColor =>
            ActiveConfig?.ErrorColor ?? ConsoleColor.Red;

        public static ConsoleColor AgentToolColor =>
            ActiveConfig?.ToolColor ?? ConsoleColor.Green;

        public static ConsoleColor AgentReasoningColor =>
            ActiveConfig?.ReasoningColor ?? ConsoleColor.DarkCyan;

        public static ConsoleColor AgentActionColor =>
            ActiveConfig?.ActionColor ?? ConsoleColor.Yellow;
    }
}
