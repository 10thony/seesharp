using TestMAUIApp.Models;
using TestMAUIApp.Models.Api;

namespace TestMAUIApp.Services;

public class ChatService
{
    private readonly HttpService _http;
    private readonly DataBridgeService _dataBridge;
    private readonly AuthenticationService _authentication;

    public ChatService(
        HttpService http,
        DataBridgeService dataBridge,
        AuthenticationService authentication)
    {
        _http = http;
        _dataBridge = dataBridge;
        _authentication = authentication;
    }

    public async Task<List<ChatMessageRecord>> GetMessagesAsync(
        bool refreshFromServer = false,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (refreshFromServer && _authentication.IsAuthenticated)
        {
            var remote = await _authentication.ExecuteAuthorizedAsync(
                ct => _http.GetJsonAsync<List<ChatMessageDto>>(
                    $"api/chat/messages?limit={limit}",
                    ct),
                cancellationToken).ConfigureAwait(false);

            if (remote is not null)
            {
                await _dataBridge.ClearMessagesAsync(Constants.GlobalConversationId).ConfigureAwait(false);

                var currentUserId = _authentication.AccessToken is null
                    ? null
                    : await GetCurrentUserIdAsync(cancellationToken).ConfigureAwait(false);

                foreach (var dto in remote)
                {
                    await _dataBridge.SaveMessageAsync(MapToRecord(dto, currentUserId)).ConfigureAwait(false);
                }
            }
        }

        return await _dataBridge.GetMessagesAsync(Constants.GlobalConversationId).ConfigureAwait(false);
    }

    public async Task<ChatMessageRecord?> SendMessageAsync(
        string content,
        CancellationToken cancellationToken = default)
    {
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

    private async Task<string?> GetCurrentUserIdAsync(CancellationToken cancellationToken)
    {
        var user = await MobileAppServices.UserService.GetCurrentUserAsync(cancellationToken).ConfigureAwait(false);
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
