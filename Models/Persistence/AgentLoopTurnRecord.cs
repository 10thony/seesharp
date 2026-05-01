namespace SeeSharp.Models.Persistence;

public sealed class AgentLoopTurnRecord
{
    public required string TaskRunId { get; init; }
    public required int TurnNumber { get; init; }
    public required string AssistantText { get; init; }
    public required string ToolCallsJson { get; init; }
    public required string ToolResultsJson { get; init; }
    public required int SuccessfulToolExecutionsSoFar { get; init; }
    public required int ContextResetCount { get; init; }
    public required long CreatedAtUnixMs { get; init; }
}
