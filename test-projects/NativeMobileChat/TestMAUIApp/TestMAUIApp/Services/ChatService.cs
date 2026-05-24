using TestMAUIApp.Models;
using TestMAUIApp.Models.Api;

namespace TestMAUIApp.Services;

public class ChatService
{
    private readonly HttpService _http;
    private readonly DataBridgeService _dataBridge;
    private readonly AuthenticationService _authentication;
    private readonly UserService _userService;

    public ChatService(
        HttpService http,
        DataBridgeService dataBridge,
        AuthenticationService authentication,
        UserService userService)
    {
        _http = http;
        _dataBridge = dataBridge;
        _authentication = authentication;
        _userService = userService;
    }

    public event EventHandler? MessagesSynced;

    public Task<List<ChatMessageRecord>> GetMessagesAsync(
        bool refreshFromServer = false,
        int limit = 50,
        CancellationToken cancellationToken = default) =>
        GetMessagesAsync(Constants.GlobalConversationId, refreshFromServer, limit, cancellationToken);

    public async Task<List<ChatMessageRecord>> GetMessagesAsync(
        string conversationId,
        bool refreshFromServer = false,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (refreshFromServer && _authentication.IsAuthenticated)
        {
            await SyncGlobalMessagesAsync(limit, cancellationToken).ConfigureAwait(false);
        }

        if (conversationId == Constants.GlobalConversationId)
        {
            return await _dataBridge.GetMessagesAsync(Constants.GlobalConversationId).ConfigureAwait(false);
        }

        if (Constants.IsGroupConversation(conversationId))
        {
            return await _dataBridge.GetMessagesAsync(conversationId).ConfigureAwait(false);
        }

        var recipientId = Constants.ExtractUserId(conversationId);
        if (recipientId is null)
        {
            return [];
        }

        var globalMessages = await _dataBridge.GetMessagesAsync(Constants.GlobalConversationId).ConfigureAwait(false);
        var currentUserId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
        return ThreadService.BuildRecipientThreadMessages(globalMessages, recipientId, currentUserId);
    }

    public Task<ChatMessageRecord?> SendMessageAsync(
        string content,
        CancellationToken cancellationToken = default) =>
        SendMessageAsync(Constants.GlobalConversationId, content, cancellationToken);

    public async Task<ChatMessageRecord?> SendMessageAsync(
        string conversationId,
        string content,
        CancellationToken cancellationToken = default)
    {
        if (Constants.IsGroupConversation(conversationId))
        {
            return await SendGroupMessageAsync(conversationId, content, cancellationToken).ConfigureAwait(false);
        }

        var localMessage = new ChatMessageRecord
        {
            ConversationId = Constants.GlobalConversationId,
            SenderName = "You",
            Content = content,
            SentAtUtc = DateTime.UtcNow,
            IsOutgoing = true,
        };

        if (!_authentication.IsAuthenticated)
        {
            await _dataBridge.SaveMessageAsync(localMessage).ConfigureAwait(false);
            return localMessage;
        }

        var request = new PostChatMessageRequest { Message = content };
        var sent = await _authentication.ExecuteAuthorizedAsync(
            ct => _http.PostJsonAsync<PostChatMessageRequest, ChatMessageDto>(
                "api/chat/messages",
                request,
                ct),
            cancellationToken).ConfigureAwait(false);

        if (sent is not null)
        {
            var currentUserId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);
            localMessage = MapToRecord(sent, currentUserId);
        }

        await _dataBridge.SaveMessageAsync(localMessage).ConfigureAwait(false);
        return localMessage;
    }

    private async Task<ChatMessageRecord?> SendGroupMessageAsync(
        string conversationId,
        string content,
        CancellationToken cancellationToken)
    {
        var user = await _userService.GetCurrentUserAsync(cancellationToken).ConfigureAwait(false);
        var senderName = user?.DisplayName;
        if (string.IsNullOrWhiteSpace(senderName))
        {
            senderName = "You";
        }

        var localMessage = new ChatMessageRecord
        {
            ConversationId = conversationId,
            SenderId = user?.ExternalId ?? string.Empty,
            SenderName = senderName,
            Content = content,
            SentAtUtc = DateTime.UtcNow,
            IsOutgoing = true,
        };

        await _dataBridge.SaveMessageAsync(localMessage).ConfigureAwait(false);
        MessagesSynced?.Invoke(this, EventArgs.Empty);
        return localMessage;
    }

    public async Task SyncGlobalMessagesAsync(
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (!_authentication.IsAuthenticated)
        {
            return;
        }

        var remote = await _authentication.ExecuteAuthorizedAsync(
            ct => _http.GetJsonAsync<List<ChatMessageDto>>(
                $"api/chat/messages?limit={limit}",
                ct),
            cancellationToken).ConfigureAwait(false);

        if (remote is null)
        {
            return;
        }

        await _dataBridge.ClearMessagesAsync(Constants.GlobalConversationId).ConfigureAwait(false);

        var currentUserId = await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);

        foreach (var dto in remote)
        {
            await _dataBridge.SaveMessageAsync(MapToRecord(dto, currentUserId)).ConfigureAwait(false);
        }

        MessagesSynced?.Invoke(this, EventArgs.Empty);
    }

    private async Task<string?> GetCurrentUserIdAsync(CancellationToken cancellationToken)
    {
        var user = await _userService.GetCurrentUserAsync(cancellationToken).ConfigureAwait(false);
        return user?.ExternalId;
    }

    private static ChatMessageRecord MapToRecord(ChatMessageDto dto, string? currentUserId) =>
        new()
        {
            ConversationId = Constants.GlobalConversationId,
            SenderId = dto.UserId.ToString(),
            SenderName = dto.UserName,
            Content = dto.Message,
            SentAtUtc = dto.SentAt.UtcDateTime,
            IsOutgoing = currentUserId is not null && dto.UserId.ToString() == currentUserId,
        };
}
