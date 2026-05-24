using SQLite;

namespace TestMAUIApp.Models;

public class DirectThreadRecord
{
    [PrimaryKey]
    public string RecipientId { get; set; } = string.Empty;

    public string RecipientName { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}
