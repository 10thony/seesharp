using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SeeSharp.Models
{
    /// <summary>
    /// Discovers, loads, merges, and validates SeeSharp configuration from:
    ///   1. Compiled defaults (lowest priority)
    ///   2. Global config (~/.seesharp/config.json)
    ///   3. Workspace config (./seesharp.config.json)
    ///   4. Environment variables (highest priority)
    /// Designed to be resilient: any layer that fails to parse is skipped with a warning.
    /// </summary>
    public static class SeeSharpConfigLoader
    {
        public const string GlobalConfigDirectoryName = ".seesharp";
        public const string GlobalConfigFileName = "config.json";
        public const string WorkspaceConfigFileName = "seesharp.config.json";

        private static readonly JsonSerializerOptions s_jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public static ResolvedConfig Load(string workspaceRoot)
        {
            ResolvedConfig resolved = new();

            // Layer 2: Global config
            string globalPath = GetGlobalConfigPath();
            SeeSharpConfig? globalConfig = TryLoadConfigFile(globalPath, "global");
            if (globalConfig is not null)
            {
                resolved.GlobalConfigPath = globalPath;
                MergeInto(resolved, globalConfig);
            }

            // Layer 3: Workspace config
            string workspacePath = Path.Combine(workspaceRoot, WorkspaceConfigFileName);
            SeeSharpConfig? workspaceConfig = TryLoadConfigFile(workspacePath, "workspace");
            if (workspaceConfig is not null)
            {
                resolved.WorkspaceConfigPath = workspacePath;
                MergeInto(resolved, workspaceConfig);
            }

            // Layer 4: Environment variable overrides
            ApplyEnvironmentOverrides(resolved);

            // Final validation pass: clamp all values to safe ranges
            Validate(resolved);

            return resolved;
        }

        /// <summary>
        /// Generates a starter workspace config file at the given path.
        /// Returns true if the file was created, false if it already exists.
        /// </summary>
        public static bool GenerateStarterConfig(string workspaceRoot)
        {
            string path = Path.Combine(workspaceRoot, WorkspaceConfigFileName);
            if (File.Exists(path))
                return false;

            var starter = new SeeSharpConfig
            {
                Schema = "./seesharp.config.schema.json",
                Comment = "SeeSharp instance configuration. Edit this file or ask the agent to modify it.",
                AgentName = "SeeSharp",
                PreferredModel = null,
                Limits = new AgentLimitsConfig
                {
                    MaxAgentTurnsPerTask = 28,
                    MaxToolCallsPerTurn = 2,
                    MaxSuccessfulToolExecutionsPerTask = 22,
                    BashCommandTimeoutSeconds = 30,
                    ResponsesApiCallTimeoutMinutes = 10
                },
                SystemPrompt = new SystemPromptConfig(),
                Contextualizer = new ContextualizerConfig
                {
                    MaxFilesToRead = 15,
                    MaxCharsPerFile = 48_000,
                    ExcludedDirectories = new List<string> { "bin", "obj", ".git", "node_modules" },
                    PinnedFiles = new List<string> { "Program.cs" }
                },
                CustomTools = new List<CustomToolDefinition>(),
                DisabledTools = new List<string>(),
                Theme = new ConsoleThemeConfig
                {
                    AgentColor = "DarkCyan",
                    ToolColor = "Green",
                    ErrorColor = "Red"
                }
            };

            string json = JsonSerializer.Serialize(starter, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });

            File.WriteAllText(path, json);
            return true;
        }

        /// <summary>
        /// Persists a config change to the specified scope file (atomic write).
        /// </summary>
        public static void SaveConfig(SeeSharpConfig config, string filePath)
        {
            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });

            string tempPath = filePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, filePath, overwrite: true);
        }

        /// <summary>
        /// Loads a raw SeeSharpConfig from a file path (used by CONFIG_EDIT tool for read-modify-write).
        /// Returns null on any failure.
        /// </summary>
        public static SeeSharpConfig? LoadRawConfig(string filePath)
        {
            return TryLoadConfigFile(filePath, Path.GetFileName(filePath));
        }

        public static string GetGlobalConfigPath()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, GlobalConfigDirectoryName, GlobalConfigFileName);
        }

        public static string GetWorkspaceConfigPath(string workspaceRoot)
        {
            return Path.Combine(workspaceRoot, WorkspaceConfigFileName);
        }

        private static SeeSharpConfig? TryLoadConfigFile(string path, string label)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;

            try
            {
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                    return null;

                var config = JsonSerializer.Deserialize<SeeSharpConfig>(json, s_jsonOptions);
                if (config is not null)
                {
                    ThemedConsole.WriteLine(TerminalTone.Reasoning,
                        $"[Config] Loaded {label} config from: {path}");
                }
                return config;
            }
            catch (JsonException ex)
            {
                ThemedConsole.WriteLine(TerminalTone.Error,
                    $"[Config] Failed to parse {label} config at {path}: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                ThemedConsole.WriteLine(TerminalTone.Error,
                    $"[Config] Failed to read {label} config at {path}: {ex.Message}");
                return null;
            }
        }

        private static void MergeInto(ResolvedConfig resolved, SeeSharpConfig layer)
        {
            if (!string.IsNullOrWhiteSpace(layer.AgentName))
                resolved.AgentName = layer.AgentName;

            if (!string.IsNullOrWhiteSpace(layer.PreferredModel))
                resolved.PreferredModel = layer.PreferredModel;

            MergeLimits(resolved, layer.Limits);
            MergeSystemPrompt(resolved, layer.SystemPrompt);
            MergeContextualizer(resolved, layer.Contextualizer);
            MergeCustomTools(resolved, layer.CustomTools);
            MergeDisabledTools(resolved, layer.DisabledTools);
            MergeTheme(resolved, layer.Theme);
        }

        private static void MergeLimits(ResolvedConfig resolved, AgentLimitsConfig? limits)
        {
            if (limits is null) return;

            if (limits.MaxAgentTurnsPerTask.HasValue)
                resolved.MaxAgentTurnsPerTask = limits.MaxAgentTurnsPerTask.Value;
            if (limits.MaxToolCallsPerTurn.HasValue)
                resolved.MaxToolCallsPerTurn = limits.MaxToolCallsPerTurn.Value;
            if (limits.MaxSuccessfulToolExecutionsPerTask.HasValue)
                resolved.MaxSuccessfulToolExecutionsPerTask = limits.MaxSuccessfulToolExecutionsPerTask.Value;
            if (limits.BashCommandTimeoutSeconds.HasValue)
                resolved.BashCommandTimeout = TimeSpan.FromSeconds(limits.BashCommandTimeoutSeconds.Value);
            if (limits.ResponsesApiCallTimeoutMinutes.HasValue)
                resolved.ResponsesApiCallTimeout = TimeSpan.FromMinutes(limits.ResponsesApiCallTimeoutMinutes.Value);
            if (limits.ContextualizerCallTimeoutMinutes.HasValue)
                resolved.ContextualizerCallTimeout = TimeSpan.FromMinutes(limits.ContextualizerCallTimeoutMinutes.Value);
            if (limits.MaxNoProgressTurns.HasValue)
                resolved.MaxNoProgressTurns = limits.MaxNoProgressTurns.Value;
            if (limits.MaxResponseRetries.HasValue)
                resolved.MaxResponseRetries = limits.MaxResponseRetries.Value;
        }

        private static void MergeSystemPrompt(ResolvedConfig resolved, SystemPromptConfig? prompt)
        {
            if (prompt is null) return;

            if (prompt.Prepend is not null)
                resolved.SystemPromptPrepend = prompt.Prepend;
            if (prompt.Append is not null)
                resolved.SystemPromptAppend = prompt.Append;
            if (prompt.Replace is not null)
                resolved.SystemPromptReplace = prompt.Replace;
        }

        private static void MergeContextualizer(ResolvedConfig resolved, ContextualizerConfig? ctx)
        {
            if (ctx is null) return;

            if (ctx.MaxFilesToRead.HasValue)
                resolved.ContextualizerMaxFilesToRead = ctx.MaxFilesToRead.Value;
            if (ctx.MaxCharsPerFile.HasValue)
                resolved.ContextualizerMaxCharsPerFile = ctx.MaxCharsPerFile.Value;
            if (ctx.ExcludedDirectories is { Count: > 0 })
                resolved.ContextualizerExcludedDirectories = new List<string>(ctx.ExcludedDirectories);
            if (ctx.PinnedFiles is { Count: > 0 })
                resolved.ContextualizerPinnedFiles = new List<string>(ctx.PinnedFiles);
            if (!string.IsNullOrWhiteSpace(ctx.PreferredModel))
                resolved.ContextualizerPreferredModel = ctx.PreferredModel;
        }

        private static void MergeCustomTools(ResolvedConfig resolved, List<CustomToolDefinition>? tools)
        {
            if (tools is null || tools.Count == 0) return;

            foreach (var tool in tools)
            {
                if (string.IsNullOrWhiteSpace(tool.Name))
                    continue;

                // Later layers override earlier tools with the same name
                resolved.CustomTools.RemoveAll(t =>
                    string.Equals(t.Name, tool.Name, StringComparison.OrdinalIgnoreCase));
                resolved.CustomTools.Add(tool);
            }
        }

        private static void MergeDisabledTools(ResolvedConfig resolved, List<string>? disabled)
        {
            if (disabled is null || disabled.Count == 0) return;

            foreach (string name in disabled)
            {
                if (!string.IsNullOrWhiteSpace(name))
                    resolved.DisabledTools.Add(name.Trim());
            }
        }

        private static void MergeTheme(ResolvedConfig resolved, ConsoleThemeConfig? theme)
        {
            if (theme is null) return;

            if (TryParseColor(theme.AgentColor, out ConsoleColor c))
                resolved.AgentColor = c;
            if (TryParseColor(theme.ToolColor, out c))
                resolved.ToolColor = c;
            if (TryParseColor(theme.ErrorColor, out c))
                resolved.ErrorColor = c;
            if (TryParseColor(theme.ReasoningColor, out c))
                resolved.ReasoningColor = c;
            if (TryParseColor(theme.UserColor, out c))
                resolved.UserColor = c;
            if (TryParseColor(theme.ActionColor, out c))
                resolved.ActionColor = c;
        }

        private static void ApplyEnvironmentOverrides(ResolvedConfig resolved)
        {
            string? bashTimeout = Environment.GetEnvironmentVariable("SEESHARP_BASH_TIMEOUT_SECONDS");
            if (int.TryParse(bashTimeout, out int bt) && bt > 0)
                resolved.BashCommandTimeout = TimeSpan.FromSeconds(bt);

            string? maxTurns = Environment.GetEnvironmentVariable("SEESHARP_MAX_AGENT_TURNS");
            if (int.TryParse(maxTurns, out int mt) && mt > 0)
                resolved.MaxAgentTurnsPerTask = mt;

            string? maxTools = Environment.GetEnvironmentVariable("SEESHARP_MAX_TOOL_CALLS_PER_TURN");
            if (int.TryParse(maxTools, out int tc) && tc > 0)
                resolved.MaxToolCallsPerTurn = tc;

            string? agentName = Environment.GetEnvironmentVariable("SEESHARP_AGENT_NAME");
            if (!string.IsNullOrWhiteSpace(agentName))
                resolved.AgentName = agentName;
        }

        private static void Validate(ResolvedConfig resolved)
        {
            resolved.MaxAgentTurnsPerTask = Clamp(resolved.MaxAgentTurnsPerTask, 1, 200);
            resolved.MaxToolCallsPerTurn = Clamp(resolved.MaxToolCallsPerTurn, 1, 10);
            resolved.MaxSuccessfulToolExecutionsPerTask = Clamp(resolved.MaxSuccessfulToolExecutionsPerTask, 1, 100);
            resolved.MaxNoProgressTurns = Clamp(resolved.MaxNoProgressTurns, 1, 20);
            resolved.MaxResponseRetries = Clamp(resolved.MaxResponseRetries, 1, 10);
            resolved.ContextualizerMaxFilesToRead = Clamp(resolved.ContextualizerMaxFilesToRead, 1, 50);
            resolved.ContextualizerMaxCharsPerFile = Clamp(resolved.ContextualizerMaxCharsPerFile, 500, 200_000);

            if (resolved.BashCommandTimeout < TimeSpan.FromSeconds(5))
                resolved.BashCommandTimeout = TimeSpan.FromSeconds(5);
            if (resolved.BashCommandTimeout > TimeSpan.FromMinutes(10))
                resolved.BashCommandTimeout = TimeSpan.FromMinutes(10);

            if (resolved.ResponsesApiCallTimeout < TimeSpan.FromSeconds(30))
                resolved.ResponsesApiCallTimeout = TimeSpan.FromSeconds(30);
            if (resolved.ResponsesApiCallTimeout > TimeSpan.FromMinutes(60))
                resolved.ResponsesApiCallTimeout = TimeSpan.FromMinutes(60);

            if (resolved.ContextualizerCallTimeout < TimeSpan.FromSeconds(30))
                resolved.ContextualizerCallTimeout = TimeSpan.FromSeconds(30);
            if (resolved.ContextualizerCallTimeout > TimeSpan.FromMinutes(60))
                resolved.ContextualizerCallTimeout = TimeSpan.FromMinutes(60);

            // Remove custom tools with empty names or descriptions
            resolved.CustomTools.RemoveAll(t =>
                string.IsNullOrWhiteSpace(t.Name) || string.IsNullOrWhiteSpace(t.Description));

            // Normalize custom tool names to uppercase
            foreach (var tool in resolved.CustomTools)
            {
                tool.Name = tool.Name.Trim().ToUpperInvariant();
            }
        }

        private static bool TryParseColor(string? value, out ConsoleColor color)
        {
            color = default;
            if (string.IsNullOrWhiteSpace(value))
                return false;
            return Enum.TryParse(value.Trim(), ignoreCase: true, out color);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
