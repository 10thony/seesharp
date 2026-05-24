using SQLite;

namespace TestMAUIApp.Models;

public class ChatGroupRecord
{
    [PrimaryKey]
    public string GroupId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Comma-separated user ids (GUID strings).</summary>
    public string MemberIds { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}
