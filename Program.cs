
using OpenAI;
using OpenAI.Chat;
using OpenAI.Conversations;
using OpenAI.Models;
using OpenAI.Responses;
using SeeSharp.Dev;
using SeeSharp.Models;
using System.ClientModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;

// Traffic goes only to LM Studio. The OpenAI .NET SDK is used because LM
// Studio exposes the same /v1 routes
// and wire models (e.g. OpenAIModel, ResponseResult); type names come from
// the library, not from calling OpenAI.

#region functional running of the program
ThemedConsole.Initialize();
LoadDevelopmentEnvironmentFile(args);
// Default path is the interactive test harness. Watch relaunch
// runs only for --legacy (avoids host output interleaving with prompts when using
// `dotnet run` without the debugger).
if (TryRelaunchUnderDotnetWatch(args))
{
    return;
}

// Load layered config (global + workspace) early so all downstream consumers see it.
string configWorkspaceRoot = AgentUtilities.ResolveWorkspaceRoot();
ResolvedConfig resolvedConfig = SeeSharpConfigLoader.Load(configWorkspaceRoot);
AgentDefaults.ActiveConfig = resolvedConfig;

// Generate starter config on first run if no workspace config exists.
if (SeeSharpConfigLoader.GenerateStarterConfig(configWorkspaceRoot))
{
    ThemedConsole.WriteLine(TerminalTone.Reasoning,
        $"[Config] Generated starter config: {Path.Combine(configWorkspaceRoot, SeeSharpConfigLoader.WorkspaceConfigFileName)}");
}

string lmStudioBaseUri = Environment.GetEnvironmentVariable("LMSTUDIO_BASE_URI")
    ?? "http://cobec-spark:1234/v1";

// LM Studio often ignores this; the SDK still requires a credential value.
string apiKey = Environment.GetEnvironmentVariable("LMSTUDIO_API_KEY") ?? "lm-studio";
var credential = new ApiKeyCredential(apiKey);
var clientOptions = new OpenAIClientOptions
{
    Endpoint = new Uri(lmStudioBaseUri)
};

var lmStudioModelsClient = new OpenAIModelClient(credential, clientOptions);
var lmStudioResponsesClient = new ResponsesClient(credential, clientOptions);

string contextualizerModelId = Environment.
    GetEnvironmentVariable("SEESHARP_CONTEXTUALIZER_MODEL")
    ?? resolvedConfig.ContextualizerPreferredModel
    ?? Agent.DefaultContextualizerModelId;
var contextualizerChatClient = new ChatClient(
    contextualizerModelId, credential, clientOptions);

StringBuilder availableModelsSB = new StringBuilder();
availableModelsSB.AppendLine("LM Studio — select a loaded model:");


// Session recording: write all session data to JSONL for fine-tuning.
string sessionOutputDir = Path.Combine(configWorkspaceRoot, "datasets", "sessions");
SessionRecorder sessionRecorder = new SessionRecorder(sessionOutputDir);
ThemedConsole.WriteLine(TerminalTone.Reasoning,
    $"[Session] Recording to: {sessionRecorder.OutputPath}");

using var cts = new CancellationTokenSource();
List<OpenAIModel> models = (await lmStudioModelsClient
    .GetModelsAsync(cts.Token)).Value.ToList();

HashSet<string> activeModelIds = new(StringComparer.OrdinalIgnoreCase);
void MarkModelActive(string modelId)
{
    if (!string.IsNullOrWhiteSpace(modelId))
    {
        _ = activeModelIds.Add(modelId.Trim());
    }
}
MarkModelActive(contextualizerModelId);

int[] unloadOnceGate = [0];
int[] sessionEndGate = [0];
Action unloadLoadedModelsOnExit = CreateUnloadLoadedModelsAction(
    () => activeModelIds,
    lmStudioBaseUri,
    apiKey,
    unloadOnceGate);
Action finalizeSessionOnExit = () =>
{
    if (Interlocked.Exchange(ref sessionEndGate[0], 1) != 0)
        return;
    try
    {
        sessionRecorder.RecordSessionEnd("process_exit");
        sessionRecorder.Dispose();
    }
    catch { }
};
AppDomain.CurrentDomain.ProcessExit += (_, _) =>
{
    finalizeSessionOnExit();
    unloadLoadedModelsOnExit();
};
Console.CancelKeyPress += (_, _) =>
{
    finalizeSessionOnExit();
    unloadLoadedModelsOnExit();
};

try
{
    if (!LocalTestProjectMenu.IsLegacyAllModelsMode(args))
    {
        await LocalTestProjectMenu.RunAsync(
            credential,
            clientOptions,
            models,
            contextualizerChatClient,
            contextualizerModelId,
            (keepOnlyIds, token) => KeepOnlyModelsLoadedAsync(models, keepOnlyIds, lmStudioBaseUri, apiKey, token),
            MarkModelActive,
            sessionRecorder,
            cts.Token,
            args);
        return;
    }

    List<string> questions =
    [
        "Turn on the postgres docker container named testpostgres",
        "Create a SQL file to create a user table with columns for ID (auto generated and incrementing Primary Key), name, role_id, labor_category_code",
    ];

    foreach (OpenAIModel model in models)
    {
        MarkModelActive(model.Id);
        using AgentRuntime runtime = AgentRuntimeFactory.Create(
            model,
            resolvedConfig,
            lmStudioResponsesClient,
            contextualizerChatClient,
            lmStudioBaseUri,
            apiKey,
            (keepOnlyIds, token) => KeepOnlyModelsLoadedAsync(models, keepOnlyIds, lmStudioBaseUri, apiKey, token),
            sessionRecorder);
        sessionRecorder.RecordSessionStart(model.Id, configWorkspaceRoot, contextualizerModelId);

        StringBuilder agentLoopStrings = await
               runtime.Agent.AgentLoop(lmStudioResponsesClient,
                               questions,
                               cts.Token);
    }
}
finally
{
    if (Interlocked.Exchange(ref sessionEndGate[0], 1) == 0)
    {
        sessionRecorder.RecordSessionEnd("normal");
        sessionRecorder.Dispose();
    }
    unloadLoadedModelsOnExit();
}


#endregion


#region get models functions
static void LoadDevelopmentEnvironmentFile(string[] args)
{
    if (!IsDevelopmentMode(args))
    {
        return;
    }

    string workspaceRoot = AgentUtilities.ResolveWorkspaceRoot();
    string[] candidates =
    [
        ".env.development.local",
        ".env.development",
        ".env.local",
        ".env"
    ];

    foreach (string filename in candidates)
    {
        string fullPath = Path.Combine(workspaceRoot, filename);
        if (!File.Exists(fullPath))
        {
            continue;
        }

        int loaded = 0;
        foreach (string raw in File.ReadAllLines(fullPath))
        {
            string line = raw.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            int idx = line.IndexOf('=');
            if (idx <= 0)
            {
                continue;
            }

            string key = line[..idx].Trim();
            string value = line[(idx + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if ((value.StartsWith("\"", StringComparison.Ordinal) && value.EndsWith("\"", StringComparison.Ordinal)) ||
                (value.StartsWith("'", StringComparison.Ordinal) && value.EndsWith("'", StringComparison.Ordinal)))
            {
                value = value[1..^1];
            }

            // Keep machine/shell-provided values as highest priority.
            string? existing = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(existing))
            {
                continue;
            }

            Environment.SetEnvironmentVariable(key, value, EnvironmentVariableTarget.Process);
            loaded++;
        }

        ThemedConsole.WriteLine(
            TerminalTone.Reasoning,
            $"[Env] Loaded {loaded} variable(s) from {filename} (dev mode).");
        return;
    }
}

static bool IsDevelopmentMode(string[] args)
{
    string? dotnetEnv = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
    string? aspnetEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
    bool envSaysDevelopment =
        string.Equals(dotnetEnv, "Development", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(aspnetEnv, "Development", StringComparison.OrdinalIgnoreCase);

    // For this CLI app, treat debugger sessions and legacy harness mode as development mode.
    return envSaysDevelopment || Debugger.IsAttached || LocalTestProjectMenu.IsLegacyAllModelsMode(args);
}

static bool TryRelaunchUnderDotnetWatch(string[] args)
{
    if (string.Equals(Environment.
        GetEnvironmentVariable("SEESHARP_DISABLE_WATCH"), "1", StringComparison.Ordinal))
    {
        return false;
    }

    if (!LocalTestProjectMenu.IsLegacyAllModelsMode(args))
    {
        return false;
    }

    // Spawning `dotnet watch` exits this process and drops the Visual Studio debugger attachment.
    if (Debugger.IsAttached)
    {
        return false;
    }

    if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOTNET_WATCH")) ||
        !string.IsNullOrWhiteSpace(Environment.
            GetEnvironmentVariable("DOTNET_WATCH_ITERATION")))
    {
        return false;
    }

    string workspaceRoot = AgentUtilities.ResolveWorkspaceRoot();
    string projectPath = Path.Combine(workspaceRoot, "SeeSharp.csproj");
    if (!File.Exists(projectPath))
    {
        return false;
    }

    string forwardedArgs = string.Join(" ", args.Select(QuoteArg));
    string dotnetArgs = $"watch --project \"{projectPath}\" run";
    if (!string.IsNullOrWhiteSpace(forwardedArgs))
    {
        dotnetArgs += " -- " + forwardedArgs;
    }

    try
    {
        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = dotnetArgs,
            WorkingDirectory = workspaceRoot,
            UseShellExecute = false
        };

        Process.Start(psi);
        ThemedConsole.WriteLine(TerminalTone.Reasoning,
            "[DevMode] Relaunched under `dotnet watch run` for --legacy" +
            " (set SEESHARP_DISABLE_WATCH=1 to bypass).");
        return true;
    }
    catch (Exception ex)
    {
        ThemedConsole.WriteLine(TerminalTone.Error, 
            $"[DevMode] Failed to relaunch under dotnet watch: {ex.Message}");
        ThemedConsole.WriteLine(TerminalTone.Reasoning, 
            "[DevMode] Continuing without watch mode.");
        return false;
    }
}

static string QuoteArg(string arg)
{
    if (string.IsNullOrEmpty(arg))
    {
        return "\"\"";
    }

    if (arg.IndexOfAny([' ', '\t', '"']) < 0)
    {
        return arg;
    }

    return "\"" + arg.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}

static Action CreateUnloadLoadedModelsAction(
    Func<IEnumerable<string>> modelIdsProvider,
    string lmStudioBaseUri,
    string apiKey,
    int[] unloadOnceGate)
{
    return () =>
    {
        if (Interlocked.Exchange(ref unloadOnceGate[0], 1) != 0)
        {
            return;
        }

        string[] modelIds = modelIdsProvider()
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (modelIds.Length == 0)
        {
            return;
        }

        try
        {
            Task.Run(async () =>
            {
                foreach (string modelId in modelIds)
                {
                    await TryUnloadModelAsync(lmStudioBaseUri, apiKey, modelId)
                        .ConfigureAwait(false);
                }
            }).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            ThemedConsole.WriteLine(TerminalTone.Error,
                $"[Shutdown] Model unload failed: {ex.Message}");
        }
    };
}

static async Task TryUnloadModelAsync(string lmStudioBaseUri, string apiKey, string modelId)
{
    Uri baseUri = new Uri(lmStudioBaseUri.TrimEnd('/') + "/", UriKind.Absolute);
    using HttpClient http = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(8)
    };
    http.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", apiKey);

    // LM Studio endpoint compatibility can vary by version. Try known unload routes.
    string[] routes =
    [
        "models/unload",
        "../api/v0/models/unload",
        "../api/v0/model/unload"
    ];
    bool unloaded = false;

    foreach (string route in routes)
    {
        Uri requestUri = new Uri(baseUri, route);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { model = modelId }),
                    Encoding.UTF8,
                    "application/json")
            };
            using HttpResponseMessage response = await http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                unloaded = true;
                ThemedConsole.WriteLine(
                    TerminalTone.Reasoning,
                    $"[Shutdown] Unloaded model: {modelId}");
                return;
            }
        }
        catch
        {
            // Best effort on shutdown; try the next known route.
        }
    }

    if (!unloaded)
    {
        ThemedConsole.WriteLine(
            TerminalTone.Error,
            $"[Shutdown] Could not unload model via known LM Studio routes: {modelId}");
    }
}

static async Task KeepOnlyModelsLoadedAsync(
    IReadOnlyList<OpenAIModel> currentlyLoadedModels,
    IReadOnlyCollection<string> keepModelIds,
    string lmStudioBaseUri,
    string apiKey,
    CancellationToken cancellationToken)
{
    HashSet<string> keep = new HashSet<string>(
        keepModelIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim()),
        StringComparer.OrdinalIgnoreCase);

    if (keep.Count == 0)
    {
        return;
    }

    string[] unloadCandidates = currentlyLoadedModels
        .Select(static m => m.Id)
        .Where(static id => !string.IsNullOrWhiteSpace(id))
        .Select(static id => id.Trim())
        .Where(id => !keep.Contains(id))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    foreach (string modelId in unloadCandidates)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await TryUnloadModelAsync(lmStudioBaseUri, apiKey, modelId).ConfigureAwait(false);
    }
}
#endregion

