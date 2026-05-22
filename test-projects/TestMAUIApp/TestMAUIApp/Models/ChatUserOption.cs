namespace TestMAUIApp.Models;

public class ChatUserOption
{
    public string UserId { get; init; } = string.Empty;

    public string UserName { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Label => string.IsNullOrWhiteSpace(DisplayName) ? UserName : DisplayName;
}
