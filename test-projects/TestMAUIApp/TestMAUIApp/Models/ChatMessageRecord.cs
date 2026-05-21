using SQLite;

namespace TestMAUIApp.Models;

public class ChatMessageRecord
{
    [PrimaryKey, AutoIncrement]
    public int ID { get; set; }

    public string ConversationId { get; set; } = string.Empty;

    public string SenderId { get; set; } = string.Empty;

    public string SenderName { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTime SentAtUtc { get; set; }

    public bool IsOutgoing { get; set; }
}
