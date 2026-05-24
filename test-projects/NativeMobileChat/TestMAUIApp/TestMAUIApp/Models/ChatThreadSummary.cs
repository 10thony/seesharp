namespace TestMAUIApp.Models;

public class ChatThreadSummary
{
    public string ConversationId { get; init; } = string.Empty;

    public string RecipientId { get; init; } = string.Empty;

    public string RecipientName { get; init; } = string.Empty;

    public ChatThreadKind Kind { get; init; } = ChatThreadKind.Direct;

    public int MemberCount { get; init; }

    public string LastMessagePreview { get; init; } = string.Empty;

    public DateTime? LastMessageAtUtc { get; init; }

    public int MessageCount { get; init; }

    public string LastMessageAtDisplay =>
        LastMessageAtUtc?.ToLocalTime().ToString("g") ?? string.Empty;

    public string ThreadTypeLabel => Kind switch
    {
        ChatThreadKind.Global => "Room",
        ChatThreadKind.Group => MemberCount > 0 ? $"Group · {MemberCount} members" : "Group",
        _ => "Direct",
    };
}
