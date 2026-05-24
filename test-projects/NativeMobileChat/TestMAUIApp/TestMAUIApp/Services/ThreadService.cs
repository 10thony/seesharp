using TestMAUIApp.Models;

namespace TestMAUIApp.Services;

public class ThreadService
{
    private readonly DataBridgeService _dataBridge;
    private readonly UserService _userService;

    public ThreadService(
        DataBridgeService dataBridge,
        UserService userService)
    {
        _dataBridge = dataBridge;
        _userService = userService;
    }

    public async Task<List<ChatThreadSummary>> GetThreadSummariesAsync(
        CancellationToken cancellationToken = default)
    {
        var globalMessages = await _dataBridge.GetMessagesAsync(Constants.GlobalConversationId).ConfigureAwait(false);
        var currentUserId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);

        var summaries = new Dictionary<string, ChatThreadSummary>(StringComparer.Ordinal)
        {
            [Constants.GlobalConversationId] = BuildGlobalSummary(globalMessages),
        };

        foreach (var direct in await _dataBridge.GetDirectThreadsAsync().ConfigureAwait(false))
        {
            var conversationId = Constants.UserConversationId(direct.RecipientId);
            summaries[conversationId] = BuildEmptyDirectSummary(direct);
        }

        var recipientGroups = globalMessages
            .Where(message => !message.IsOutgoing && !string.IsNullOrWhiteSpace(message.SenderId))
            .Where(message => message.SenderId != currentUserId)
            .GroupBy(message => message.SenderId);

        foreach (var group in recipientGroups)
        {
            var threadMessages = BuildRecipientThreadMessages(globalMessages, group.Key, currentUserId);
            var lastMessage = threadMessages.LastOrDefault();
            var conversationId = Constants.UserConversationId(group.Key);

            summaries[conversationId] = new ChatThreadSummary
            {
                ConversationId = conversationId,
                RecipientId = group.Key,
                RecipientName = group.First().SenderName,
                Kind = ChatThreadKind.Direct,
                LastMessagePreview = TruncatePreview(lastMessage?.Content),
                LastMessageAtUtc = lastMessage?.SentAtUtc,
                MessageCount = threadMessages.Count,
            };
        }

        foreach (var chatGroup in await _dataBridge.GetGroupsAsync().ConfigureAwait(false))
        {
            var conversationId = Constants.GroupConversationId(chatGroup.GroupId);
            var groupMessages = await _dataBridge.GetMessagesAsync(conversationId).ConfigureAwait(false);
            var lastMessage = groupMessages.LastOrDefault();
            var memberCount = ConversationService.ParseMemberIds(chatGroup.MemberIds).Count;

            summaries[conversationId] = new ChatThreadSummary
            {
                ConversationId = conversationId,
                RecipientId = chatGroup.GroupId,
                RecipientName = chatGroup.Name,
                Kind = ChatThreadKind.Group,
                MemberCount = memberCount,
                LastMessagePreview = TruncatePreview(lastMessage?.Content),
                LastMessageAtUtc = lastMessage?.SentAtUtc,
                MessageCount = groupMessages.Count,
            };
        }

        return summaries.Values
            .OrderByDescending(summary => summary.LastMessageAtUtc ?? DateTime.MinValue)
            .ThenBy(summary => summary.RecipientName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<ChatMessageRecord> BuildRecipientThreadMessages(
        IReadOnlyList<ChatMessageRecord> globalMessages,
        string recipientId,
        string? currentUserId)
    {
        return globalMessages
            .Where(message => message.IsOutgoing || message.SenderId == recipientId)
            .OrderBy(message => message.SentAtUtc)
            .ToList();
    }

    private static ChatThreadSummary BuildGlobalSummary(IReadOnlyList<ChatMessageRecord> globalMessages)
    {
        var lastMessage = globalMessages.LastOrDefault();

        return new ChatThreadSummary
        {
            ConversationId = Constants.GlobalConversationId,
            RecipientId = Constants.GlobalConversationId,
            RecipientName = "Everyone",
            Kind = ChatThreadKind.Global,
            LastMessagePreview = TruncatePreview(lastMessage?.Content),
            LastMessageAtUtc = lastMessage?.SentAtUtc,
            MessageCount = globalMessages.Count,
        };
    }

    private static ChatThreadSummary BuildEmptyDirectSummary(DirectThreadRecord direct) =>
        new()
        {
            ConversationId = Constants.UserConversationId(direct.RecipientId),
            RecipientId = direct.RecipientId,
            RecipientName = direct.RecipientName,
            Kind = ChatThreadKind.Direct,
            LastMessagePreview = "No messages yet",
            MessageCount = 0,
        };

    private async Task<string?> GetCurrentUserIdAsync(CancellationToken cancellationToken)
    {
        var user = await _userService.GetCurrentUserAsync(cancellationToken).ConfigureAwait(false);
        return user?.ExternalId;
    }

    private static string TruncatePreview(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        return content.Length <= 80 ? content : $"{content[..77]}...";
    }
}
