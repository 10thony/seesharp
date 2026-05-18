using System.ClientModel;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Models;
using OpenAI.Responses;
using SeeSharp.Models;

namespace SeeSharp.Dev;

/// <summary>
/// Local dev-only harness: pick tasks, pick a project under <c>test-projects/</c>, set
/// <c>HARNESS_WORKSPACE_ROOT</c>, run <see cref="LMStudioAgent.AgentLoop"/> on the first loaded model.
/// </summary>
public static class LocalTestProjectMenu
{
    enum HarnessMode
    {
        SingleProject = 1,
        MultiProjectGenerated = 2
    }

    /// <summary>Opt-in: original multi-model loop. Default is the interactive test harness in <see cref="RunAsync"/>.</summary>
    public static bool IsLegacyAllModelsMode(string[]? args)
    {
        return string.Equals(Environment.GetEnvironmentVariable("SEESHARP_LEGACY"), "1", StringComparison.Ordinal)
        || (args is not null
            && args.Any(static a =>
                a is not null && string.Equals(a, "--legacy", StringComparison.OrdinalIgnoreCase)));
    }

    static readonly string[] PresetTasks =
    {
        //"Create an item model entity with columns for id, name, " +
        //    "description, created_at, updated_at, updated_by, created_by, and status",
        //"create an item status enum with values for active, inactive, and archived",
        //"Create an API Controller for the Item model with CRUD functionality ",
        //"create an oracle docker container for this project based off of " +
        //    "https://www.oracle.com/database/free/get-started/ and name it cobec-oracle"
            "Turn on the postgres docker container named testpostgres",
            "Create a SQL file to create a user table with columns for ID" +
                " (auto generated and incrementing Primary Key), name, role_id," +
                "labor_category_code, ",
            "Create a SQL script that creates an item with columns for, ID " +
                "(auto generated and incrementing primary key)" +
                "name, description, cost, manufacturer, location, owner (user id foreign key)," +
                " status (item status lookup value)",
            "Create a SQL script that creates an item status lookup table with values for" +
                " active, inactive, and archived",
            "Create a SQL script that creates a role lookup table with values for admin, user",
            "Create a SQL script that creates a labor category lookup table with values for " +
                "full time, part time, contractor,",
            "Create a SQL Script that loads our postgres container with entities for the tables it contains.",
    };

    public static async Task RunAsync(
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

        if (!TrySelectHarnessMode(out HarnessMode mode))
            return;

        if (mode == HarnessMode.MultiProjectGenerated)
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

        if (!TrySelectTaskSource(out IReadOnlyList<string>? taskList) || taskList is null)
            return;

        if (taskList.Count == 0)
        {
            ThemedConsole.WriteLine(TerminalTone.Error, "[TestHarness] No tasks to run. Exiting.");
            return;
        }

        if (!TrySelectTestProjectDir(out string? projectDir) || string.IsNullOrEmpty(projectDir))
            return;

        string expanded = Path.GetFullPath(projectDir);
        if (!Directory.Exists(expanded))
        {
            ThemedConsole.WriteLine(TerminalTone.Error, $"[TestHarness] Not a directory: {expanded}");
            return;
        }

        Environment.SetEnvironmentVariable("HARNESS_WORKSPACE_ROOT", expanded, EnvironmentVariableTarget.Process);
        ThemedConsole.WriteLine(TerminalTone.Reasoning, $"[TestHarness] HARNESS_WORKSPACE_ROOT = {expanded}");

        OpenAIModel firstModel = models[0];
        ThemedConsole.WriteLine(TerminalTone.Reasoning,
            "[TestHarness] Using first loaded model (single-model run in test mode).");
        onModelActivated?.Invoke(firstModel.Id);
        onModelActivated?.Invoke(contextualizerModelId);
        if (keepOnlyModelsLoadedAsync is not null)
        {
            await keepOnlyModelsLoadedAsync(
                new HashSet<string>(new[] { firstModel.Id, contextualizerModelId }, StringComparer.OrdinalIgnoreCase),
                cancellationToken);
        }

        var config = AgentDefaults.ActiveConfig ?? new ResolvedConfig();
        var toolKit = new ToolKit(config);
        var agent = new Agent(firstModel, toolKit, contextualizerChatClient, config)
        {
            SessionRecorder = sessionRecorder
        };
        sessionRecorder?.RecordSessionStart(firstModel.Id, expanded, contextualizerModelId);
        var taskListForLoop = new List<string>(taskList);
        _ = await agent.AgentLoop(
            new ResponsesClient(credential, clientOptions),
            taskListForLoop,
            cancellationToken);
    }

    static bool TrySelectHarnessMode(out HarnessMode mode)
    {
        mode = HarnessMode.SingleProject;
        while (true)
        {
            ThemedConsole.WriteLine(TerminalTone.Reasoning, "[TestHarness] Run mode:");
            ThemedConsole.WriteLine(TerminalTone.Reasoning, "  1) Single project (existing interactive flow)");
            ThemedConsole.WriteLine(TerminalTone.Reasoning, "  2) Multi-project generated tasks");
            ThemedConsole.WriteLine(TerminalTone.Reasoning, "  Q) Quit");
            ThemedConsole.WriteLine(TerminalTone.Reasoning, "Enter choice: ");
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

            if (string.Equals(line, "1", StringComparison.Ordinal))
            {
                mode = HarnessMode.SingleProject;
                return true;
            }

            if (string.Equals(line, "2", StringComparison.Ordinal))
            {
                mode = HarnessMode.MultiProjectGenerated;
                return true;
            }

            ThemedConsole.WriteLine(TerminalTone.Error, "[TestHarness] Invalid mode choice.");
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

        Dictionary<string, List<string>> taskMap = BuildGeneratedTaskMap(candidates);
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

        var activeModelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            contextualizerModelId
        };
        for (int i = 0; i < projects.Count; i++)
        {
            OpenAIModel selected = SelectModelForProjectIndex(i, qwen36, qwen35);
            activeModelIds.Add(selected.Id);
        }
        foreach (string modelId in activeModelIds)
        {
            onModelActivated?.Invoke(modelId);
        }
        if (keepOnlyModelsLoadedAsync is not null)
        {
            await keepOnlyModelsLoadedAsync(activeModelIds, cancellationToken);
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
            Environment.SetEnvironmentVariable(
                "HARNESS_WORKSPACE_ROOT",
                projectDir,
                EnvironmentVariableTarget.Process);
            ThemedConsole.WriteLine(TerminalTone.Reasoning,
                $"[TestHarness] Running project loop {i + 1}/{projects.Count} at {projectDir}");

            // Reload config per-project since workspace root changes
            var projectConfig = SeeSharpConfigLoader.Load(projectDir);
            var toolKit = new ToolKit(projectConfig);
            var agent = new Agent(model, toolKit, contextualizerChatClient, projectConfig)
            {
                SessionRecorder = sessionRecorder
            };
            sessionRecorder?.RecordSessionStart(model.Id, projectDir, contextualizerModelId);
            _ = await agent.AgentLoop(responsesClient, new List<string>(tasks), cancellationToken);
        }
    }

    static bool TrySelectTaskSource(out IReadOnlyList<string>? list)
    {
        list = null;
        while (true)
        {
            ThemedConsole.WriteLine(TerminalTone.Reasoning, "[TestHarness] Task source:");
            ThemedConsole.WriteLine(TerminalTone.Reasoning, "  0) Run all preset tasks in order");
            for (int i = 0; i < PresetTasks.Length; i++)
            {
                ThemedConsole.WriteLine(
                    TerminalTone.Reasoning,
                    $"  {i + 1}) {TrimOneLine(PresetTasks[i], 100)}");
            }
            ThemedConsole.WriteLine(TerminalTone.Reasoning, "  C) Enter a custom list (one per line, blank line ends)");
            ThemedConsole.WriteLine(TerminalTone.Reasoning, "  Q) Quit");
            ThemedConsole.WriteLine(TerminalTone.Reasoning, "Enter choice: ");
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

            if (string.Equals(line, "0", StringComparison.Ordinal))
            {
                list = PresetTasks;
                return true;
            }

            if (string.Equals(line, "C", StringComparison.OrdinalIgnoreCase))
            {
                return TryReadCustomTasks(out list);
            }

            if (int.TryParse(line, out int n) && n >= 1 && n <= PresetTasks.Length)
            {
                list = [PresetTasks[n - 1]];
                return true;
            }

            ThemedConsole.WriteLine(TerminalTone.Error, "[TestHarness] Invalid choice.");
        }
    }

    static bool TryReadCustomTasks(out IReadOnlyList<string>? list)
    {
        var lines = new List<string>();
        ThemedConsole.WriteLine(TerminalTone.Reasoning, "Enter one task per line. Empty line to finish. (Q alone to cancel)");
        while (true)
        {
            ThemedConsole.ApplyTone(TerminalTone.User);
            string? s = Console.ReadLine();
            ThemedConsole.Reset();
            if (s is null)
            {
                list = null;
                return false;
            }

            if (string.Equals(s.Trim(), "Q", StringComparison.OrdinalIgnoreCase))
            {
                list = null;
                return false;
            }

            if (string.IsNullOrWhiteSpace(s))
            {
                break;
            }

            lines.Add(s.Trim());
        }

        list = lines;
        return true;
    }

    static readonly (string Pattern, string Label)[] WorkspaceMarkers =
    [
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

        var candidates = EnumerateWorkspaceCandidates(testRoot).ToList();
        if (candidates.Count == 0)
        {
            ThemedConsole.WriteLine(TerminalTone.Error,
                $"[TestHarness] No recognized test workspaces under: {testRoot}");
            return false;
        }

        for (;;)
        {
            ThemedConsole.WriteLine(TerminalTone.Reasoning,
                "[TestHarness] Test workspace candidates (directory containing marker file is used as workspace):");
            for (int i = 0; i < candidates.Count; i++)
            {
                WorkspaceCandidate candidate = candidates[i];
                ThemedConsole.WriteLine(
                    TerminalTone.Reasoning,
                    $"  {i}) {candidate.MarkerPath}  [{candidate.MarkerLabel}]");
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

            if (!int.TryParse(line, out int idx) || idx < 0 || idx >= candidates.Count)
            {
                ThemedConsole.WriteLine(TerminalTone.Error, "[TestHarness] Invalid project index.");
                continue;
            }

            string selectedDir = Path.GetFullPath(candidates[idx].DirectoryPath);
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

    static Dictionary<string, List<string>> BuildGeneratedTaskMap(IReadOnlyList<WorkspaceCandidate> candidates)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (WorkspaceCandidate candidate in candidates)
        {
            string projectDir = Path.GetFullPath(candidate.DirectoryPath);
            string projectName = Path.GetFileName(projectDir);
            List<string> tasks = BuildGeneratedTasksForProject(projectDir, projectName);
            if (tasks.Count == 0)
            {
                continue;
            }

            // Keep only unique tasks while preserving order.
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

        if (normalized.Contains("testnativemobilebackendapi", StringComparison.Ordinal))
        {
            return
            [
                "Tier 1: Standardize controller error payloads to one shape containing code and message for all bad request, conflict, and not-found paths.",
                "Tier 1: Validate ID, Name, and Notes are required for create and edit operations and return typed validation errors.",
                "Tier 2: Add done and nameContains query options to the list endpoint for filtered retrieval.",
                "Tier 2: Add deterministic sorting for list output to stabilize results across runs.",
                "Tier 3: Refactor repository update and delete methods to return explicit success or failure so controller responses are accurate.",
                "Tier 4: Add controller tests for conflict, not found, validation failure, filtered list, and successful edit and delete flows."
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

        // Supports forms like qwen3.6-27b and qwen/qwen3.6-35b-a3b.
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

        // No fallback models loaded; continue rotating through qwen 3.6 only.
        return qwen36[projectIndex % qwen36.Count];
    }

    static string TrimOneLine(string s, int max)
    {
        s = s.ReplaceLineEndings(" ");
        s = s.Trim();
        if (s.Length <= max)
        {
            return s;
        }

        return s[..max] + "…";
    }
}
