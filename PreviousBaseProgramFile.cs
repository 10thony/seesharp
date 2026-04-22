/*
 
using OpenAI;
using OpenAI.Conversations;
using OpenAI.Models;
using OpenAI.Responses;
using System.ClientModel;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

// Traffic goes only to LM Studio. The OpenAI .NET SDK is used because LM Studio exposes the same /v1 routes
// and wire models (e.g. OpenAIModel, ResponseResult); type names come from the library, not from calling OpenAI.

#region functional running of the program
const string lmStudioBaseUri = "http://cobec-spark:1234/v1";
ConsoleColor YouColor = ConsoleColor.Magenta;
ConsoleColor AgentColor = ConsoleColor.DarkCyan;
ConsoleColor ErrorColor = ConsoleColor.Red;
ConsoleColor ResetColor = ConsoleColor.White;
ConsoleColor AgentToolColor = ConsoleColor.Green;
ConsoleColor AgentReasoningColor = ConsoleColor.Cyan;
string _relativePathBase = ResolveWorkspaceRoot();
string READ_TOOL_NAME = "READ_FILE";
string LIST_FILE_TOOL_NAME = "LIST_FILE";
string EDIT_FILE_TOOL_NAME = "EDIT_FILE";


// LM Studio often ignores this; the SDK still requires a credential value.
string apiKey = Environment.GetEnvironmentVariable("LMSTUDIO_API_KEY") ?? "lm-studio";
var credential = new ApiKeyCredential(apiKey);
var clientOptions = new OpenAIClientOptions
{
    Endpoint = new Uri(lmStudioBaseUri)
};

var lmStudioModelsClient = new OpenAIModelClient(credential, clientOptions);
var lmStudioResponsesClient = new ResponsesClient(credential, clientOptions);

StringBuilder availableModelsSB = new StringBuilder();
availableModelsSB.AppendLine("LM Studio — select a loaded model:");

using var cts = new CancellationTokenSource();
List<OpenAIModel> models = (await lmStudioModelsClient.GetModelsAsync(cts.Token)).Value.ToList();

for (int i = 0; i < models.Count; i++)
{
    availableModelsSB.AppendLine($"({i}): - {models[i].Id}");
}
Console.ForegroundColor = AgentColor;
Console.WriteLine(availableModelsSB.ToString());

string userInput = Console.ReadLine() ?? "";
OpenAIModel selectedModel = PromptUserToSelectModel(userInput, models);

Console.ForegroundColor = ResetColor;
Console.WriteLine($"Selected model: {selectedModel.Id}");

await AgentLoop(lmStudioResponsesClient, selectedModel.Id);
#endregion




async Task AgentLoop(ResponsesClient lmStudioResponsesClient, string modelId)
{
    while (true)
    {
        Console.ForegroundColor = ResetColor;
        Console.WriteLine("Enter a prompt for the model (or type 'exit' to quit):");
        string userPrompt = Console.ReadLine() ?? "";
        if (userPrompt.Equals("exit", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Exiting the program. Goodbye!");
            break;
        }

        Console.ForegroundColor = YouColor;
        CreateResponseResult responseResult = await StartNewResponseAsync(
            modelId,
            userPrompt,
            lmStudioResponsesClient);

        Console.ForegroundColor = YouColor;
        Console.WriteLine($"You: {userPrompt}");


        var invocations = ExtractToolCallInvocations(responseResult.ResponseContentText);

        if(invocations.Count == 0) 
        {
            Console.ForegroundColor = AgentColor;
            Console.WriteLine($" {responseResult.ResponseContentText}");
        }

        foreach(var (toolName, args) in invocations)
        {
            var toolInvocationResult = ExecuteToolInvocation(toolName, args);
            string payload = JsonSerializer.Serialize(toolInvocationResult);

            
        }
    }
}

async Task<CreateResponseResult> StartNewResponseAsync(
    string modelId,
    string input,
    ResponsesClient lmStudioResponsesClient,
    CancellationToken cancellation = default)
{
    if (string.IsNullOrWhiteSpace(modelId))
    {
        throw new ArgumentException("Model ID is required.", nameof(modelId));
    }

    if (string.IsNullOrWhiteSpace(input))
    {
        throw new ArgumentException("Input is required.", nameof(input));
    }

    ClientResult<ResponseResult> clientResult = await lmStudioResponsesClient.CreateResponseAsync(
        modelId,
        input,
        cancellationToken: cancellation);

    ResponseResult response = clientResult.Value;
    return new CreateResponseResult
    {
        ResponseId = response.Id ?? "",
        ResponseContentText = response.GetOutputText() ?? ""
    };
}

#region tool implementations
/// <summary>
/// register new tools here and put the tool names in a string variable at the top where we declare global variables.
/// </summary>
Dictionary<string, string> GetToolkitInformation()
{

    Dictionary<string, string> result = new Dictionary<string, string>();

    result.Add(READ_TOOL_NAME,
        "Gets the full content of a file. Relative filenames are resolved from the project/workspace root (folder with the .csproj), not the executable folder.\r\n    :param filename: Path to the file.\r\n    :return: The full content of the file.");

    result.Add(LIST_FILE_TOOL_NAME,
        "Lists files in a directory. Relative paths are resolved from the project/workspace root.\r\n    :param directory: Path of the directory to list.\r\n    :return: A list of file paths.");

    result.Add(EDIT_FILE_TOOL_NAME,
        "Writes or edits a file on disk. Relative path uses the workspace root.\r\n" +
        "    :param path: File to write.\r\n" +
        "    :param oldContents: Exact substring to replace (copy from READ_FILE tool_result). Use empty string \"\" to replace the entire file with newContents.\r\n" +
        "    :param newContents: New text (full file when oldContents is empty; otherwise replaces the first occurrence of oldContents).\r\n" +
        "    :return: Edit confirmation.");

    return result;
}
/// <summary>
/// implement new tools here
/// </summary>
Dictionary<string, object> ReadFile_Tool(string fileName)
{
    Dictionary<string, object> result = new Dictionary<string, object>();

    string fullPath = ResolveAbsPath(fileName);

    Console.ForegroundColor = AgentToolColor;
    Console.WriteLine($"[Tool] Reading file: {fullPath}");

    try
    {
        var contents = System.IO.File.ReadAllText(fullPath);

        result = new Dictionary<string, object>()
        {
            { "file_path", fullPath },
            { "content", contents }
        };
    }
    catch(Exception ex)
    {
        Console.ForegroundColor = ErrorColor;
        Console.WriteLine($"Error reading file: {ex.Message}");
    }

    return result;
}

Dictionary<string, object> ListFiles_Tool(string directoryPath)
{
    Dictionary<string, object> result = new Dictionary<string, object>();
    string fullPath = ResolveAbsPath(directoryPath);
    Console.ForegroundColor = AgentToolColor;
    Console.WriteLine($"[Tool]: Listing files in directory at:");
    Console.WriteLine(fullPath);
    try
    {
        var files = Directory.GetFiles(fullPath);

        result = new Dictionary<string, object>()
        {
            { "file_path", fullPath },
            { "files", files }
        };

        Console.ForegroundColor = ResetColor;
    }
    catch (Exception e)
    {
        Console.ForegroundColor = ErrorColor;
        Console.WriteLine($"Error listing files in directory: {fullPath}");
        Console.WriteLine(e.Message);
        throw;
    }
    return result;
}

Dictionary<string, object> EditFile_Tool(string path, string oldContents, string newContents)
{
    Dictionary<string, object> result = new Dictionary<string, object>();
    string fullPath = ResolveAbsPath(path);

    if (string.IsNullOrEmpty(oldContents))
    {
        Console.ForegroundColor = AgentToolColor;
        Console.WriteLine($"[Tool]: Creating new file at:");
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

    Console.ForegroundColor = AgentToolColor;
    Console.WriteLine($"[Tool]: Editing file at:");
    var editedContent = originalContents.Remove(fileIndex, oldContents.Length).Insert(fileIndex, newContents);
    System.IO.File.WriteAllText(fullPath, editedContent, Encoding.UTF8);

    result = new Dictionary<string, object>()
            {
                { "path", fullPath },
                { "action", "File Edited" }
            };
    Console.ForegroundColor = ResetColor;
    return result;
}

 Dictionary<string, object> ExecuteToolInvocation(string toolName, Dictionary<string, object?> args)
{
    try
    {
        return toolName switch
        {
            nameof(READ_TOOL_NAME) => ReadFile_Tool(
                RequireArg(args, "filename", "fileName")),
            nameof(LIST_FILE_TOOL_NAME) => ListFiles_Tool(
                RequireArg(args, "directory", "path", "directoryPath")),
            nameof(EDIT_FILE_TOOL_NAME) => EditFile_Tool(
                RequireArg(args, "path"),
                OptionalArg(args, "oldContents", "old_str", "oldStr") ?? "",
                OptionalArg(args, "newContents", "new_str", "newStr") ?? ""),
            _ => new Dictionary<string, object>
                    {
                        { "error", $"Unknown tool: {toolName}" }
                    }
        };
    }
    catch (Exception ex)
    {
        return new Dictionary<string, object>
                {
                    { "error", ex.Message }
                };
    }
}

string RequireArg(Dictionary<string, object?> args, params string[] keys)
{
    string? v = OptionalArg(args, keys);
    if (string.IsNullOrEmpty(v))
        throw new ArgumentException($"Missing required argument; expected one of: {string.Join(", ", keys)}");
    return v;
}

string? OptionalArg(Dictionary<string, object?> args, params string[] keys)
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

///<summary>
///toolkit utils go under here
/// </summary>
/// 

List<(string toolName, Dictionary<string, object?> args)> ExtractToolCallInvocations(string llmResponseText)
{
    List<(string toolName, Dictionary<string, object?> args)> toolCalls = new List<(string toolName, Dictionary<string, object?> args)>();

    foreach (string rawLine in llmResponseText.Trim().Split(
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

            if (openParenIndex == -1)
                continue;

            var name = after[..openParenIndex].Trim();
            var rest = after[(openParenIndex + 1)..];

            if (!rest.EndsWith(')'))
                continue;

            var jsonStr = NormalizeToolCallJson(rest[..^1].Trim());
            if (IsPlaceholderToolJson(jsonStr))
            {
                Console.WriteLine($"Skipping tool line (placeholder, not real JSON): {line}");
                continue;
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var args = JsonSerializer.Deserialize<Dictionary<string, object?>>(jsonStr, options);

            if (args is not null)
                toolCalls.Add((name, args));
        }
        catch (Exception e)
        {
            Console.ForegroundColor = ErrorColor;
            Console.WriteLine($"Error parsing tool call from line: {line}");
            Console.WriteLine(e.Message);
        }
    }

    return toolCalls;
}

string GenerateToolRegistryAsString()
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


#endregion

#region Utilities

/// <summary>
/// Small models often emit doubled outer braces, e.g. <c>{{"filename":"a.txt"}}</c>, which is not valid JSON.
/// Peel matching outer <c>{</c> / <c>}</c> pairs until the payload parses or no longer starts/ends with double braces.
/// </summary>
string NormalizeToolCallJson(string jsonStr)
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

bool IsPlaceholderToolJson(string jsonStr)
{
    if (string.IsNullOrWhiteSpace(jsonStr))
        return true;
    return string.Equals(jsonStr.Trim(), "{JSON_ARGS}", StringComparison.OrdinalIgnoreCase);
}
string GenerateSystemPrompt()
{

    StringBuilder sb = new StringBuilder();
    sb.AppendLine("You are a coding assistant whose goal is to help us solve coding tasks. ");
    sb.AppendLine("You have access to a series of tools you can execute. Here are the tools you can execute:");
    sb.AppendLine(GenerateToolRegistryAsString());
    sb.AppendLine();
    sb.AppendLine("TOOL TURNS: Whenever you call a tool, your entire reply must be exactly one line starting with \"tool:\" and nothing else on that line (no markdown, no preamble, no code fences).");
    sb.AppendLine("Format: tool: TOOL_NAME({\"argName\":\"value\"}) with valid JSON inside the parentheses.");
    sb.AppendLine("Example read: tool: READ_FILE({\"filename\":\"questions.txt\"})");
    sb.AppendLine();
    sb.AppendLine("AFTER READ_FILE: If the user asked you to update, edit, answer, or write into that same file, your very next reply after you see tool_result(...) for the read MUST be a single tool: EDIT_FILE(...) line.");
    sb.AppendLine("Do not ask what to write. Infer sensible answers (e.g. reply to questions or greetings in the file) and persist them with EDIT_FILE.");
    sb.AppendLine("Whole-file rewrite (simplest): oldContents is \"\" and newContents is the entire new file text.");
    sb.AppendLine("Example after reading questions.txt (one line, real JSON): tool: EDIT_FILE({\"path\":\"questions.txt\",\"oldContents\":\"\",\"newContents\":\"hi how are you doing? Answer: I am doing well — thank you. How can I help you today?\"})");
    sb.AppendLine();
    sb.AppendLine("EDIT_FILE details: (1) Replace entire file: oldContents \"\", newContents = full new text. (2) Replace first occurrence of a substring: oldContents must match the file exactly (copy from the latest READ_FILE tool_result \"content\" field); newContents is the replacement.");
    sb.AppendLine();
    sb.AppendLine("FINAL TURN: Only after every requested disk change is done (you received tool_result with success for EDIT_FILE), you may answer in normal language to summarize. Until then, keep using tool lines as required.");

    return sb.ToString();
}
string ResolveAbsPath(string pathStr)
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

string ResolveWorkspaceRoot()
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

#endregion

#region get models functions
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
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("Invalid input. Please enter a valid number corresponding to the model you want to select:");
        userInput = Console.ReadLine() ?? "";
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

 */