using System.ClientModel;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Models;
using OpenAI.Responses;
using SeeSharp.Models;

namespace SeeSharp.Dev;

/// <summary>
/// Local dev harness: pick a workspace under <c>test-projects/</c>, contextualize it, then run an
/// interactive agent chat against that project.
/// </summary>
public static class LocalTestProjectMenu
{
    /// <summary>Opt-in: original multi-model loop. Default is the interactive test harness in <see cref="RunAsync"/>.</summary>
    public static bool IsLegacyAllModelsMode(string[]? args)
    {
        return string.Equals(Environment.GetEnvironmentVariable("SEESHARP_LEGACY"), "1", StringComparison.Ordinal)
        || (args is not null
            && args.Any(static a =>
                a is not null && string.Equals(a, "--legacy", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Opt-in: batch run generated tasks across all test projects.</summary>
    public static bool IsMultiProjectHarnessMode(string[]? args)
    {
        return string.Equals(Environment.GetEnvironmentVariable("SEESHARP_MULTI_PROJECT"), "1", StringComparison.Ordinal)
            || (args is not null
                && args.Any(static a =>
                    a is not null && string.Equals(a, "--multi-project", StringComparison.OrdinalIgnoreCase)));
    }

    public static async Task RunAsync(
        ApiKeyCredential credential,
        OpenAIClientOptions clientOptions,
        IReadOnlyList<OpenAIModel> models,
        ChatClient contextualizerChatClient,
        string contextualizerModelId,
        Func<IReadOnlyCollection<string>, CancellationToken, Task>? keepOnlyModelsLoadedAsync,
        Action<string>? onModelActivated,
        SessionRecorder? sessionRecorder,
        CancellationToken cancellationToken,
        string[]? args = null)
    {
        if (models.Count == 0)
        {
            ThemedConsole.WriteLine(TerminalTone.Error,
                "[TestHarness] No models returned from LM Studio. Load a model and retry.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOTNET_WATCH")))
        {
            ThemedConsole.WriteLine(
                TerminalTone.Error,
                "[TestHarness] Running under `dotnet watch`: the host can print on top of prompts. "
                + "For a clean console use Visual Studio (F5), or `set SEESHARP_DISABLE_WATCH=1` + `dotnet run` (not watch), "
                + "or the `--legacy` flow which enables auto `dotnet watch` only for the old multi-model mode.");
        }

        if (IsMultiProjectHarnessMode(args))
        {
            await RunGeneratedMultiProjectAsync(
                credential,
                clientOptions,
                models,
                contextualizerChatClient,
                contextualizerModelId,
                keepOnlyModelsLoadedAsync,
                onModelActivated,
                sessionRecorder,
                cancellationToken);
            return;
        }

        if (!TrySelectTestProjectDir(out string? projectDir) || string.IsNullOrEmpty(projectDir))
        {
            return;
        }

        string expanded = Path.GetFullPath(projectDir);
        if (!Directory.Exists(expanded))
        {
            ThemedConsole.WriteLine(TerminalTone.Error, $"[TestHarness] Not a directory: {expanded}");
            return;
        }

        if (!TrySelectAgentModel(models, out OpenAIModel? agentModel) || agentModel is null)
        {
            return;
        }

        Environment.SetEnvironmentVariable("HARNESS_WORKSPACE_ROOT", expanded, EnvironmentVariableTarget.Process);
        ThemedConsole.WriteLine(TerminalTone.Reasoning, $"[TestHarness] Workspace = {expanded}");
        ThemedConsole.WriteLine(TerminalTone.Reasoning, $"[TestHarness] Agent model = {agentModel.Id}");

        onModelActivated?.Invoke(agentModel.Id);
        onModelActivated?.Invoke(contextualizerModelId);
        if (keepOnlyModelsLoadedAsync is not null)
        {
            await keepOnlyModelsLoadedAsync(
                new HashSet<string>(new[] { agentModel.Id, contextualizerModelId }, StringComparer.OrdinalIgnoreCase),
                cancellationToken);
        }

        var projectConfig = SeeSharpConfigLoader.Load(expanded);
        var responsesClient = new ResponsesClient(credential, clientOptions);
        string lmStudioBaseUri = Environment.GetEnvironmentVariable("LMSTUDIO_BASE_URI")
            ?? "http://cobec-spark:1234/v1";
        string apiKey = Environment.GetEnvironmentVariable("LMSTUDIO_API_KEY") ?? "lm-studio";

        using (AgentUtilities.PushWorkspaceRoot(expanded))
        using (AgentRuntime runtime = AgentRuntimeFactory.Create(
            agentModel,
            projectConfig,
            responsesClient,
            contextualizerChatClient,
            lmStudioBaseUri,
            apiKey,
            keepOnlyModelsLoadedAsync,
            sessionRecorder))
        {
            sessionRecorder?.RecordSessionStart(agentModel.Id, expanded, contextualizerModelId);
            _ = await runtime.Agent.RunInteractiveChatAsync(responsesClient, cancellationToken: cancellationToken);
        }
    }

    static bool TrySelectAgentModel(IReadOnlyList<OpenAIModel> models, out OpenAIModel? model)
    {
        model = null;
        if (models.Count == 1)
        {
            model = models[0];
            return true;
        }

        for (;;)
        {
            ThemedConsole.WriteLine(TerminalTone.Reasoning, "[TestHarness] Loaded LM Studio models:");
            for (int i = 0; i < models.Count; i++)
            {
                ThemedConsole.WriteLine(TerminalTone.Reasoning, $"  {i}) {models[i].Id}");
            }

            ThemedConsole.WriteLine(TerminalTone.Reasoning, "Enter model number, or Q to quit: ");
            Console.Out.Flush();
            ThemedConsole.ApplyTone(TerminalTone.User);
            string? line = Console.ReadLine();
            ThemedConsole.Reset();
            if (line is null)
            {
                return false;
            }

            line = line.Trim();
            if (string.Equals(line, "Q", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!int.TryParse(line, out int idx) || idx < 0 || idx >= models.Count)
            {
                ThemedConsole.WriteLine(TerminalTone.Error, "[TestHarness] Invalid model index.");
                continue;
            }

            model = models[idx];
            return true;
        }
    }

    static async Task RunGeneratedMultiProjectAsync(
        ApiKeyCredential credential,
        OpenAIClientOptions clientOptions,
        IReadOnlyList<OpenAIModel> models,
        ChatClient contextualizerChatClient,
        string contextualizerModelId,
        Func<IReadOnlyCollection<string>, CancellationToken, Task>? keepOnlyModelsLoadedAsync,
        Action<string>? onModelActivated,
        SessionRecorder? sessionRecorder,
        CancellationToken cancellationToken)
    {
        string repoRoot = AgentUtilities.ResolveWorkspaceRoot();
        string testRoot = Path.Combine(repoRoot, "test-projects");
        if (!Directory.Exists(testRoot))
        {
            ThemedConsole.WriteLine(TerminalTone.Error, $"[TestHarness] Missing folder: {testRoot}");
            return;
        }

        List<WorkspaceCandidate> candidates = EnumerateWorkspaceCandidates(testRoot)
            .OrderBy(static c => c.DirectoryPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (candidates.Count == 0)
        {
            ThemedConsole.WriteLine(TerminalTone.Error,
                $"[TestHarness] No recognized test workspaces under: {testRoot}");
            return;
        }

        List<WorkspaceGroup> groups = GroupWorkspaceCandidates(candidates);
        if (groups.Count == 0)
        {
            ThemedConsole.WriteLine(TerminalTone.Error,
                $"[TestHarness] No grouped developer projects under: {testRoot}");
            return;
        }

        Dictionary<string, List<string>> taskMap = BuildGeneratedTaskMap(groups);
        if (taskMap.Count == 0)
        {
            ThemedConsole.WriteLine(TerminalTone.Error,
                "[TestHarness] Generated task map is empty. Add known test projects under test-projects.");
            return;
        }

        List<OpenAIModel> qwen36 = models
            .Where(static m => IsQwenVersion(m.Id, "3.6"))
            .OrderBy(static m => m.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        List<OpenAIModel> qwen35 = models
            .Where(static m => IsQwenVersion(m.Id, "3.5"))
            .OrderBy(static m => m.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (qwen36.Count == 0 && qwen35.Count == 0)
        {
            ThemedConsole.WriteLine(TerminalTone.Error,
                "[TestHarness] No qwen 3.6 or qwen 3.5 models are loaded in LM Studio.");
            return;
        }

        var projects = taskMap.Keys
            .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
        ThemedConsole.WriteLine(TerminalTone.Reasoning,
            $"[TestHarness] Multi-project run: {projects.Count} project(s)");
        ThemedConsole.WriteLine(TerminalTone.Reasoning,
            $"[TestHarness] Model pools: qwen 3.6 = {qwen36.Count}, qwen 3.5 = {qwen35.Count}");

        for (int i = 0; i < projects.Count; i++)
        {
            string projectDir = projects[i];
            List<string> tasks = taskMap[projectDir];
            OpenAIModel model = SelectModelForProjectIndex(i, qwen36, qwen35);
            ThemedConsole.WriteLine(
                TerminalTone.Reasoning,
                $"[TestHarness] Project {i + 1}/{projects.Count}: {projectDir} | tasks={tasks.Count} | model={model.Id}");
        }

        // Keep only contextualizer + the current project agent loaded. Preloading every
        // project's model at once causes LM Studio load/cancel churn on limited VRAM.
        onModelActivated?.Invoke(contextualizerModelId);
        if (keepOnlyModelsLoadedAsync is not null)
        {
            await keepOnlyModelsLoadedAsync(
                new HashSet<string>(new[] { contextualizerModelId }, StringComparer.OrdinalIgnoreCase),
                cancellationToken);
        }

        var responsesClient = new ResponsesClient(credential, clientOptions);
        for (int i = 0; i < projects.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string projectDir = projects[i];
            List<string> tasks = taskMap[projectDir];
            if (tasks.Count == 0)
            {
                ThemedConsole.WriteLine(TerminalTone.Error,
                    $"[TestHarness] Skipping {projectDir}: no tasks generated.");
                continue;
            }

            OpenAIModel model = SelectModelForProjectIndex(i, qwen36, qwen35);
            onModelActivated?.Invoke(model.Id);
            if (keepOnlyModelsLoadedAsync is not null)
            {
                await keepOnlyModelsLoadedAsync(
                    new HashSet<string>(new[] { model.Id, contextualizerModelId }, StringComparer.OrdinalIgnoreCase),
                    cancellationToken);
            }
            Environment.SetEnvironmentVariable(
                "HARNESS_WORKSPACE_ROOT",
                projectDir,
                EnvironmentVariableTarget.Process);
            ThemedConsole.WriteLine(TerminalTone.Reasoning,
                $"[TestHarness] Running project loop {i + 1}/{projects.Count} at {projectDir}");

            var projectConfig = SeeSharpConfigLoader.Load(projectDir);
            using (AgentUtilities.PushWorkspaceRoot(projectDir))
            {
                var toolKit = new ToolKit(projectConfig);
                var agent = new Agent(model, toolKit, contextualizerChatClient, projectConfig)
                {
                    SessionRecorder = sessionRecorder
                };
                sessionRecorder?.RecordSessionStart(model.Id, projectDir, contextualizerModelId);
                _ = await agent.AgentLoop(responsesClient, new List<string>(tasks), cancellationToken);
            }
        }
    }

    static readonly (string Pattern, string Label)[] WorkspaceMarkers =
    [
        ("*.slnx", "Solution"),
        ("*.sln", "Solution"),
        ("*.csproj", ".NET project"),
        ("package.json", "Node project"),
        ("pyproject.toml", "Python project"),
        ("requirements.txt", "Python project"),
        ("go.mod", "Go project"),
        ("pom.xml", "Java project"),
        ("docker-compose.yml", "Docker compose"),
        ("Dockerfile", "Dockerfile")
    ];

    sealed class WorkspaceCandidate
    {
        public required string DirectoryPath { get; init; }
        public required string MarkerPath { get; init; }
        public required string MarkerLabel { get; init; }
    }

    sealed class WorkspaceGroup
    {
        public required string RootDirectoryPath { get; init; }
        public required string MarkerLabel { get; init; }
        public required int MemberCount { get; init; }
    }

    static bool TrySelectTestProjectDir(out string? projectDir)
    {
        projectDir = null;
        string repoRoot = AgentUtilities.ResolveWorkspaceRoot();
        string testRoot = Path.Combine(repoRoot, "test-projects");
        if (!Directory.Exists(testRoot))
        {
            ThemedConsole.WriteLine(TerminalTone.Error,
                $"[TestHarness] Missing folder: {testRoot}");
            return false;
        }

        var candidates = EnumerateWorkspaceCandidates(testRoot)
            .OrderBy(static c => c.DirectoryPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (candidates.Count == 0)
        {
            ThemedConsole.WriteLine(TerminalTone.Error,
                $"[TestHarness] No recognized test workspaces under: {testRoot}");
            return false;
        }

        var groups = GroupWorkspaceCandidates(candidates);
        if (groups.Count == 0)
        {
            ThemedConsole.WriteLine(TerminalTone.Error,
                $"[TestHarness] No grouped developer projects under: {testRoot}");
            return false;
        }

        for (;;)
        {
            ThemedConsole.WriteLine(TerminalTone.Reasoning,
                "[TestHarness] Developer projects (grouped by solution or standalone marker):");
            for (int i = 0; i < groups.Count; i++)
            {
                WorkspaceGroup group = groups[i];
                string rel = Path.GetRelativePath(testRoot, group.RootDirectoryPath);
                string memberSuffix = group.MemberCount > 1
                    ? $", {group.MemberCount} markers"
                    : "";
                ThemedConsole.WriteLine(
                    TerminalTone.Reasoning,
                    $"  {i}) {rel}  [{group.MarkerLabel}{memberSuffix}]");
            }
            ThemedConsole.WriteLine(TerminalTone.Reasoning, "Enter number, or Q to quit: ");
            Console.Out.Flush();
            ThemedConsole.ApplyTone(TerminalTone.User);
            string? line = Console.ReadLine();
            ThemedConsole.Reset();
            if (line is null)
            {
                return false;
            }

            line = line.Trim();
            if (string.Equals(line, "Q", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!int.TryParse(line, out int idx) || idx < 0 || idx >= groups.Count)
            {
                ThemedConsole.WriteLine(TerminalTone.Error, "[TestHarness] Invalid project index.");
                continue;
            }

            string selectedDir = Path.GetFullPath(groups[idx].RootDirectoryPath);
            if (string.IsNullOrEmpty(selectedDir) || !Directory.Exists(selectedDir))
            {
                ThemedConsole.WriteLine(TerminalTone.Error, "[TestHarness] Could not resolve project directory.");
                continue;
            }

            projectDir = selectedDir;
            return true;
        }
    }

    static IEnumerable<WorkspaceCandidate> EnumerateWorkspaceCandidates(string testRoot)
    {
        var seenDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dirs = Directory.EnumerateDirectories(testRoot, "*", SearchOption.AllDirectories)
            .Prepend(testRoot);
        foreach (string dir in dirs)
        {
            string fullDir = Path.GetFullPath(dir);
            if (!seenDirs.Add(fullDir))
            {
                continue;
            }

            string? markerPath = null;
            string? markerLabel = null;
            foreach ((string pattern, string label) in WorkspaceMarkers)
            {
                string? match = Directory
                    .EnumerateFiles(fullDir, pattern, SearchOption.TopDirectoryOnly)
                    .FirstOrDefault();
                if (match is null)
                {
                    continue;
                }

                markerPath = Path.GetFullPath(match);
                markerLabel = label;
                break;
            }

            if (markerPath is null || markerLabel is null)
            {
                continue;
            }

            yield return new WorkspaceCandidate
            {
                DirectoryPath = fullDir,
                MarkerPath = markerPath,
                MarkerLabel = markerLabel
            };
        }
    }

    static List<WorkspaceGroup> GroupWorkspaceCandidates(IReadOnlyList<WorkspaceCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        var solutionRoots = candidates
            .Where(static c => string.Equals(c.MarkerLabel, "Solution", StringComparison.Ordinal))
            .Select(static c => Path.GetFullPath(c.DirectoryPath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static p => p.Count(ch => ch is '/' or '\\'))
            .ThenBy(static p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var outerSolutionRoots = new List<string>();
        foreach (string root in solutionRoots)
        {
            if (outerSolutionRoots.Any(existing =>
                    IsSameOrUnderDirectory(root, existing)))
            {
                continue;
            }

            outerSolutionRoots.Add(root);
        }

        var groups = new List<WorkspaceGroup>();
        var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string root in outerSolutionRoots)
        {
            if (!PassesHarnessOnlyFilter(root))
            {
                continue;
            }

            List<WorkspaceCandidate> members = candidates
                .Where(c => IsSameOrUnderDirectory(c.DirectoryPath, root))
                .ToList();
            foreach (WorkspaceCandidate member in members)
            {
                _ = assigned.Add(member.DirectoryPath);
            }

            groups.Add(new WorkspaceGroup
            {
                RootDirectoryPath = root,
                MarkerLabel = ResolveGroupMarkerLabel(root, members),
                MemberCount = members.Count
            });
        }

        foreach (WorkspaceCandidate candidate in candidates
                     .OrderBy(static c => c.DirectoryPath, StringComparer.OrdinalIgnoreCase))
        {
            if (assigned.Contains(candidate.DirectoryPath))
            {
                continue;
            }

            if (IsHarnessExcludedLeaf(candidate.DirectoryPath))
            {
                continue;
            }

            if (!PassesHarnessOnlyFilter(candidate.DirectoryPath))
            {
                continue;
            }

            groups.Add(new WorkspaceGroup
            {
                RootDirectoryPath = candidate.DirectoryPath,
                MarkerLabel = candidate.MarkerLabel,
                MemberCount = 1
            });
            _ = assigned.Add(candidate.DirectoryPath);
        }

        return groups
            .OrderBy(static g => g.RootDirectoryPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static string ResolveGroupMarkerLabel(string root, IReadOnlyList<WorkspaceCandidate> members)
    {
        WorkspaceCandidate? atRoot = members.FirstOrDefault(c =>
            string.Equals(Path.GetFullPath(c.DirectoryPath), root, StringComparison.OrdinalIgnoreCase));
        if (atRoot is not null)
        {
            return atRoot.MarkerLabel;
        }

        return members.FirstOrDefault(c =>
                   string.Equals(c.MarkerLabel, "Solution", StringComparison.Ordinal))?.MarkerLabel
               ?? members[0].MarkerLabel;
    }

    static bool IsSameOrUnderDirectory(string path, string root)
    {
        string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string prefix = fullRoot + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    static bool PassesHarnessOnlyFilter(string directoryPath)
    {
        string? only = Environment.GetEnvironmentVariable("SEESHARP_HARNESS_ONLY");
        if (string.IsNullOrWhiteSpace(only))
        {
            return true;
        }

        string norm = directoryPath.Replace('\\', '/');
        return norm.IndexOf(only.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Platform heads, test projects, and tooling folders are grouped under a parent solution
    /// but should not appear as standalone developer projects in the menu.
    /// </summary>
    static bool IsHarnessExcludedLeaf(string directoryPath)
    {
        string norm = directoryPath.Replace('\\', '/');
        return norm.Contains("/RunBoth", StringComparison.OrdinalIgnoreCase)
            || norm.Contains(".Droid", StringComparison.OrdinalIgnoreCase)
            || norm.Contains(".iOS", StringComparison.OrdinalIgnoreCase)
            || norm.Contains(".Mac", StringComparison.OrdinalIgnoreCase)
            || norm.Contains(".WinUI", StringComparison.OrdinalIgnoreCase)
            || norm.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
            || norm.Contains("/infra/tools/", StringComparison.OrdinalIgnoreCase)
            || norm.EndsWith(".Client", StringComparison.OrdinalIgnoreCase);
    }

    static Dictionary<string, List<string>> BuildGeneratedTaskMap(IReadOnlyList<WorkspaceGroup> groups)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (WorkspaceGroup group in groups)
        {
            string projectDir = Path.GetFullPath(group.RootDirectoryPath);
            string projectName = Path.GetFileName(projectDir);
            List<string> tasks = BuildGeneratedTasksForProject(projectDir, projectName);
            if (tasks.Count == 0)
            {
                continue;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            List<string> deduped = new List<string>(tasks.Count);
            foreach (string task in tasks)
            {
                if (seen.Add(task))
                {
                    deduped.Add(task);
                }
            }

            map[projectDir] = deduped;
        }

        return map;
    }

    static List<string> BuildGeneratedTasksForProject(string projectDir, string projectName)
    {
        string normalized = (projectDir + "/" + projectName).Replace('\\', '/').ToLowerInvariant();
        if (normalized.Contains("testminimalwebapi", StringComparison.Ordinal))
        {
            return
            [
                "Tier 1: Add request guards so create and update reject empty Name values and return Results.BadRequest with a consistent payload.",
                "Tier 1: Add GET /todoitems/incomplete endpoint that mirrors existing /complete behavior for unfinished todos.",
                "Tier 2: Extend GET /todoitems to support optional query filtering by nameContains and isComplete.",
                "Tier 2: Add page and pageSize query parameters to the list endpoint with sane defaults and bounds.",
                "Tier 3: Harden update and delete flows to return stable not-found responses with consistent behavior across endpoints.",
                "Tier 4: Add integration tests that verify validation failures, filtered list behavior, and pagination edge cases."
            ];
        }

        if (normalized.Contains("nativemobilechat", StringComparison.Ordinal))
        {
            return
            [
                "REQUIRED: Read target files with BASH Get-Content -Raw, then apply edits with BASH Set-Content or EDIT_FILE using exact old text. Verify with read-back. Prose-only answers fail validation.",
                "Tier 1 (API): In ChatController.PostMessage and ChatHub.SendMessage, reject empty or whitespace-only messages with a consistent validation error payload.",
                "Tier 1 (API): Add max message length guardrails (e.g. 2000 chars) on REST and hub send paths with ProblemDetails or equivalent JSON errors.",
                "Tier 1 (Client): In ChatService and RealtimeChatService, reject empty or whitespace-only message content before calling the API or hub; surface a clear user-visible hint on MainPage.",
                "Tier 1 (Client): Add max message length guardrails (e.g. 2000 chars) on send in the MAUI client aligned with server expectations.",
                "Tier 2 (API): Standardize API error responses to one shape { code, message } for BadRequest, Unauthorized, and Conflict across chat and auth controllers.",
                "Tier 2 (Client): When RealtimeChatService receives ReceiveMessage, map payload into ChatMessageRecord and persist via DataBridgeService so the list updates without a full refresh.",
                "Tier 3 (Client): Add defensive handling when API base address is unreachable (show connection status on MainPage, avoid duplicate pending sends).",
                "Tier 3 (API): Ensure ChatRepository.Insert persists before hub broadcast and handle database failures without broadcasting partial events.",
                "Tier 4: Add integration tests for chat validation, unauthorized access, successful post + hub ReceiveMessage broadcast, and client/server alignment."
            ];
        }

        if (normalized.Contains("testsignalrblazorapp", StringComparison.Ordinal))
        {
            return
            [
                "Tier 1: Add ChatHub SendMessage validation that rejects empty user or message values and normalizes whitespace.",
                "Tier 1: Add max length guardrails for user and message fields with clear client-visible error behavior.",
                "Tier 2: Add join and leave notifications in the hub so clients can display presence events.",
                "Tier 2: Add lightweight in-memory presence tracking keyed by connection id and normalized username.",
                "Tier 3: Add reconnect-safe client flow that re-establishes the hub connection and re-announces presence.",
                "Tier 4: Add SignalR integration tests for broadcast delivery, validation rejection, and reconnect-presence behavior."
            ];
        }

        return [];
    }

    static bool IsQwenVersion(string? modelId, string versionToken)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return false;
        }

        string id = modelId.Trim().ToLowerInvariant();
        if (!id.Contains("qwen", StringComparison.Ordinal))
        {
            return false;
        }

        return id.Contains($"qwen{versionToken}", StringComparison.Ordinal)
            || id.Contains($"qwen/{versionToken}", StringComparison.Ordinal)
            || id.Contains(versionToken, StringComparison.Ordinal);
    }

    static OpenAIModel SelectModelForProjectIndex(
        int projectIndex,
        IReadOnlyList<OpenAIModel> qwen36,
        IReadOnlyList<OpenAIModel> qwen35)
    {
        if (projectIndex < qwen36.Count)
        {
            return qwen36[projectIndex];
        }

        if (qwen35.Count > 0)
        {
            return qwen35[(projectIndex - qwen36.Count) % qwen35.Count];
        }

        return qwen36[projectIndex % qwen36.Count];
    }
}
