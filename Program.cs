
using OpenAI;
using OpenAI.Chat;
using OpenAI.Conversations;
using OpenAI.Models;
using OpenAI.Responses;
using SeeSharp.Dev;
using SeeSharp.Models;
using System.ClientModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

// Traffic goes only to LM Studio. The OpenAI .NET SDK is used because LM Studio exposes the same /v1 routes
// and wire models (e.g. OpenAIModel, ResponseResult); type names come from the library, not from calling OpenAI.

#region functional running of the program
ThemedConsole.Initialize();
// Default path is the interactive test harness. Watch relaunch runs only for --legacy (avoids host output interleaving with prompts when using `dotnet run` without the debugger).
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

string contextualizerModelId = Environment.GetEnvironmentVariable("SEESHARP_CONTEXTUALIZER_MODEL")
    ?? Agent.DefaultContextualizerModelId;
var contextualizerChatClient = new ChatClient(contextualizerModelId, credential, clientOptions);

StringBuilder availableModelsSB = new StringBuilder();
availableModelsSB.AppendLine("LM Studio — select a loaded model:");


using var cts = new CancellationTokenSource();
List<OpenAIModel> models = (await lmStudioModelsClient.GetModelsAsync(cts.Token)).Value.ToList();

if (!LocalTestProjectMenu.IsLegacyAllModelsMode(args))
{
    await LocalTestProjectMenu.RunAsync(credential, clientOptions, models, contextualizerChatClient, cts.Token);
    return;
}

//Dictionary<string,string> questions = new Dictionary<string, string>()
//{
//    {"ReadOne","Read the Program.cs file and tell me what it does." },
//    {"EditOne","Edit the Agent.cs file so it is a regular class instead of an abstract class." },
//    {"ListOne","List the files in the codebase, study what there is, " +
//    "determine the most important files to read, and tell me what this app does." },
//    {"UnclearOne","How can i build this program?" },
//    {"UnclearTwo","Study this codebase and let me know what it does." },
//};

List<string> questions = new List<string>()
{
    //"Read the Program.cs file and tell me what it does.",
    //"Edit the Agent.cs file so it is a regular class instead of an abstract class.",
    "what does cobec inc do? here is there homepage: https://www.cobec.com/",
    //"List the files in the codebase, study what there is, determine the most important files to read," +
    //" and tell me what this app does.",
    //"How can i build this program?",
    //"Study this codebase and let me know what it does." //"List the files in the codebase, study what there is, determine the most important files to read," +
    //" and tell me what this app does.",
    //"How can i build this program?",
    //"Study this codebase and let me know what it does."
};

LMStudioToolKit toolKit = new LMStudioToolKit();

foreach (OpenAIModel model in models)
{
    LMStudioAgent agent = new(model, toolKit, contextualizerChatClient);


    StringBuilder agentLoopStrings = await
           agent.AgentLoop(new ResponsesClient(credential, clientOptions),
                           questions,
                           cts.Token);
}


#endregion


#region get models functions
static bool TryRelaunchUnderDotnetWatch(string[] args)
{
    if (string.Equals(Environment.GetEnvironmentVariable("SEESHARP_DISABLE_WATCH"), "1", StringComparison.Ordinal))
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
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOTNET_WATCH_ITERATION")))
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
            "[DevMode] Relaunched under `dotnet watch run` for --legacy (set SEESHARP_DISABLE_WATCH=1 to bypass).");
        return true;
    }
    catch (Exception ex)
    {
        ThemedConsole.WriteLine(TerminalTone.Error, $"[DevMode] Failed to relaunch under dotnet watch: {ex.Message}");
        ThemedConsole.WriteLine(TerminalTone.Reasoning, "[DevMode] Continuing without watch mode.");
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
#endregion

#region app-local result type
sealed class CreateResponseResult
{
    public required string ResponseId { get; init; }
    public required string ResponseContentText { get; init; }
}
#endregion
