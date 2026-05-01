
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace SeeSharp.Models
{
    public static class AgentUtilities
    {
        private sealed class ContextualizerPickDto
        {
            public List<string>? Paths { get; set; }
        }

        public const int ContextualizerMaxListLines = 2500;
        public const int ContextualizerMaxFilesToRead = 15;
        public const int ContextualizerMaxCharsPerFile = 48_000;
        public static readonly JsonSerializerOptions s_contextualizerJson = new()
        { PropertyNameCaseInsensitive = true };

        static readonly HashSet<string> s_workspaceWalkExcludedDirs = 
            new(StringComparer.OrdinalIgnoreCase)
        {
            "bin", "obj", ".git", ".vs", ".idea", "node_modules", "openai-dotnet",
            ".dockerignore","tools"
        };

        static readonly HashSet<string> s_workspaceWalkSkippedExtensions = 
            new(StringComparer.OrdinalIgnoreCase)
        {
            ".dll", ".exe", ".pdb",
            ".png", ".jpg", ".jpeg",
            ".gif", ".webp", ".ico", 
            ".zip",".7z", ".tar", 
            ".gz",".pdf", ".woff", 
            ".woff2", ".ttf", ".eot", 
            ".mp4",".mp3", ".user",
            ".h"
        };

        /// <summary>
        /// Pinned first in the contextualizer’s file list so the model and fallbacks see entry points
        /// even when the sorted walk is long.
        /// </summary>
        static readonly string[] s_contextualizerPinnedRelativePaths =
        {
            "SeeSharp.csproj",
            "Program.cs",
            "Models/Agent.cs",
            "Models/ToolKit.cs",
            "Models/AgentUtilities.cs",
        };

        static readonly string[] s_contextualizerPreferredFileNames =
        {
            // Language/runtime manifests and workspace markers.
            "docker-compose.yml",
            "docker-compose.yaml",
            "dockerfile",
            "package.json",
            "pyproject.toml",
            "requirements.txt",
            "go.mod",
            "pom.xml",
            "*.csproj",
            "*.sln",
            "*.slnx",
            // Common app entrypoints.
            "program.cs",
            "main.py",
            "main.go",
            "main.rs",
            "app.py",
            "app.ts",
            "app.js",
            "index.ts",
            "index.js",
            // Helpful project context for non-code roots.
            "readme.md"
        };

        public static string GetChatCompletionText(ChatCompletion completion)
        {
            var sb = new StringBuilder();
            foreach (ChatMessageContentPart part in completion.Content)
            {
                if (part.Kind == ChatMessageContentPartKind.Text)
                    sb.Append(part.Text);
            }

            return sb.ToString().Trim();
        }

        public static string BuildFileListUserBlock(IReadOnlyList<string> allRelPaths,
                                     string? userTask)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Workspace-relative file paths (one per line):");
            int n = Math.Min(allRelPaths.Count, ContextualizerMaxListLines);
            for (int i = 0; i < n; i++)
                sb.AppendLine(allRelPaths[i]);
            if (allRelPaths.Count > ContextualizerMaxListLines)
                sb.AppendLine($"... and {allRelPaths.Count - ContextualizerMaxListLines}" +
                    $" more paths not shown.");

            if (!string.IsNullOrWhiteSpace(userTask))
            {
                sb.AppendLine();
                sb.AppendLine("User task (use to prioritize files):");
                sb.AppendLine(userTask.Trim());
            }

            return sb.ToString();
        }

        public static List<string> ParsePickedPaths(
            string pickRaw,
            string workspaceRoot,
            IReadOnlyList<string> catalog,
            out string? diagnostic)
        {
            diagnostic = null;
            var set = new HashSet<string>(catalog, StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(pickRaw))
            {
                diagnostic = "Pick step returned empty or whitespace-only text " +
                    "(no JSON to parse).";
                return new List<string>();
            }

            string json = StripMarkdownFence(pickRaw);
            try
            {
                var dto = JsonSerializer.Deserialize<ContextualizerPickDto>
                    (json, s_contextualizerJson);

                if (dto?.Paths is null || dto.Paths.Count == 0)
                {
                    diagnostic =
                        "JSON deserialized but \"paths\" is missing or empty. " +
                        $"Snippet: {TruncateForLog(json, 400)}";
                    return new List<string>();
                }

                var notInCatalog = new List<string>();
                var skippedNotUnderRoot = new List<string>();
                var result = new List<string>();
                foreach (string p in dto.Paths)
                {
                    if (string.IsNullOrWhiteSpace(p) || 
                        result.Count >= ContextualizerMaxFilesToRead)
                        break;

                    string norm = p.Trim().Replace('\\', '/');
                    if (!set.Contains(norm))
                    {
                        // Model may name a file that was omitted from the catalog (e.g. list cap) or a new
                        // file; accept if it is a real file under the workspace root.
                        string absMaybe = AgentUtilities.ResolveAbsPath(norm);
                        if (File.Exists(absMaybe) && 
                            AgentUtilities.IsPathUnderWorkspaceRoot(absMaybe, workspaceRoot))
                        {
                            result.Add(norm);
                            if (result.Count >= ContextualizerMaxFilesToRead)
                                break;
                        }
                        else if (notInCatalog.Count < 6)
                        {
                            notInCatalog.Add(norm);
                        }
                        continue;
                    }

                    string abs = AgentUtilities.ResolveAbsPath(norm);
                    if (!AgentUtilities.IsPathUnderWorkspaceRoot(abs, workspaceRoot) || 
                        !File.Exists(abs))
                    {
                        if (skippedNotUnderRoot.Count < 4)
                            skippedNotUnderRoot.Add(norm);
                        continue;
                    }

                    result.Add(norm);
                }

                if (result.Count == 0)
                {
                    diagnostic =
                        $"Model proposed {dto.Paths.Count} path(s); none were usable. " +
                        (notInCatalog.Count > 0
                            ? $"Not in workspace file list (examples): " +
                            $"{string.Join(", ", notInCatalog)}. "
                            : "") +
                        (skippedNotUnderRoot.Count > 0
                            ? $"Rejected path/workspace (examples): " +
                            $"{string.Join(", ", skippedNotUnderRoot)}. "
                            : "") +
                        $"Raw snippet: {TruncateForLog(json, 350)}";
                }

                return result;
            }
            catch (Exception ex)
            {
                diagnostic =
                    $"JSON parse failed: {ex.Message}. " +
                    $"After fence strip, snippet: {TruncateForLog(json, 400)}";
                return new List<string>();
            }
        }

        public static string TruncateForLog(string s, int maxChars)
        {
            if (string.IsNullOrEmpty(s))
                return "(empty)";
            string oneLine = s.Replace("\r", " ").Replace("\n", " ").Trim();
            if (oneLine.Length <= maxChars)
                return oneLine;
            return oneLine[..maxChars] + "…";
        }

        /// <summary>
        /// Truncates very long file text for the small contextualizer model by keeping the
        /// start and end (imports, type headers, and closing) so the middle can be dropped.
        /// </summary>
        public static string TruncateMiddleForModel(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
                return text;
            
            const string gap = "\n\n[... middle of file omitted for model context size ...]\n\n";
            if (maxChars < gap.Length + 120)
                return text[..maxChars];

            int inner = maxChars - gap.Length;
            int headLen = (inner * 2) / 3;
            int tailLen = inner - headLen;
            headLen = Math.Min(headLen, text.Length);
            tailLen = Math.Min(tailLen, Math.Max(0, text.Length - headLen));

            string head = text[..headLen];
            string tail = tailLen == 0 ? "" : text[^tailLen..];
            return head + gap + tail;
        }
        static string StripMarkdownFence(string raw)
        {
            string s = raw.Trim();
            if (!s.StartsWith("```", StringComparison.Ordinal))
                return s;

            int firstNl = s.IndexOf('\n');
            if (firstNl >= 0)
                s = s[(firstNl + 1)..];
            int fence = s.LastIndexOf("```", StringComparison.Ordinal);
            if (fence >= 0)
                s = s[..fence];
            return s.Trim();
        }
        /// <summary>
        /// Recursively lists files under the workspace root as POSIX-style relative paths,
        /// skipping build artifacts, VCS, IDE folders, and common binary extensions.
        /// </summary>
        public static IReadOnlyList<string> ListWorkspaceSourceFilesRelative()
        {
            string root = Path.GetFullPath(ResolveWorkspaceRoot());
            if (!Directory.Exists(root))
                return Array.Empty<string>();

            var list = new List<string>();
            foreach (string absFile in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(root, absFile);
                if (ShouldSkipWorkspaceRelativePath(rel))
                    continue;

                string ext = Path.GetExtension(absFile);
                if (s_workspaceWalkSkippedExtensions.Contains(ext))
                    continue;

                list.Add(rel.Replace('\\', '/'));
            }

            list.Sort(StringComparer.OrdinalIgnoreCase);
            return ReorderForContextualizerFileList(list);
        }

        /// <summary>
        /// Puts well-known project entry points at the start of the list sent to the pick model.
        /// </summary>
        public static List<string> ReorderForContextualizerFileList(IReadOnlyList<string> sortedRelPaths)
        {
            var inCatalog = new HashSet<string>(sortedRelPaths, StringComparer.OrdinalIgnoreCase);
            var ordered = new List<string>(sortedRelPaths.Count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string p in s_contextualizerPinnedRelativePaths)
            {
                if (inCatalog.Contains(p) && seen.Add(p))
                    ordered.Add(p);
            }

            foreach (string p in sortedRelPaths)
            {
                if (seen.Add(p))
                    ordered.Add(p);
            }

            return ordered;
        }

        /// <summary>
        /// When the model’s JSON pick names paths that are not in the catalog, use a small
        /// deterministic set of files that are actually indexed.
        /// </summary>
        public static List<string> GetContextualizerFallbackPicks(IReadOnlyList<string> catalog)
        {
            var set = new HashSet<string>(catalog, StringComparer.OrdinalIgnoreCase);
            var list = new List<string>(ContextualizerMaxFilesToRead);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string p in s_contextualizerPinnedRelativePaths)
            {
                if (set.Contains(p) && seen.Add(p))
                {
                    list.Add(p);
                    if (list.Count >= ContextualizerMaxFilesToRead)
                        break;
                }
            }

            if (list.Count < ContextualizerMaxFilesToRead)
            {
                foreach (string candidate in catalog)
                {
                    if (list.Count >= ContextualizerMaxFilesToRead)
                        break;

                    string fileName = Path.GetFileName(candidate);
                    if (!IsPreferredContextualizerName(fileName))
                        continue;

                    if (seen.Add(candidate))
                        list.Add(candidate);
                }
            }

            // Last-resort safety net for stacks with no known marker names.
            if (list.Count == 0)
            {
                foreach (string candidate in catalog)
                {
                    if (seen.Add(candidate))
                    {
                        list.Add(candidate);
                        if (list.Count >= ContextualizerMaxFilesToRead)
                            break;
                    }
                }
            }

            return list;
        }

        static bool IsPreferredContextualizerName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return false;

            foreach (string preferred in s_contextualizerPreferredFileNames)
            {
                if (preferred.StartsWith("*.", StringComparison.Ordinal))
                {
                    if (fileName.EndsWith(preferred[1..], StringComparison.OrdinalIgnoreCase))
                        return true;
                    continue;
                }

                if (string.Equals(fileName, preferred, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        static bool ShouldSkipWorkspaceRelativePath(string relativePath)
        {
            foreach (string segment in relativePath.Split(Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar))
            {
                if (string.IsNullOrEmpty(segment))
                    continue;
                if (s_workspaceWalkExcludedDirs.Contains(segment))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// True if <paramref name="absolutePath"/> is the workspace root or a path inside it.
        /// </summary>
        public static bool IsPathUnderWorkspaceRoot(string absolutePath, string workspaceRoot)
        {
            string root = Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar, 
                Path.AltDirectorySeparatorChar);
            string abs = Path.GetFullPath(absolutePath).TrimEnd(Path.DirectorySeparatorChar, 
                Path.AltDirectorySeparatorChar);

            if (string.Equals(abs, root, StringComparison.OrdinalIgnoreCase))
                return true;

            return abs.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || abs.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

       public static string NormalizeToolCallJson(string jsonStr)
        {
            if (string.IsNullOrWhiteSpace(jsonStr))
                return jsonStr;

            string s = jsonStr.Trim();
            while (s.Length >= 4
                   && s.StartsWith("{{", StringComparison.Ordinal)
                   && s.EndsWith("}}", StringComparison.Ordinal))
            {
                s = s[1..^1].Trim();
            }

            return s;
        }

        public static bool IsPlaceholderToolJson(string jsonStr)
        {
            if (string.IsNullOrWhiteSpace(jsonStr))
                return true;
            return string.Equals(jsonStr.Trim(), "{JSON_ARGS}", StringComparison.OrdinalIgnoreCase);
        }

        public static string ResolveAbsPath(string pathStr)
        {
            if (string.IsNullOrWhiteSpace(pathStr))
                throw new ArgumentException("Path cannot be null or empty.", nameof(pathStr));
            if (pathStr.StartsWith("~"))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                pathStr = Path.Combine(home, pathStr.TrimStart('~', '/', '\\'));
            }

            if (!Path.IsPathRooted(pathStr))
            {
                pathStr = Path.GetFullPath(Path.Combine(ResolveWorkspaceRoot(), pathStr));
            }
            else
            {
                pathStr = Path.GetFullPath(pathStr);
            }

            return pathStr;
        }

        public static string ResolveWorkspaceRoot()
        {
            string? env = Environment.GetEnvironmentVariable("HARNESS_WORKSPACE_ROOT");
            if (!string.IsNullOrWhiteSpace(env))
            {
                string expanded = Path.GetFullPath(env.Trim());
                if (Directory.Exists(expanded))
                    return expanded;
            }

            try
            {
                for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                {
                    if (dir.GetFiles("*.csproj", SearchOption.TopDirectoryOnly).Length > 0)
                        return dir.FullName;
                }
            }
            catch
            {
                // ignore discovery errors
            }

            return Directory.GetCurrentDirectory();
        }
    }
}
