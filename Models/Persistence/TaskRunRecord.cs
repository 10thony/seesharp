namespace SeeSharp.Models.Persistence;

public sealed class TaskRunRecord
{
    public required string TaskRunId { get; init; }
    public required string ModelId { get; init; }
    public required string TaskText { get; init; }
    public required string Status { get; init; }
    public required string RepoContextSummary { get; init; }
    public required long StartedAtUnixMs { get; init; }
    public long? CompletedAtUnixMs { get; init; }
    public string FinalAssistantText { get; init; } = "";
}
