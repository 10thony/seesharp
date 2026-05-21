using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using TestNativeMobileBackendApi.Configuration;
using TestNativeMobileBackendApi.Interfaces;

namespace TestNativeMobileBackendApi.Hubs;

[Authorize(Policy = AuthorizationPolicies.ChatUser)]
public class ChatHub : Hub
{
    private readonly IChatRepository _chatRepository;

    public ChatHub(IChatRepository chatRepository)
    {
        _chatRepository = chatRepository;
    }

    public async Task SendMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var userId = GetUserId();
        var userName = Context.User?.FindFirstValue(ClaimTypes.Name) ?? "unknown";
        var saved = _chatRepository.Insert(userId, userName, message.Trim());
        await Clients.All.SendAsync("ReceiveMessage", saved.UserName, saved.Message, saved.SentAt);
    }

    private Guid GetUserId()
    {
        var id = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(id, out var userId)
            ? userId
            : throw new HubException("Authenticated user id is required.");
    }
}
