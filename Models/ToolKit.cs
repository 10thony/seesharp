using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SeeSharp.Models
{

    public class LMStudioToolKit : ToolKit
    {
        
    }

    public abstract class ToolKit
    {
        /// <summary>
        /// This is how Agents get information about what tools they have access to and how to use them.
        /// Whenever you add a new tool, add a new entry in the dictionary returned by GetToolkitInformation()
        /// and make sure to include the tool name in the system prompt generation in Agent.cs so that the agent knows it can use the tool.
        /// </summary>
        /// <returns></returns>
        public string GenerateToolRegistryAsString()
        {
            Dictionary<string, string> toolInfo = GetToolkitInformation();

            StringBuilder sb = new StringBuilder();

            foreach (string info in toolInfo.Keys)
            {
                //tool name is the key, description is the value.

                string description = toolInfo[info];

                sb.AppendLine($"Tool Name: {info}, Tool Description: {description}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// register new tools here and put the tool names in a string variable at the top where we declare global variables.
        /// </summary>
        public Dictionary<string, string> GetToolkitInformation()
        {

            Dictionary<string, string> result = new Dictionary<string, string>();

            result.Add(AgentDefaults.READ_TOOL_NAME,
                "[DEPRECATED: prefer BASH] Gets the full content of a file. Relative filenames are resolved from the project/workspace root " +
                "(folder with the .csproj), not the executable folder.\r\n    :param filename: Path to the file.\r\n    " +
                ":return: The full content of the file.");

            result.Add(AgentDefaults.LIST_FILE_TOOL_NAME,
                "[DEPRECATED: prefer BASH] Lists files in a directory. Relative paths are resolved from the project/workspace root.\r\n    " +
                ":param directory: Path of the directory to list.\r\n    :return: A list of file paths.");

            result.Add(AgentDefaults.EDIT_FILE_TOOL_NAME,
                "[DEPRECATED: prefer BASH] Writes or edits a file on disk. Relative path uses the workspace root.\r\n" +
                "    :param path: File to write.\r\n" +
                "    :param oldContents: Exact substring to replace (copy from READ_FILE tool_result). " +
                "Use empty string \"\" to replace the entire file with newContents.\r\n" +
                "    :param newContents: New text (full file when oldContents is empty;" +
                " otherwise replaces the first occurrence of oldContents).\r\n" +
                "    :return: Edit confirmation.");

            result.Add(AgentDefaults.WEB_CALL_TOOL_NAME,
                "Performs a web call foundation request (currently GET only).\r\n" +
                "    :param url: Absolute URL (http/https).\r\n" +
                "    :return: Status code, response headers, and body preview.");

            result.Add(AgentDefaults.BASH_TOOL_NAME,
                "Runs a shell command in the workspace root (preferred for reading/searching/editing/listing).\r\n" +
                "    :param command: Command line to execute.\r\n" +
                "    :param workingDirectory: Optional relative/absolute working directory.\r\n" +
                "    :return: Exit code, stdout, stderr.");

            return result;
        }

        [Obsolete("LIST_FILE is deprecated. Prefer BASH for listing/searching files.")]
        public Dictionary<string, object> ListFiles_Tool(string fullPath)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();
            ThemedConsole.WriteLine(TerminalTone.Tool, $"[Tool]: Listing files in directory at:");
            ThemedConsole.WriteLine(TerminalTone.Tool, fullPath);
            try
            {
                var files = Directory.GetFiles(fullPath);

                result = new Dictionary<string, object>()
        {
            { "file_path", fullPath },
            { "files", files }
        };
            }
            catch (Exception e)
            {
                ThemedConsole.WriteLine(TerminalTone.Error, $"Error listing files in directory: {fullPath}");
                ThemedConsole.WriteLine(TerminalTone.Error, e.Message);
                throw;
            }
            return result;
        }


        [Obsolete("READ_FILE is deprecated. Prefer BASH for file inspection.")]
        public Dictionary<string, object> ReadFile_Tool(string fullPath)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();

            ThemedConsole.WriteLine(TerminalTone.Tool, $"[Tool] Reading file: {fullPath}");

            try
            {
                var contents = System.IO.File.ReadAllText(fullPath);

                result = new Dictionary<string, object>()
                {
                    { "file_path", fullPath },
                    { "content", contents }
                };
            }
            catch (Exception ex)
            {
                ThemedConsole.WriteLine(TerminalTone.Error, $"Error reading file: {ex.Message}");
            }

            return result;
        }

        [Obsolete("EDIT_FILE is deprecated. Prefer BASH for editing flows.")]
        public Dictionary<string, object> EditFile_Tool(string fullPath, 
                                                 string oldContents, 
                                                 string newContents)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();

            if (string.IsNullOrEmpty(oldContents))
            {
                ThemedConsole.WriteLine(TerminalTone.Tool, $"[Tool]: Creating new file at: {fullPath}");
                System.IO.File.WriteAllText(fullPath, newContents, Encoding.UTF8);
                return new Dictionary<string, object>()
                {
                    { "path", fullPath },
                    { "action", "File Created" }
                };
            }

            var originalContents = System.IO.File.ReadAllText(fullPath, Encoding.UTF8);
            var fileIndex = originalContents.IndexOf(oldContents, StringComparison.Ordinal);


            if (fileIndex == -1)
            {
                return new Dictionary<string, object>()
                {
                    { "path", fullPath },
                    { "action", "Old contents not found. No changes made." }
                };
            }

            ThemedConsole.WriteLine(TerminalTone.Tool, $"[Tool]: Editing file at: {fullPath}");
            var editedContent = originalContents.Remove(fileIndex, oldContents.Length).Insert(fileIndex, newContents);
            System.IO.File.WriteAllText(fullPath, editedContent, Encoding.UTF8);

            result = new Dictionary<string, object>()
            {
                { "path", fullPath },
                { "action", "File Edited" }
            };
            return result;
        }

        public Dictionary<string, object> WebCall_Tool(string url)
        {
            var result = new Dictionary<string, object>();
            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    return new Dictionary<string, object>
                    {
                        { "error", "WEB_CALL requires an absolute http/https URL." }
                    };
                }

                ThemedConsole.WriteLine(TerminalTone.Tool, $"[Tool] WEB_CALL GET: {uri}");

                using HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                using HttpResponseMessage response = http.GetAsync(uri).GetAwaiter().GetResult();
                string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                if (body.Length > 25_000)
                {
                    body = body[..25_000] + "\n[...truncated...]";
                }

                result["url"] = uri.ToString();
                result["status_code"] = (int)response.StatusCode;
                result["reason"] = response.ReasonPhrase ?? "";
                result["content_type"] = response.Content.Headers.ContentType?.ToString() ?? "";
                result["body"] = body;
                return result;
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object>
                {
                    { "error", ex.Message }
                };
            }
        }

        public Dictionary<string, object> Bash_Tool(string command, string? workingDirectory = null)
        {
            try
            {
                return Bash_ToolAsync(command, workingDirectory).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object>
                {
                    { "error", ex.Message }
                };
            }
        }

        private static async Task<Dictionary<string, object>> Bash_ToolAsync(string command, string? workingDirectory)
        {
            string root = AgentUtilities.ResolveWorkspaceRoot();
            string resolvedWorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                ? root
                : AgentUtilities.ResolveAbsPath(workingDirectory);

            if (!Directory.Exists(resolvedWorkingDirectory))
            {
                return new Dictionary<string, object>
                {
                    { "error", $"Working directory does not exist: {resolvedWorkingDirectory}" }
                };
            }

            if (!AgentUtilities.IsPathUnderWorkspaceRoot(resolvedWorkingDirectory, root))
            {
                return new Dictionary<string, object>
                {
                    { "error", "Working directory must remain inside workspace root." }
                };
            }

            if (LooksLikeRawSqlCommand(command))
            {
                return new Dictionary<string, object>
                {
                    { "command", command },
                    { "working_directory", resolvedWorkingDirectory },
                    { "error", "Command looks like raw SQL, not a shell command. Write SQL to a .sql file first, then execute it with psql or docker compose exec." },
                    { "hint", RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                        ? "PowerShell example: @'CREATE TABLE ...;'@ | Set-Content CreateTable.sql"
                        : "POSIX example: printf 'CREATE TABLE ...;\\n' > create_table.sql" }
                };
            }

            if (LooksLikeEscapedSqlWriteCommand(command))
            {
                return new Dictionary<string, object>
                {
                    { "command", command },
                    { "working_directory", resolvedWorkingDirectory },
                    { "error", "SQL write command appears to embed escaped newline/string-literal SQL. Write raw multiline SQL content instead." },
                    { "hint", RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                        ? "Use PowerShell here-string: @'...SQL...'@ | Set-Content file.sql"
                        : "Use heredoc: cat <<'EOF' > file.sql" }
                };
            }

            string originalCommand = command;
            string? composeAutoCorrection = null;
            if (TryRewriteDockerComposeServiceCommand(command, resolvedWorkingDirectory, out string rewrittenCommand, out string correctionNote))
            {
                command = rewrittenCommand;
                composeAutoCorrection = correctionNote;
                ThemedConsole.WriteLine(TerminalTone.Tool, $"[Tool] BASH docker-compose autocorrect: {correctionNote}");
            }

            TimeSpan wallTimeout = AgentDefaults.BashCommandTimeout;
            CreateMissingWriteParentDirectories(command, resolvedWorkingDirectory, root);
            ThemedConsole.WriteLine(
                TerminalTone.Tool,
                $"[Tool] BASH started (max {wallTimeout.TotalSeconds:F0}s): {command}");

            ProcessStartInfo startInfo = CreateShellStartInfo(command, resolvedWorkingDirectory);
            using CancellationTokenSource telemetryCts = new();
            DateTime startUtc = DateTime.UtcNow;
            Task telemetryTask = Task.Run(
                () => BashProgressTelemetryAsync(telemetryCts.Token, startUtc, wallTimeout),
                CancellationToken.None);

            try
            {
                using Process process = new() { StartInfo = startInfo };
                process.Start();

                // Read stdout and stderr concurrently to avoid classic redirected-pipe deadlocks.
                Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
                Task<string> stderrTask = process.StandardError.ReadToEndAsync();
                Task waitTask = process.WaitForExitAsync();
                Task allWork = Task.WhenAll(stdoutTask, stderrTask, waitTask);

                Task delayTask = Task.Delay(wallTimeout);
                Task finished = await Task.WhenAny(allWork, delayTask).ConfigureAwait(false);
                if (finished != allWork)
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    try
                    {
                        await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(TimeSpan.FromSeconds(3))
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                    }

                    return new Dictionary<string, object>
                    {
                        { "error", $"BASH command timed out after {wallTimeout.TotalSeconds:F0}s." },
                        { "working_directory", resolvedWorkingDirectory }
                    };
                }

                string stdout = await stdoutTask.ConfigureAwait(false);
                string stderr = await stderrTask.ConfigureAwait(false);
                ThemedConsole.WriteLine(
                    TerminalTone.Tool,
                    $"[Tool] BASH finished in {(DateTime.UtcNow - startUtc).TotalSeconds:F1}s (exit {process.ExitCode})");

                return new Dictionary<string, object>
                {
                    { "command", command },
                    { "original_command", originalCommand },
                    { "working_directory", resolvedWorkingDirectory },
                    { "shell", GetNativeShellDisplayName() },
                    { "shell_family", RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "powershell" : "posix" },
                    { "ok", process.ExitCode == 0 },
                    { "exit_code", process.ExitCode },
                    { "summary", process.ExitCode == 0
                        ? $"Command succeeded in {(DateTime.UtcNow - startUtc).TotalSeconds:F1}s."
                        : $"Command failed with exit code {process.ExitCode}." },
                    { "auto_correction", composeAutoCorrection ?? "" },
                    { "sql_format_warnings", BuildSqlFormatWarnings(command, resolvedWorkingDirectory) },
                    { "stdout", stdout.Length > 30_000 ? stdout[..30_000] + "\n[...truncated...]" : stdout },
                    { "stderr", stderr.Length > 30_000 ? stderr[..30_000] + "\n[...truncated...]" : stderr }
                };
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

        private static bool LooksLikeRawSqlCommand(string command) =>
            !string.IsNullOrWhiteSpace(command) &&
            Regex.IsMatch(command.TrimStart(), @"^(CREATE|INSERT|UPDATE|DELETE|ALTER|DROP|TRUNCATE)\b", RegexOptions.IgnoreCase);

        private static bool LooksLikeEscapedSqlWriteCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return false;
            }

            bool writesFile = Regex.IsMatch(
                command,
                @"\b(Set-Content|Add-Content|Out-File)\b|>>?|tee\b",
                RegexOptions.IgnoreCase);
            if (!writesFile)
            {
                return false;
            }

            bool sqlLike = Regex.IsMatch(
                command,
                @"\b(CREATE\s+TABLE|INSERT\s+INTO|ALTER\s+TABLE|DROP\s+TABLE|REFERENCES)\b",
                RegexOptions.IgnoreCase);
            if (!sqlLike)
            {
                return false;
            }

            // Guard against literal \n SQL blobs in quoted strings.
            return command.Contains("\\n", StringComparison.Ordinal);
        }

        private static string BuildSqlFormatWarnings(string command, string workingDirectory)
        {
            string? sqlPath = TryExtractSqlPathFromWriteCommand(command, workingDirectory);
            if (string.IsNullOrWhiteSpace(sqlPath) || !File.Exists(sqlPath))
            {
                return "";
            }

            string content;
            try
            {
                content = File.ReadAllText(sqlPath);
            }
            catch
            {
                return "";
            }

            List<string> warnings = new();
            if (content.Contains("\\n", StringComparison.Ordinal))
            {
                warnings.Add("Contains literal '\\n' sequences; expected real line breaks.");
            }

            if (!content.EndsWith('\n'))
            {
                warnings.Add("Missing trailing newline at end of file.");
            }

            // Catch single-line SQL blobs that should be multiline.
            if ((content.Contains("CREATE TABLE", StringComparison.OrdinalIgnoreCase) ||
                 content.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase)) &&
                !content.Contains('\n'))
            {
                warnings.Add("SQL appears in a single line; expected formatted multiline SQL.");
            }

            return string.Join(" ", warnings);
        }

        private static string? TryExtractSqlPathFromWriteCommand(string command, string workingDirectory)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return null;
            }

            // PowerShell style: -Path "file.sql"
            Match psPath = Regex.Match(
                command,
                @"-Path\s+(?<p>""[^""]+\.sql""|'[^']+\.sql'|[^\s;|&]+\.sql)",
                RegexOptions.IgnoreCase);
            if (psPath.Success)
            {
                return ResolveCommandPath(psPath.Groups["p"].Value, workingDirectory);
            }

            // Redirection style: > file.sql or >> file.sql
            Match redirPath = Regex.Match(
                command,
                @">>?\s*(?<p>""[^""]+\.sql""|'[^']+\.sql'|[^\s;|&]+\.sql)",
                RegexOptions.IgnoreCase);
            if (redirPath.Success)
            {
                return ResolveCommandPath(redirPath.Groups["p"].Value, workingDirectory);
            }

            return null;
        }

        private static string ResolveCommandPath(string rawPath, string workingDirectory)
        {
            string cleaned = rawPath.Trim().Trim('"', '\'');
            return Path.IsPathRooted(cleaned)
                ? Path.GetFullPath(cleaned)
                : Path.GetFullPath(Path.Combine(workingDirectory, cleaned));
        }

        private static bool TryRewriteDockerComposeServiceCommand(
            string command,
            string resolvedWorkingDirectory,
            out string rewritten,
            out string note)
        {
            rewritten = command;
            note = "";
            if (string.IsNullOrWhiteSpace(command))
            {
                return false;
            }

            Match composeMatch = Regex.Match(
                command,
                @"^\s*docker\s+compose\s+(?<sub>[a-zA-Z]+)\b(?<rest>.*)$",
                RegexOptions.IgnoreCase);
            if (!composeMatch.Success)
            {
                return false;
            }

            string sub = composeMatch.Groups["sub"].Value.ToLowerInvariant();
            if (!sub.Equals("up", StringComparison.Ordinal) &&
                !sub.Equals("start", StringComparison.Ordinal) &&
                !sub.Equals("stop", StringComparison.Ordinal) &&
                !sub.Equals("restart", StringComparison.Ordinal) &&
                !sub.Equals("exec", StringComparison.Ordinal) &&
                !sub.Equals("logs", StringComparison.Ordinal) &&
                !sub.Equals("ps", StringComparison.Ordinal))
            {
                return false;
            }

            if (!TryBuildDockerComposeAliasMap(resolvedWorkingDirectory, out Dictionary<string, string> aliasToService))
            {
                return false;
            }

            string? token = TryExtractLikelyComposeServiceToken(command, sub);
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            string trimmedToken = token.Trim().Trim('"', '\'');
            if (string.IsNullOrWhiteSpace(trimmedToken))
            {
                return false;
            }

            if (aliasToService.ContainsKey(trimmedToken))
            {
                return false;
            }

            string normalizedToken = NormalizeAlias(trimmedToken);
            if (string.IsNullOrWhiteSpace(normalizedToken))
            {
                return false;
            }

            string? mappedService = null;
            if (aliasToService.TryGetValue(normalizedToken, out string? exactMapped))
            {
                mappedService = exactMapped;
            }
            else
            {
                List<string> fuzzyMatches = aliasToService
                    .Where(kvp =>
                    {
                        string keyNorm = NormalizeAlias(kvp.Key);
                        return keyNorm.Contains(normalizedToken, StringComparison.OrdinalIgnoreCase) ||
                               normalizedToken.Contains(keyNorm, StringComparison.OrdinalIgnoreCase);
                    })
                    .Select(kvp => kvp.Value)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (fuzzyMatches.Count == 1)
                {
                    mappedService = fuzzyMatches[0];
                }
            }

            if (string.IsNullOrWhiteSpace(mappedService))
            {
                return false;
            }

            rewritten = Regex.Replace(
                command,
                $@"\b{Regex.Escape(trimmedToken)}\b",
                mappedService,
                RegexOptions.IgnoreCase,
                TimeSpan.FromMilliseconds(200));
            note = $"service '{trimmedToken}' -> '{mappedService}' from docker-compose aliases";
            return !string.Equals(rewritten, command, StringComparison.Ordinal);
        }

        private static bool TryBuildDockerComposeAliasMap(
            string resolvedWorkingDirectory,
            out Dictionary<string, string> aliasToService)
        {
            aliasToService = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string yml = Path.Combine(resolvedWorkingDirectory, "docker-compose.yml");
            string yaml = Path.Combine(resolvedWorkingDirectory, "docker-compose.yaml");
            string composePath = File.Exists(yml) ? yml : (File.Exists(yaml) ? yaml : "");
            if (string.IsNullOrWhiteSpace(composePath))
            {
                return false;
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(composePath);
            }
            catch
            {
                return false;
            }

            bool inServices = false;
            string? currentService = null;
            foreach (string raw in lines)
            {
                string line = raw.Replace("\t", "    ");
                string trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                int indent = line.Length - line.TrimStart().Length;
                if (!inServices)
                {
                    if (trimmed.Equals("services:", StringComparison.OrdinalIgnoreCase))
                    {
                        inServices = true;
                    }
                    continue;
                }

                if (indent == 0 && !trimmed.Equals("services:", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (indent == 2 && trimmed.EndsWith(":", StringComparison.Ordinal))
                {
                    currentService = trimmed[..^1].Trim();
                    if (!string.IsNullOrWhiteSpace(currentService))
                    {
                        AddComposeAlias(aliasToService, currentService, currentService);
                    }
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(currentService) &&
                    indent >= 4 &&
                    trimmed.StartsWith("container_name:", StringComparison.OrdinalIgnoreCase))
                {
                    string containerName = trimmed["container_name:".Length..].Trim().Trim('"', '\'');
                    if (!string.IsNullOrWhiteSpace(containerName))
                    {
                        AddComposeAlias(aliasToService, containerName, currentService);
                    }
                }
            }

            return aliasToService.Count > 0;
        }

        private static void AddComposeAlias(Dictionary<string, string> aliasToService, string alias, string service)
        {
            if (string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(service))
            {
                return;
            }

            aliasToService[alias] = service;
            string normalized = NormalizeAlias(alias);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                aliasToService[normalized] = service;
            }
        }

        private static string NormalizeAlias(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return "";
            }

            StringBuilder sb = new StringBuilder(input.Length);
            foreach (char c in input.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static string? TryExtractLikelyComposeServiceToken(string command, string subCommand)
        {
            // Tokenize while preserving simple quoted groups.
            MatchCollection tokenMatches = Regex.Matches(command, @"(""[^""]*""|'[^']*'|\S+)");
            List<string> tokens = tokenMatches.Select(m => m.Value).ToList();
            int composeIdx = tokens.FindIndex(t => t.Equals("compose", StringComparison.OrdinalIgnoreCase));
            if (composeIdx < 0 || composeIdx + 1 >= tokens.Count)
            {
                return null;
            }

            int idx = composeIdx + 2; // first token after subcommand
            while (idx < tokens.Count)
            {
                string t = tokens[idx];
                if (t.StartsWith("-", StringComparison.Ordinal))
                {
                    // Skip flag value when likely present.
                    if ((t.Equals("-f", StringComparison.OrdinalIgnoreCase) ||
                         t.Equals("--file", StringComparison.OrdinalIgnoreCase) ||
                         t.Equals("--project-name", StringComparison.OrdinalIgnoreCase) ||
                         t.Equals("-p", StringComparison.OrdinalIgnoreCase)) &&
                        idx + 1 < tokens.Count)
                    {
                        idx += 2;
                        continue;
                    }

                    idx++;
                    continue;
                }

                // For `docker compose ps` without args, no service token exists.
                if (subCommand.Equals("ps", StringComparison.OrdinalIgnoreCase) &&
                    t.Contains("=", StringComparison.Ordinal))
                {
                    return null;
                }

                return t.Trim('"', '\'');
            }

            return null;
        }

        private static async Task BashProgressTelemetryAsync(
            CancellationToken cancellationToken,
            DateTime startUtc,
            TimeSpan maxDuration)
        {
            TimeSpan interval = AgentDefaults.BashTelemetryInterval;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                double elapsed = (DateTime.UtcNow - startUtc).TotalSeconds;
                if (elapsed >= maxDuration.TotalSeconds)
                {
                    return;
                }

                ThemedConsole.WriteLine(
                    TerminalTone.Tool,
                    $"[Tool] BASH still running… ({elapsed:F0}s / {maxDuration.TotalSeconds:F0}s max)");
            }
        }

        private static ProcessStartInfo CreateShellStartInfo(string command, string workingDirectory)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Prefer PowerShell 7 when available, then fall back to Windows PowerShell.
                string shell = ResolveFirstAvailableCommand("pwsh") ? "pwsh" : "powershell";
                return new ProcessStartInfo
                {
                    FileName = shell,
                    Arguments = $"-NoProfile -Command \"{command.Replace("\"", "\\\"")}\"",
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
            }

            string shellPath =
                File.Exists("/bin/bash") ? "/bin/bash" :
                File.Exists("/bin/zsh") ? "/bin/zsh" :
                File.Exists("/bin/sh") ? "/bin/sh" :
                ResolveFirstAvailableCommand("bash") ? "bash" :
                ResolveFirstAvailableCommand("zsh") ? "zsh" :
                "sh";

            string shellArguments = shellPath.EndsWith("sh", StringComparison.OrdinalIgnoreCase) &&
                                    !shellPath.EndsWith("bash", StringComparison.OrdinalIgnoreCase) &&
                                    !shellPath.EndsWith("zsh", StringComparison.OrdinalIgnoreCase)
                ? $"-c \"{command.Replace("\"", "\\\"")}\""
                : $"-lc \"{command.Replace("\"", "\\\"")}\"";

            return new ProcessStartInfo
            {
                FileName = shellPath,
                Arguments = shellArguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }

        private static string GetNativeShellDisplayName()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return ResolveFirstAvailableCommand("pwsh")
                    ? "PowerShell 7 (pwsh)"
                    : "Windows PowerShell";
            }

            if (File.Exists("/bin/bash") || ResolveFirstAvailableCommand("bash"))
            {
                return "bash";
            }

            if (File.Exists("/bin/zsh") || ResolveFirstAvailableCommand("zsh"))
            {
                return "zsh";
            }

            return "sh";
        }

        /// <summary>
        /// Best-effort guard: create parent directories when command writes to a redirected file.
        /// This avoids common failures like `> sql/file.sql` when `sql/` does not exist.
        /// </summary>
        private static void CreateMissingWriteParentDirectories(
            string command,
            string resolvedWorkingDirectory,
            string workspaceRoot)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return;
            }

            foreach (string target in ExtractRedirectTargetPaths(command))
            {
                if (string.IsNullOrWhiteSpace(target))
                {
                    continue;
                }

                string normalized = target.Trim().Trim('\'', '"');
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    continue;
                }

                // Ignore obvious stream redirects and dynamic expressions.
                if (normalized.StartsWith("&", StringComparison.Ordinal) ||
                    normalized.StartsWith("$", StringComparison.Ordinal))
                {
                    continue;
                }

                string fullPath = Path.IsPathRooted(normalized)
                    ? Path.GetFullPath(normalized)
                    : Path.GetFullPath(Path.Combine(resolvedWorkingDirectory, normalized));

                string? parent = Path.GetDirectoryName(fullPath);
                if (string.IsNullOrWhiteSpace(parent))
                {
                    continue;
                }

                if (!AgentUtilities.IsPathUnderWorkspaceRoot(parent, workspaceRoot))
                {
                    continue;
                }

                try
                {
                    Directory.CreateDirectory(parent);
                }
                catch
                {
                    // Non-fatal; command execution will surface a concrete error if write fails.
                }
            }
        }

        private static IEnumerable<string> ExtractRedirectTargetPaths(string command)
        {
            // Matches:
            //   > path
            //   >> path
            //   1> path
            //   2>> path
            // Stops at separators ; | & or line breaks.
            MatchCollection matches = Regex.Matches(
                command,
                @"(?:^|[\s;])(?:\d)?>>?\s*(?<path>(?:""[^""]+""|'[^']+'|[^\s;|&]+))",
                RegexOptions.Multiline);

            foreach (Match match in matches)
            {
                string value = match.Groups["path"].Value;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value;
                }
            }
        }

        private static bool ResolveFirstAvailableCommand(string command)
        {
            try
            {
                using Process probe = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "where" : "which",
                        Arguments = command,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                probe.Start();
                probe.WaitForExit(2_000);
                return probe.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }


        public List<(string toolName, Dictionary<string, object?> args)> ExtractToolCallInvocations(string llmResposneText)
        {

            List<(string toolName, Dictionary<string, object?> args)> toolCalls = new List<(string toolName, Dictionary<string, object?> args)>();
            foreach (string rawLine in llmResposneText.Trim().Split(
                         new[] { '\r', '\n' },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                string line = rawLine.Trim();
                if (!line.StartsWith("tool:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;

                }

                try
                {
                    var after = line["tool:".Length..].Trim();
                    var openParenIndex = after.IndexOf('(');
                    var closeParenIndex = after.LastIndexOf(')');

                    if (openParenIndex == -1 || closeParenIndex <= openParenIndex)
                        continue;

                    var name = after[..openParenIndex].Trim();
                    var rest = after[(openParenIndex + 1)..closeParenIndex];
                    var jsonStr = AgentUtilities.NormalizeToolCallJson(rest.Trim());
                    if (AgentUtilities.IsPlaceholderToolJson(jsonStr))
                    {
                        ThemedConsole.WriteLine(TerminalTone.Default, $"Skipping tool line (placeholder, not real JSON): {line}");
                        continue;
                    }

                    var args = ParseToolArgs(name, jsonStr);

                    if (args is not null)
                        toolCalls.Add((name, args));
                }
                catch (Exception e)
                {
                    ThemedConsole.WriteLine(TerminalTone.Error, $"Error parsing tool call from line: {line}");
                    ThemedConsole.WriteLine(TerminalTone.Error, e.Message);
                }
            }

            return toolCalls;
        }

        private Dictionary<string, object?>? ParseToolArgs(string toolName, string payload)
        {
            JsonSerializerOptions options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, object?>>(payload, options);
            }
            catch
            {
                // fall through to lenient parsing for common malformed JSON tool calls.
            }

            string normalizedName = (toolName ?? "").Trim().ToUpperInvariant();
            Dictionary<string, object?> recovered = new(StringComparer.OrdinalIgnoreCase);
            if (normalizedName == AgentDefaults.BASH_TOOL_NAME)
            {
                string? command = TryExtractLenientStringArg(payload, "command");
                if (string.IsNullOrWhiteSpace(command))
                    return null;

                recovered["command"] = command;
                string? workingDirectory = TryExtractLenientStringArg(payload, "workingDirectory", "cwd", "directory");
                if (!string.IsNullOrWhiteSpace(workingDirectory))
                {
                    recovered["workingDirectory"] = workingDirectory;
                }
                return recovered;
            }

            if (normalizedName == AgentDefaults.WEB_CALL_TOOL_NAME)
            {
                string? url = TryExtractLenientStringArg(payload, "url");
                if (string.IsNullOrWhiteSpace(url))
                {
                    string trimmed = payload.Trim().Trim('"', '\'');
                    if (Uri.IsWellFormedUriString(trimmed, UriKind.Absolute))
                        url = trimmed;
                }

                if (!string.IsNullOrWhiteSpace(url))
                {
                    recovered["url"] = url;
                    return recovered;
                }
            }

            return null;
        }

        private static string? TryExtractLenientStringArg(string payload, params string[] keys)
        {
            foreach (string key in keys)
            {
                string quotedKey = $"\"{key}\"";
                int keyIndex = payload.IndexOf(quotedKey, StringComparison.OrdinalIgnoreCase);
                if (keyIndex < 0)
                    continue;

                int colonIndex = payload.IndexOf(':', keyIndex + quotedKey.Length);
                if (colonIndex < 0)
                    continue;

                string valuePortion = payload[(colonIndex + 1)..].TrimStart();
                if (string.IsNullOrEmpty(valuePortion))
                    continue;

                if (valuePortion[0] == '"')
                {
                    // Handles escaped quotes and fallback when models fail to escape inner quotes.
                    StringBuilder sb = new StringBuilder();
                    bool escaping = false;
                    for (int i = 1; i < valuePortion.Length; i++)
                    {
                        char c = valuePortion[i];
                        if (escaping)
                        {
                            sb.Append(c);
                            escaping = false;
                            continue;
                        }

                        if (c == '\\')
                        {
                            escaping = true;
                            continue;
                        }

                        if (c == '"')
                        {
                            if (i + 1 >= valuePortion.Length || valuePortion[i + 1] == ',' || valuePortion[i + 1] == '}')
                            {
                                return sb.ToString().Trim();
                            }
                        }

                        sb.Append(c);
                    }

                    return sb.ToString().Trim().TrimEnd('}', ')');
                }

                Match m = Regex.Match(valuePortion, @"^([^,}\r\n]+)");
                if (m.Success)
                {
                    return m.Groups[1].Value.Trim().Trim('"', '\'');
                }
            }

            return null;
        }

        public Dictionary<string, object> ExecuteToolInvocation(string toolName, Dictionary<string, object?> args)
        {
#pragma warning disable CS0618
            try
            {
                // Tool names come from the model as strings; normalize so casing/whitespace mismatches still dispatch.
                string key = (toolName ?? "").Trim().ToUpperInvariant();
               
                switch (key)
                {
                    case var actualToolName when key == AgentDefaults.LIST_FILE_TOOL_NAME:
                        return ListFiles_Tool(
                            AgentUtilities.ResolveAbsPath(
                                RequireArg(args, "directory", "path", "directoryPath")));
                    case var actualToolName when key == AgentDefaults.READ_TOOL_NAME:
                        return ReadFile_Tool(
                            AgentUtilities.ResolveAbsPath(
                                RequireArg(args, "filename", "fileName")));
                    case var actualToolName when key == AgentDefaults.EDIT_FILE_TOOL_NAME:
                        return EditFile_Tool(
                            AgentUtilities.ResolveAbsPath(RequireArg(args, "path")),
                            OptionalArg(args, "oldContents", "old_str", "oldStr") ?? "",
                            OptionalArg(args, "newContents", "new_str", "newStr") ?? "");
                    case var actualToolName when key == AgentDefaults.WEB_CALL_TOOL_NAME:
                        return WebCall_Tool(
                            RequireArg(args, "url"));
                    case var actualToolName when key == AgentDefaults.BASH_TOOL_NAME:
                        Dictionary<string, object> bashResult = Bash_Tool(
                            RequireArg(args, "command"),
                            OptionalArg(args, "workingDirectory", "cwd", "directory"));
                        if (bashResult.TryGetValue("exit_code", out object? exitCodeObj) &&
                            int.TryParse(exitCodeObj?.ToString(), out int exitCode) &&
                            exitCode != 0 &&
                            !bashResult.ContainsKey("error"))
                        {
                            string stderrText = bashResult.TryGetValue("stderr", out object? stderrObj)
                                ? (stderrObj?.ToString() ?? "")
                                : "";
                            string firstErrLine = stderrText
                                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                .FirstOrDefault() ?? "No stderr output.";
                            bashResult["error"] = $"BASH command failed (exit {exitCode}): {firstErrLine}";
                        }
                        return bashResult;

                    default:
                        return new Dictionary<string, object>
                    {
                        { "error", $"Unknown tool: {toolName}" }
                    };
                }
            }

            catch (Exception ex)
            {
                return new Dictionary<string, object>
                {
                    { "error", ex.Message }
                };
            }
#pragma warning restore CS0618
        }
        private string RequireArg(Dictionary<string, object?> args, params string[] keys)
        {
            string? v = OptionalArg(args, keys);
            if (string.IsNullOrEmpty(v))
                throw new ArgumentException($"Missing required argument; expected one of: {string.Join(", ", keys)}");
            return v;
        }

        private string? OptionalArg(Dictionary<string, object?> args, params string[] keys)
        {
            foreach (string key in keys)
            {
                if (!args.TryGetValue(key, out object? value) || value is null)
                    continue;
                if (value is JsonElement je)
                {
                    if (je.ValueKind == JsonValueKind.String)
                        return je.GetString();
                    return je.ToString();
                }
                return value.ToString();
            }
            return null;
        }


    }
}
