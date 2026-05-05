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
        string repoRoot = ResolveSeeSharpRepoRoot();
        string envFile = Path.Combine(repoRoot, "infra", ".env.local");
        ConvexConfig? fileConfig = null;
        if (File.Exists(envFile))
        {
            Dictionary<string, string> kv = await ParseDotEnvAsync(envFile, cancellationToken).ConfigureAwait(false);
            string? fileUrl = FirstNonEmpty(
                kv,
                "CONVEX_URL",
                "CONVEX_CLOUD_URL",
                "CONVEX_SELF_HOSTED_URL");
            string? fileAdminKey = FirstNonEmpty(
                kv,
                "CONVEX_ADMIN_KEY",
                "CONVEX_DEPLOY_KEY",
                "CONVEX_SELF_HOSTED_ADMIN_KEY");
            if (!string.IsNullOrWhiteSpace(fileUrl) &&
                !string.IsNullOrWhiteSpace(fileUrl) &&
                !string.IsNullOrWhiteSpace(fileAdminKey))
            {
                fileConfig = new ConvexConfig
                {
                    DeploymentUrl = NormalizeConfigValue(fileUrl),
                    AdminKey = NormalizeConfigValue(fileAdminKey)
                };
            }
        }

        string? envUrl = FirstNonEmpty(
            Environment.GetEnvironmentVariable("CONVEX_URL"),
            Environment.GetEnvironmentVariable("CONVEX_CLOUD_URL"),
            Environment.GetEnvironmentVariable("CONVEX_SELF_HOSTED_URL"));
        string? envAdminKey = FirstNonEmpty(
            Environment.GetEnvironmentVariable("CONVEX_ADMIN_KEY"),
            Environment.GetEnvironmentVariable("CONVEX_DEPLOY_KEY"),
            Environment.GetEnvironmentVariable("CONVEX_SELF_HOSTED_ADMIN_KEY"));
        ConvexConfig? envConfig = null;
        if (!string.IsNullOrWhiteSpace(envUrl) && !string.IsNullOrWhiteSpace(envAdminKey))
        {
            envConfig = new ConvexConfig
            {
                DeploymentUrl = NormalizeConfigValue(envUrl),
                AdminKey = NormalizeConfigValue(envAdminKey)
            };
        }

        if (fileConfig is not null)
        {
            if (envConfig is not null &&
                (!string.Equals(fileConfig.DeploymentUrl, envConfig.DeploymentUrl, StringComparison.Ordinal) ||
                 !string.Equals(fileConfig.AdminKey, envConfig.AdminKey, StringComparison.Ordinal)))
            {
                ThemedConsole.WriteLine(
                    TerminalTone.Reasoning,
                    "[Convex] Using infra/.env.local credentials; process environment values differ.");
            }

            return fileConfig;
        }

        if (envConfig is not null)
        {
            ThemedConsole.WriteLine(
                TerminalTone.Reasoning,
                "[Convex] Using process environment credentials.");
            return envConfig;
        }

        throw new InvalidOperationException(
            "Convex configuration was not found. Set CONVEX_URL and CONVEX_DEPLOY_KEY (or CONVEX_ADMIN_KEY), " +
            "or create infra/.env.local. Backward-compatible self-hosted names are also supported.");
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
            string value = line[(idx + 1)..].Trim().Trim('"').Trim('\'');
            if (key.Length == 0)
            {
                continue;
            }

            result[key] = value;
        }

        return result;
    }

    private static string NormalizeConfigValue(string value)
    {
        return value.Trim().Trim('"').Trim('\'');
    }

    private static string? FirstNonEmpty(Dictionary<string, string> values, params string[] keys)
    {
        foreach (string key in keys)
        {
            if (values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
