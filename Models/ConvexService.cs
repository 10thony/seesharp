using Convex.Client;
using SeeSharp.Models.Persistence;

namespace SeeSharp.Models;

public sealed class ConvexService : IAsyncDisposable
{
    private readonly IConvexClient _client;

    private ConvexService(IConvexClient client)
    {
        _client = client;
    }

    public static async Task<ConvexService> CreateFromLocalEnvAsync(CancellationToken cancellationToken = default)
    {
        ConvexConfig config = await LoadRequiredConfigAsync(cancellationToken).ConfigureAwait(false);
        IConvexClient client = new ConvexClientBuilder()
            .UseDeployment(config.DeploymentUrl)
            .WithTimeout(TimeSpan.FromSeconds(30))
            .WithAutoReconnect(maxAttempts: 5, delayMs: 1000)
            .Build();
        await client.Auth.SetAdminAuthAsync(config.AdminKey).ConfigureAwait(false);
        return new ConvexService(client);
    }

    public async Task SaveTaskRunStartAsync(TaskRunRecord run, CancellationToken cancellationToken = default)
    {
        _ = await _client.Mutate<object>("taskRuns:create")
            .WithArgs(new
            {
                taskRunId = run.TaskRunId,
                modelId = run.ModelId,
                taskText = run.TaskText,
                status = run.Status,
                repoContextSummary = run.RepoContextSummary,
                startedAt = run.StartedAtUnixMs
            })
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SaveAgentLoopTurnAsync(AgentLoopTurnRecord turn, CancellationToken cancellationToken = default)
    {
        _ = await _client.Mutate<object>("agentLoopTurns:create")
            .WithArgs(new
            {
                taskRunId = turn.TaskRunId,
                turnNumber = turn.TurnNumber,
                assistantText = turn.AssistantText,
                toolCallsJson = turn.ToolCallsJson,
                toolResultsJson = turn.ToolResultsJson,
                successfulToolExecutionsSoFar = turn.SuccessfulToolExecutionsSoFar,
                contextResetCount = turn.ContextResetCount,
                createdAt = turn.CreatedAtUnixMs
            })
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SaveToolExecutionAsync(ToolExecutionRecord record, CancellationToken cancellationToken = default)
    {
        _ = await _client.Mutate<object>("toolExecutions:create")
            .WithArgs(new
            {
                taskRunId = record.TaskRunId,
                turnNumber = record.TurnNumber,
                toolName = record.ToolName,
                argsJson = record.ArgsJson,
                resultJson = record.ResultJson,
                ok = record.Ok,
                createdAt = record.CreatedAtUnixMs
            })
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task CompleteTaskRunAsync(TaskRunRecord run, CancellationToken cancellationToken = default)
    {
        _ = await _client.Mutate<object>("taskRuns:complete")
            .WithArgs(new
            {
                taskRunId = run.TaskRunId,
                status = run.Status,
                completedAt = run.CompletedAtUnixMs,
                finalAssistantText = run.FinalAssistantText
            })
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        switch (_client)
        {
            case IAsyncDisposable asyncDisposable:
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                break;
            case IDisposable disposable:
                disposable.Dispose();
                break;
        }
    }

    private static async Task<ConvexConfig> LoadRequiredConfigAsync(CancellationToken cancellationToken)
    {
        string? envUrl = Environment.GetEnvironmentVariable("CONVEX_SELF_HOSTED_URL");
        string? envAdminKey = Environment.GetEnvironmentVariable("CONVEX_SELF_HOSTED_ADMIN_KEY");
        if (!string.IsNullOrWhiteSpace(envUrl) && !string.IsNullOrWhiteSpace(envAdminKey))
        {
            return new ConvexConfig
            {
                DeploymentUrl = envUrl.Trim(),
                AdminKey = envAdminKey.Trim()
            };
        }

        string repoRoot = ResolveSeeSharpRepoRoot();
        string envFile = Path.Combine(repoRoot, "infra", ".env.local");
        if (!File.Exists(envFile))
        {
            throw new InvalidOperationException(
                "Convex configuration was not found. Set CONVEX_SELF_HOSTED_URL and CONVEX_SELF_HOSTED_ADMIN_KEY, " +
                "or create infra/.env.local.");
        }

        Dictionary<string, string> kv = await ParseDotEnvAsync(envFile, cancellationToken).ConfigureAwait(false);
        if (!kv.TryGetValue("CONVEX_SELF_HOSTED_URL", out string? fileUrl) || string.IsNullOrWhiteSpace(fileUrl) ||
            !kv.TryGetValue("CONVEX_SELF_HOSTED_ADMIN_KEY", out string? fileAdminKey) || string.IsNullOrWhiteSpace(fileAdminKey))
        {
            throw new InvalidOperationException(
                "Missing CONVEX_SELF_HOSTED_URL and/or CONVEX_SELF_HOSTED_ADMIN_KEY in infra/.env.local.");
        }

        return new ConvexConfig
        {
            DeploymentUrl = fileUrl.Trim(),
            AdminKey = fileAdminKey.Trim()
        };
    }

    private static string ResolveSeeSharpRepoRoot()
    {
        for (DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string csproj = Path.Combine(dir.FullName, "SeeSharp.csproj");
            if (File.Exists(csproj))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Could not resolve SeeSharp repository root from AppContext.BaseDirectory.");
    }

    private static async Task<Dictionary<string, string>> ParseDotEnvAsync(string envFile, CancellationToken cancellationToken)
    {
        string[] lines = await File.ReadAllLinesAsync(envFile, cancellationToken).ConfigureAwait(false);
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            int idx = line.IndexOf('=');
            if (idx <= 0)
            {
                continue;
            }

            string key = line[..idx].Trim();
            string value = line[(idx + 1)..].Trim().Trim('"');
            if (key.Length == 0)
            {
                continue;
            }

            result[key] = value;
        }

        return result;
    }
}
