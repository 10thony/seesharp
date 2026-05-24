using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using TestNativeMobileBackendApi.Configuration;
using TestNativeMobileBackendApi.Interfaces;
using TestNativeMobileBackendApi.Hubs;

namespace TestNativeMobileBackendApi.Controllers;

[Authorize(Policy = AuthorizationPolicies.ChatUser)]
[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly IChatRepository _chatRepository;
    private readonly IHubContext<ChatHub> _hubContext;

    public ChatController(IChatRepository chatRepository, IHubContext<ChatHub> hubContext)
    {
        _chatRepository = chatRepository;
        _hubContext = hubContext;
    }

    [HttpGet("messages")]
    public IActionResult GetMessages([FromQuery] int limit = 50)
    {
        var cappedLimit = Math.Clamp(limit, 1, 200);
        return Ok(_chatRepository.GetRecent(cappedLimit));
    }

    [HttpPost("messages")]
    public async Task<IActionResult> PostMessage([FromBody] PostChatMessageRequest request)
    {
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(ModelState);
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var userName = User.FindFirstValue(ClaimTypes.Name);
        if (userId is null || !Guid.TryParse(userId, out var id) || string.IsNullOrEmpty(userName))
        {
            return Unauthorized();
        }

        var saved = _chatRepository.Insert(id, userName, request.Message.Trim());
        await _hubContext.Clients.All.SendAsync("ReceiveMessage", saved.UserName, saved.Message, saved.SentAt);
        return Ok(saved);
    }
}

public class PostChatMessageRequest
{
    [Required]
    public string Message { get; set; } = string.Empty;
}
