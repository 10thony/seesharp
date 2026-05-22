namespace TestMAUIApp.Models.Api;

public class PostChatMessageRequest
{
    public string Message { get; set; } = string.Empty;
}

public class ChatMessageDto
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public DateTimeOffset SentAt { get; set; }
}

public class ChatUserDto
{
    public Guid Id { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
}
