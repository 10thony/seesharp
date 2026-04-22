using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Text;
using System.Text.Json;

// Example: OpenAI .NET Chat Completions API with native function tools (ChatTool / ToolChatMessage).
// This follows the loop described in the openai-dotnet README:
// https://github.com/openai/openai-dotnet?tab=readme-ov-file#how-to-use-chat-completions-with-tools-and-function-calling
//
// Tool wire names and workspace-relative path behavior align with Program.cs (READ_FILE, LIST_FILE, EDIT_FILE).

/// <summary>
/// Hosts the same conceptual tools as Program.cs, but exposed to the model via
/// <see cref="ChatTool"/> definitions and resolved through <see cref="ChatFinishReason.ToolCalls"/>.
/// </summary>
public sealed class ChatCompletionsToolCallingExample
{
    // const wire names — match ChatTool.CreateFunctionTool(..., functionName: ...) exactly.
    public const string ReadFileToolName = "READ_FILE";
    public const string ListFileToolName = "LIST_FILE";
    public const string EditFileToolName = "EDIT_FILE";

    private readonly string _workspaceRoot;

    public ChatCompletionsToolCallingExample(string? workspaceRoot = null)
    {
        _workspaceRoot = string.IsNullOrWhiteSpace(workspaceRoot)
            ? ResolveWorkspaceRoot()
            : Path.GetFullPath(workspaceRoot.Trim());
    }

    /// <summary>LM Studio–style base URL; override when constructing <see cref="OpenAIClientOptions.Endpoint"/>.</summary>
    public const string DefaultLmStudioBaseUri = "http://cobec-spark:1234/v1";

    /// <summary>
    /// Builds a <see cref="ChatClient"/> pointed at a compatible server (e.g. LM Studio) using the same pattern as Program.cs.
    /// </summary>
    public static ChatClient CreateChatClient(string model, Uri endpoint, string? apiKey = null)
    {
        string key = apiKey ?? Environment.GetEnvironmentVariable("LMSTUDIO_API_KEY") ?? "lm-studio";
        var credential = new ApiKeyCredential(key);
        var options = new OpenAIClientOptions { Endpoint = endpoint };
        return new ChatClient(model, credential, options);
    }

    /// <summary>Registers READ_FILE, LIST_FILE, EDIT_FILE for <see cref="ChatClient.CompleteChatAsync"/>.</summary>
    public ChatCompletionOptions CreateToolOptions() => new()
    {
        Tools = { ReadFileChatTool, ListFileChatTool, EditFileChatTool },
    };

    private static readonly ChatTool ReadFileChatTool = ChatTool.CreateFunctionTool(
        functionName: ReadFileToolName,
        functionDescription:
        "Gets the full content of a file. Relative filenames are resolved from the project/workspace root (folder with the .csproj), not the executable folder.",
        functionParameters: ParametersSchema("""
            {
                "type": "object",
                "properties": {
                    "filename": {
                        "type": "string",
                        "description": "Path to the file, relative to workspace root unless absolute."
                    }
                },
                "required": [ "filename" ],
                "additionalProperties": false
            }
            """));

    private static readonly ChatTool ListFileChatTool = ChatTool.CreateFunctionTool(
        functionName: ListFileToolName,
        functionDescription: "Lists files in a directory. Relative paths are resolved from the project/workspace root.",
        functionParameters: ParametersSchema("""
            {
                "type": "object",
                "properties": {
                    "directory": {
                        "type": "string",
                        "description": "Directory to list, relative to workspace root (e.g. \".\" for root)."
                    }
                },
                "required": [ "directory" ],
                "additionalProperties": false
            }
            """));

    private static readonly ChatTool EditFileChatTool = ChatTool.CreateFunctionTool(
        functionName: EditFileToolName,
        functionDescription:
        "Writes or edits a file on disk. Relative path uses the workspace root. Use empty oldContents to replace the entire file.",
        functionParameters: ParametersSchema("""
            {
                "type": "object",
                "properties": {
                    "path": { "type": "string", "description": "File path relative to workspace root unless absolute." },
                    "oldContents": {
                        "type": "string",
                        "description": "Exact substring to replace (from READ_FILE). Empty string replaces entire file with newContents."
                    },
                    "newContents": { "type": "string", "description": "New text to write or replacement text." }
                },
                "required": [ "path", "oldContents", "newContents" ],
                "additionalProperties": false
            }
            """));

    private static BinaryData ParametersSchema(string json) => BinaryData.FromBytes(Encoding.UTF8.GetBytes(json));

    /// <summary>
    /// Runs the standard tool loop: call the model, on ToolCalls execute C# handlers, append <see cref="ToolChatMessage"/>, repeat until Stop.
    /// </summary>
    public async Task<string> CompleteWithToolsAsync(
        ChatClient client,
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        List<ChatMessage> messages = [new UserChatMessage(userMessage)];
        ChatCompletionOptions options = CreateToolOptions();

        bool requiresAction;
        do
        {
            requiresAction = false;
            ChatCompletion completion = await client.CompleteChatAsync(messages, options, cancellationToken);

            switch (completion.FinishReason)
            {
                case ChatFinishReason.Stop:
                    messages.Add(new AssistantChatMessage(completion));
                    break;

                case ChatFinishReason.ToolCalls:
                    messages.Add(new AssistantChatMessage(completion));
                    foreach (ChatToolCall toolCall in completion.ToolCalls)
                    {
                        string payload = JsonSerializer.Serialize(ExecuteToolCall(toolCall));
                        messages.Add(new ToolChatMessage(toolCall.Id, payload));
                    }

                    requiresAction = true;
                    break;

                case ChatFinishReason.Length:
                    throw new NotSupportedException(
                        "Incomplete model output (max tokens or context limit). Increase limits or shorten the conversation.");

                case ChatFinishReason.ContentFilter:
                    throw new NotSupportedException("Response omitted due to content filter.");

                case ChatFinishReason.FunctionCall:
                    throw new NotSupportedException("Legacy function_call finish reason; use tool_calls.");

                default:
                    throw new NotSupportedException($"Unhandled finish reason: {completion.FinishReason}");
            }
        } while (requiresAction);

        for (int i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i] is AssistantChatMessage assistant &&
                assistant.Content.Count > 0 &&
                !string.IsNullOrEmpty(assistant.Content[0].Text))
            {
                return assistant.Content[0].Text;
            }
        }

        return "";
    }

    /// <summary>Parses <see cref="ChatToolCall.FunctionArguments"/> and dispatches the same way as Program.cs <c>ExecuteToolInvocation</c>.</summary>
    private Dictionary<string, object> ExecuteToolCall(ChatToolCall toolCall)
    {
        Dictionary<string, object?> args = ParseFunctionArguments(toolCall.FunctionArguments);
        string name = (toolCall.FunctionName ?? "").Trim();

        try
        {
            string key = name.ToUpperInvariant();
            return key switch
            {
                ReadFileToolName => ReadFile_Tool(RequireArg(args, "filename", "fileName")),
                ListFileToolName => ListFiles_Tool(RequireArg(args, "directory", "path", "directoryPath")),
                EditFileToolName => EditFile_Tool(
                    RequireArg(args, "path"),
                    OptionalArg(args, "oldContents", "old_str", "oldStr") ?? "",
                    OptionalArg(args, "newContents", "new_str", "newStr") ?? ""),
                _ => new Dictionary<string, object> { { "error", $"Unknown tool: {toolCall.FunctionName}" } },
            };
        }
        catch (Exception ex)
        {
            return new Dictionary<string, object> { { "error", ex.Message } };
        }
    }

    private static Dictionary<string, object?> ParseFunctionArguments(BinaryData? functionArguments)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        string json = "{}";
        if (functionArguments is not null && functionArguments.ToMemory().Length > 0)
            json = functionArguments.ToString();
        if (string.IsNullOrWhiteSpace(json))
            json = "{}";
        using JsonDocument doc = JsonDocument.Parse(json);
        foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
            dict[prop.Name] = prop.Value.Clone();
        return dict;
    }

    private static string RequireArg(Dictionary<string, object?> args, params string[] keys)
    {
        string? v = OptionalArg(args, keys);
        if (string.IsNullOrEmpty(v))
            throw new ArgumentException($"Missing required argument; expected one of: {string.Join(", ", keys)}");
        return v;
    }

    private static string? OptionalArg(Dictionary<string, object?> args, params string[] keys)
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

    private Dictionary<string, object> ReadFile_Tool(string fileName)
    {
        var result = new Dictionary<string, object>();
        string fullPath = ResolveAbsPath(fileName);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[Chat tool] Reading file: {fullPath}");
        Console.ResetColor();
        try
        {
            string contents = File.ReadAllText(fullPath);
            result["file_path"] = fullPath;
            result["content"] = contents;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error reading file: {ex.Message}");
            Console.ResetColor();
            result["error"] = ex.Message;
        }

        return result;
    }

    private Dictionary<string, object> ListFiles_Tool(string directoryPath)
    {
        var result = new Dictionary<string, object>();
        string fullPath = ResolveAbsPath(directoryPath);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[Chat tool] Listing files in: {fullPath}");
        Console.ResetColor();
        try
        {
            string[] files = Directory.GetFiles(fullPath);
            result["file_path"] = fullPath;
            result["files"] = files;
        }
        catch (Exception e)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error listing directory: {fullPath} — {e.Message}");
            Console.ResetColor();
            result["error"] = e.Message;
        }

        return result;
    }

    private Dictionary<string, object> EditFile_Tool(string path, string oldContents, string newContents)
    {
        string fullPath = ResolveAbsPath(path);

        if (string.IsNullOrEmpty(oldContents))
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[Chat tool] Writing full file: {fullPath}");
            Console.ResetColor();
            File.WriteAllText(fullPath, newContents, Encoding.UTF8);
            return new Dictionary<string, object> { { "path", fullPath }, { "action", "File Created" } };
        }

        string originalContents = File.ReadAllText(fullPath, Encoding.UTF8);
        int fileIndex = originalContents.IndexOf(oldContents, StringComparison.Ordinal);
        if (fileIndex == -1)
        {
            return new Dictionary<string, object>
            {
                { "path", fullPath },
                { "action", "Old contents not found. No changes made." },
            };
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[Chat tool] Editing file: {fullPath}");
        Console.ResetColor();
        string edited = originalContents.Remove(fileIndex, oldContents.Length).Insert(fileIndex, newContents);
        File.WriteAllText(fullPath, edited, Encoding.UTF8);
        return new Dictionary<string, object> { { "path", fullPath }, { "action", "File Edited" } };
    }

    private string ResolveAbsPath(string pathStr)
    {
        if (string.IsNullOrWhiteSpace(pathStr))
            throw new ArgumentException("Path cannot be null or empty.", nameof(pathStr));
        if (pathStr.StartsWith('~'))
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            pathStr = Path.Combine(home, pathStr.TrimStart('~', '/', '\\'));
        }

        if (!Path.IsPathRooted(pathStr))
            pathStr = Path.GetFullPath(Path.Combine(_workspaceRoot, pathStr));
        else
            pathStr = Path.GetFullPath(pathStr);

        return pathStr;
    }

    private static string ResolveWorkspaceRoot()
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
            for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
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
