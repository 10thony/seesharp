
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
// Default path is the interactive test harness. Watch relaunch
// runs only for --legacy (avoids host output interleaving with prompts when using
// `dotnet run` without the debugger).
if (TryRelaunchUnderDotnetWatch(args))
{
    return;
}

const string lmStudioBaseUri = "http://cobec-spark:1234/v1";

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
    ?? Agent.DefaultContextualizerModelId;
var contextualizerChatClient = new ChatClient(
    contextualizerModelId, credential, clientOptions);

StringBuilder availableModelsSB = new StringBuilder();
availableModelsSB.AppendLine("LM Studio — select a loaded model:");


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
Action unloadLoadedModelsOnExit = CreateUnloadLoadedModelsAction(
    () => activeModelIds,
    lmStudioBaseUri,
    apiKey,
    unloadOnceGate);
AppDomain.CurrentDomain.ProcessExit += (_, _) => unloadLoadedModelsOnExit();
Console.CancelKeyPress += (_, _) => unloadLoadedModelsOnExit();

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
            cts.Token);
        return;
    }

    List<string> questions = new List<string>()
    {
        //"Read the Program.cs file and tell me what it does.",
        //"Edit the Agent.cs file so it is a regular class instead of an abstract class.",
        //"what does cobec inc do? here is there homepage: https://www.cobec.com/",
        //"List the files in the codebase, study what there is, determine the most important files to read," +
        //" and tell me what this app does.",
        //"How can i build this program?",
        //"Study this codebase and let me know what it does."
        //"List the files in the codebase, study what there is, determine the most important files to read," +
        //" and tell me what this app does.",
        //"How can i build this program?",
        //"Study this codebase and let me know what it does."
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

    LMStudioToolKit toolKit = new LMStudioToolKit();

    foreach (OpenAIModel model in models)
    {
        MarkModelActive(model.Id);
        LMStudioAgent agent = new(model, toolKit, contextualizerChatClient);


        StringBuilder agentLoopStrings = await
               agent.AgentLoop(new ResponsesClient(credential, clientOptions),
                               questions,
                               cts.Token);
    }
}
finally
{
    unloadLoadedModelsOnExit();
}


#endregion


#region get models functions
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

static OpenAIModel PromptUserToSelectModel(string userInput, List<OpenAIModel> models)
{
    bool isUserInputValid =
        int.TryParse(userInput, out int selectedModelIndex) &&
        selectedModelIndex >= 0 &&
        selectedModelIndex < models.Count;

    if (isUserInputValid)
    {
        return models[selectedModelIndex];
    }

    while (!isUserInputValid)
    {
        ThemedConsole.WriteLine(TerminalTone.Error,
            "Invalid input. Please enter a valid number corresponding to the " +
            "model you want to select:");
        ThemedConsole.ApplyTone(TerminalTone.User);
        userInput = Console.ReadLine() ?? "";
        ThemedConsole.Reset();
        isUserInputValid = int.TryParse(userInput, out selectedModelIndex) &&
            selectedModelIndex >= 0 &&
            selectedModelIndex < models.Count;
    }

    return models[selectedModelIndex];
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

#region app-local result type
sealed class CreateResponseResult
{
    public required string ResponseId { get; init; }
    public required string ResponseContentText { get; init; }
}
#endregion
