namespace SeeSharp.Models.Persistence;

public sealed class ToolExecutionRecord
{
    public required string TaskRunId { get; init; }
    public required int TurnNumber { get; init; }
    public required string ToolName { get; init; }
    public required string ArgsJson { get; init; }
    public required string ResultJson { get; init; }
    public required bool Ok { get; init; }
    public required long CreatedAtUnixMs { get; init; }
}
