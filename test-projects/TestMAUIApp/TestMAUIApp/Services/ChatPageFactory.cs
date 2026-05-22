using TestMAUIApp.Models;
using TestMAUIApp.Pages;

namespace TestMAUIApp.Services;

public class ChatPageFactory
{
    private readonly ChatService _chatService;
    private readonly RealtimeChatService _realtimeChatService;
    private readonly AuthenticationService _authenticationService;

    public ChatPageFactory(
        ChatService chatService,
        RealtimeChatService realtimeChatService,
        AuthenticationService authenticationService)
    {
        _chatService = chatService;
        _realtimeChatService = realtimeChatService;
        _authenticationService = authenticationService;
    }

    public ChatPage Create(ChatThreadSummary thread) =>
        new(thread, _chatService, _realtimeChatService, _authenticationService);
}
