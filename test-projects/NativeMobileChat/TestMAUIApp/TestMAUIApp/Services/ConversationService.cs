using TestMAUIApp.Models;

namespace TestMAUIApp.Services;

public class ConversationService
{
    private readonly DataBridgeService _dataBridge;

    public ConversationService(DataBridgeService dataBridge)
    {
        _dataBridge = dataBridge;
    }

    public async Task<ChatThreadSummary> StartDirectThreadAsync(
        string recipientId,
        string recipientName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _dataBridge.SaveDirectThreadAsync(new DirectThreadRecord
        {
            RecipientId = recipientId,
            RecipientName = recipientName,
            CreatedAtUtc = DateTime.UtcNow,
        }).ConfigureAwait(false);

        return new ChatThreadSummary
        {
            ConversationId = Constants.UserConversationId(recipientId),
            RecipientId = recipientId,
            RecipientName = recipientName,
            Kind = ChatThreadKind.Direct,
            LastMessagePreview = "No messages yet",
            MessageCount = 0,
        };
    }

    public async Task<ChatThreadSummary> CreateGroupAsync(
        string name,
        IReadOnlyList<ChatUserOption> members,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (members.Count == 0)
        {
            throw new InvalidOperationException("Select at least one member for the group.");
        }

        var groupId = Guid.NewGuid().ToString();
        var memberIds = string.Join(',', members.Select(m => m.UserId));

        await _dataBridge.SaveGroupAsync(new ChatGroupRecord
        {
            GroupId = groupId,
            Name = name.Trim(),
            MemberIds = memberIds,
            CreatedAtUtc = DateTime.UtcNow,
        }).ConfigureAwait(false);

        return new ChatThreadSummary
        {
            ConversationId = Constants.GroupConversationId(groupId),
            RecipientId = groupId,
            RecipientName = name.Trim(),
            Kind = ChatThreadKind.Group,
            MemberCount = members.Count,
            LastMessagePreview = "No messages yet",
            MessageCount = 0,
        };
    }

    public static IReadOnlyList<string> ParseMemberIds(string memberIds) =>
        string.IsNullOrWhiteSpace(memberIds)
            ? []
            : memberIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
