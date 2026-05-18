using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SeeSharp.Models
{
    /// <summary>
    /// Root configuration model for SeeSharp. Deserialized from both global
    /// (~/.seesharp/config.json) and per-workspace (seesharp.config.json) files.
    /// All properties are nullable — missing values inherit from the next lower layer
    /// or fall back to compiled defaults.
    /// </summary>
    public sealed class SeeSharpConfig
    {
        [JsonPropertyName("$schema")]
        public string? Schema { get; set; }

        [JsonPropertyName("_comment")]
        public string? Comment { get; set; }

        [JsonPropertyName("agentName")]
        public string? AgentName { get; set; }

        [JsonPropertyName("preferredModel")]
        public string? PreferredModel { get; set; }

        [JsonPropertyName("limits")]
        public AgentLimitsConfig? Limits { get; set; }

        [JsonPropertyName("systemPrompt")]
        public SystemPromptConfig? SystemPrompt { get; set; }

        [JsonPropertyName("contextualizer")]
        public ContextualizerConfig? Contextualizer { get; set; }

        [JsonPropertyName("customTools")]
        public List<CustomToolDefinition>? CustomTools { get; set; }

        [JsonPropertyName("disabledTools")]
        public List<string>? DisabledTools { get; set; }

        [JsonPropertyName("theme")]
        public ConsoleThemeConfig? Theme { get; set; }
    }

    public sealed class AgentLimitsConfig
    {
        [JsonPropertyName("maxAgentTurnsPerTask")]
        public int? MaxAgentTurnsPerTask { get; set; }

        [JsonPropertyName("maxToolCallsPerTurn")]
        public int? MaxToolCallsPerTurn { get; set; }

        [JsonPropertyName("maxSuccessfulToolExecutionsPerTask")]
        public int? MaxSuccessfulToolExecutionsPerTask { get; set; }

        [JsonPropertyName("bashCommandTimeoutSeconds")]
        public int? BashCommandTimeoutSeconds { get; set; }

        [JsonPropertyName("responsesApiCallTimeoutMinutes")]
        public int? ResponsesApiCallTimeoutMinutes { get; set; }

        [JsonPropertyName("contextualizerCallTimeoutMinutes")]
        public int? ContextualizerCallTimeoutMinutes { get; set; }

        [JsonPropertyName("maxNoProgressTurns")]
        public int? MaxNoProgressTurns { get; set; }

        [JsonPropertyName("maxResponseRetries")]
        public int? MaxResponseRetries { get; set; }
    }

    public sealed class SystemPromptConfig
    {
        /// <summary>Text prepended before the generated system prompt.</summary>
        [JsonPropertyName("prepend")]
        public string? Prepend { get; set; }

        /// <summary>Text appended after the generated system prompt.</summary>
        [JsonPropertyName("append")]
        public string? Append { get; set; }

        /// <summary>If non-null, completely replaces the generated system prompt.</summary>
        [JsonPropertyName("replace")]
        public string? Replace { get; set; }
    }

    public sealed class ContextualizerConfig
    {
        [JsonPropertyName("maxFilesToRead")]
        public int? MaxFilesToRead { get; set; }

        [JsonPropertyName("maxCharsPerFile")]
        public int? MaxCharsPerFile { get; set; }

        [JsonPropertyName("excludedDirectories")]
        public List<string>? ExcludedDirectories { get; set; }

        [JsonPropertyName("pinnedFiles")]
        public List<string>? PinnedFiles { get; set; }

        [JsonPropertyName("preferredModel")]
        public string? PreferredModel { get; set; }
    }

    public sealed class CustomToolDefinition
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        /// <summary>"bash" or "http"</summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = "bash";

        /// <summary>Shell command template. Use {{paramName}} for parameter interpolation.</summary>
        [JsonPropertyName("command")]
        public string? Command { get; set; }

        [JsonPropertyName("workingDirectory")]
        public string? WorkingDirectory { get; set; }

        [JsonPropertyName("timeoutSeconds")]
        public int? TimeoutSeconds { get; set; }

        /// <summary>Parameter names expected in the template (for "bash" type).</summary>
        [JsonPropertyName("parameters")]
        public List<string>? Parameters { get; set; }

        /// <summary>HTTP method (for "http" type).</summary>
        [JsonPropertyName("method")]
        public string? Method { get; set; }

        /// <summary>URL template (for "http" type). Use {{paramName}} for interpolation.</summary>
        [JsonPropertyName("url")]
        public string? Url { get; set; }

        /// <summary>Request body template (for "http" type).</summary>
        [JsonPropertyName("body")]
        public string? Body { get; set; }
    }

    public sealed class ConsoleThemeConfig
    {
        [JsonPropertyName("agentColor")]
        public string? AgentColor { get; set; }

        [JsonPropertyName("toolColor")]
        public string? ToolColor { get; set; }

        [JsonPropertyName("errorColor")]
        public string? ErrorColor { get; set; }

        [JsonPropertyName("reasoningColor")]
        public string? ReasoningColor { get; set; }

        [JsonPropertyName("userColor")]
        public string? UserColor { get; set; }

        [JsonPropertyName("actionColor")]
        public string? ActionColor { get; set; }
    }

    /// <summary>
    /// Fully resolved configuration with guaranteed non-null values.
    /// Produced by <see cref="SeeSharpConfigLoader"/> after merging all layers.
    /// </summary>
    public sealed class ResolvedConfig
    {
        public string AgentName { get; set; } = "SeeSharp";
        public string? PreferredModel { get; set; }

        // Limits
        public int MaxAgentTurnsPerTask { get; set; } = 28;
        public int MaxToolCallsPerTurn { get; set; } = 2;
        public int MaxSuccessfulToolExecutionsPerTask { get; set; } = 22;
        public TimeSpan BashCommandTimeout { get; set; } = TimeSpan.FromSeconds(30);
        public TimeSpan ResponsesApiCallTimeout { get; set; } = TimeSpan.FromMinutes(10);
        public TimeSpan ContextualizerCallTimeout { get; set; } = TimeSpan.FromMinutes(10);
        public int MaxNoProgressTurns { get; set; } = 3;
        public int MaxResponseRetries { get; set; } = 3;

        // System prompt
        public string? SystemPromptPrepend { get; set; }
        public string? SystemPromptAppend { get; set; }
        public string? SystemPromptReplace { get; set; }

        // Contextualizer
        public int ContextualizerMaxFilesToRead { get; set; } = 15;
        public int ContextualizerMaxCharsPerFile { get; set; } = 48_000;
        public List<string> ContextualizerExcludedDirectories { get; set; } = new()
        {
            "bin", "obj", ".git", ".vs", ".idea", "node_modules", "openai-dotnet", ".dockerignore", "tools"
        };
        public List<string> ContextualizerPinnedFiles { get; set; } = new()
        {
            "SeeSharp.csproj", "Program.cs", "Models/Agent.cs", "Models/ToolKit.cs", "Models/AgentUtilities.cs"
        };
        public string? ContextualizerPreferredModel { get; set; }

        // Custom tools
        public List<CustomToolDefinition> CustomTools { get; set; } = new();
        public HashSet<string> DisabledTools { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        // Theme
        public ConsoleColor AgentColor { get; set; } = ConsoleColor.DarkCyan;
        public ConsoleColor ToolColor { get; set; } = ConsoleColor.Green;
        public ConsoleColor ErrorColor { get; set; } = ConsoleColor.Red;
        public ConsoleColor ReasoningColor { get; set; } = ConsoleColor.DarkCyan;
        public ConsoleColor UserColor { get; set; } = ConsoleColor.Magenta;
        public ConsoleColor ActionColor { get; set; } = ConsoleColor.Yellow;

        // Source tracking for diagnostics
        public string? GlobalConfigPath { get; set; }
        public string? WorkspaceConfigPath { get; set; }
    }
}
