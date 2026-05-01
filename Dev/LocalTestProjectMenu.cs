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
        ConvexService convexService,
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

        var toolKit = new LMStudioToolKit();
        var agent = new LMStudioAgent(firstModel, toolKit, contextualizerChatClient, convexService);
        var taskListForLoop = new List<string>(taskList);
        _ = await agent.AgentLoop(
            new ResponsesClient(credential, clientOptions),
            taskListForLoop,
            cancellationToken);
    }

    static bool TrySelectTaskSource(out IReadOnlyList<string>? list)
    {
        list = null;
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
        return false;
    }

    static bool TryReadCustomTasks(out IReadOnlyList<string>? list)
    {
        var lines = new List<string>();
        ThemedConsole.WriteLine(TerminalTone.Reasoning, "Enter one task per line. Empty line to finish. (Q alone to cancel)");
        for (;;)
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
            return false;
        }

        string selectedDir = Path.GetFullPath(candidates[idx].DirectoryPath);
        if (string.IsNullOrEmpty(selectedDir) || !Directory.Exists(selectedDir))
        {
            ThemedConsole.WriteLine(TerminalTone.Error, "[TestHarness] Could not resolve project directory.");
            return false;
        }

        projectDir = selectedDir;
        return true;
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
