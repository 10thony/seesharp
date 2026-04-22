
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

        static string _relativePathBase = ResolveWorkspaceRoot();
        public const int ContextualizerMaxListLines = 2500;
        public const int ContextualizerMaxFilesToRead = 15;
        public const int ContextualizerMaxCharsPerFile = 48_000;
        public static readonly JsonSerializerOptions s_contextualizerJson = new()
        { PropertyNameCaseInsensitive = true };

        static readonly HashSet<string> s_workspaceWalkExcludedDirs = new(StringComparer.OrdinalIgnoreCase)
        {
            "bin", "obj", ".git", ".vs", ".idea", "node_modules", "openai-dotnet",
            ".dockerignore","tools"
        };

        static readonly HashSet<string> s_workspaceWalkSkippedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".dll", ".exe", ".pdb", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".ico", ".zip",
            ".7z", ".tar", ".gz", ".pdf", ".woff", ".woff2", ".ttf", ".eot", ".mp4", ".mp3", ".user",
            ".h"
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
                sb.AppendLine($"... and {allRelPaths.Count - ContextualizerMaxListLines} more paths not shown.");

            if (!string.IsNullOrWhiteSpace(userTask))
            {
                sb.AppendLine();
                sb.AppendLine("User task (use to prioritize files):");
                sb.AppendLine(userTask.Trim());
            }

            return sb.ToString();
        }

        public static List<string> ParsePickedPaths(string pickRaw, string workspaceRoot, IReadOnlyList<string> catalog)
        {
            var set = new HashSet<string>(catalog, StringComparer.OrdinalIgnoreCase);
            string json = StripMarkdownFence(pickRaw);
            try
            {
                var dto = JsonSerializer.Deserialize<ContextualizerPickDto>(json, s_contextualizerJson);
                if (dto?.Paths is null || dto.Paths.Count == 0)
                    return new List<string>();

                var result = new List<string>();
                foreach (string p in dto.Paths)
                {
                    if (string.IsNullOrWhiteSpace(p) || result.Count >= ContextualizerMaxFilesToRead)
                        break;

                    string norm = p.Trim().Replace('\\', '/');
                    if (!set.Contains(norm))
                        continue;

                    string abs = AgentUtilities.ResolveAbsPath(norm);
                    if (!AgentUtilities.IsPathUnderWorkspaceRoot(abs, workspaceRoot) || !File.Exists(abs))
                        continue;

                    result.Add(norm);
                }

                return result;
            }
            catch
            {
                return new List<string>();
            }
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
            return list;
        }

        static bool ShouldSkipWorkspaceRelativePath(string relativePath)
        {
            foreach (string segment in relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
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
            string root = Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string abs = Path.GetFullPath(absolutePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
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
                pathStr = Path.GetFullPath(Path.Combine(_relativePathBase, pathStr));
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
