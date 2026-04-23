using System;
using System.Collections.Generic;
using System.Text;

namespace SeeSharp.Models
{
    public static class AgentDefaults
    {
        /// <summary>Wall-clock cap for a single shell command (agent BASH tool).</summary>
        public static readonly TimeSpan BashCommandTimeout = TimeSpan.FromSeconds(30);
        /// <summary>How often to log BASH progress while a command is still running.</summary>
        public static readonly TimeSpan BashTelemetryInterval = TimeSpan.FromSeconds(2);
        /// <summary>Per-request cap for Responses API create-response calls (main agent and tool parser).</summary>
        public static readonly TimeSpan ResponsesApiCallTimeout = TimeSpan.FromMinutes(10);
        /// <summary>Per-request cap for contextualizer chat completion calls.</summary>
        public static readonly TimeSpan ContextualizerCallTimeout = TimeSpan.FromMinutes(10);
        /// <summary>How often to log "still waiting" while a model request is in flight.</summary>
        public static readonly TimeSpan LlmTelemetryInterval = TimeSpan.FromSeconds(3);

        public static readonly ConsoleColor YouColor = ConsoleColor.Magenta;
        public static readonly ConsoleColor AgentColor = ConsoleColor.DarkCyan;
        public static readonly ConsoleColor ErrorColor = ConsoleColor.Red;
        public static readonly ConsoleColor ResetColor = ConsoleColor.White;
        public static readonly ConsoleColor AgentToolColor = ConsoleColor.Green;
        public static readonly ConsoleColor AgentReasoningColor = ConsoleColor.DarkCyan;
        public static readonly ConsoleColor AgentActionColor = ConsoleColor.Yellow;

        public static readonly string LIST_FILE_TOOL_NAME = "LIST_FILE";
        public static readonly string EDIT_FILE_TOOL_NAME = "EDIT_FILE";
        public static readonly string READ_TOOL_NAME = "READ_FILE";
        public static readonly string WEB_CALL_TOOL_NAME = "WEB_CALL";
        public static readonly string BASH_TOOL_NAME = "BASH";
    }
}
