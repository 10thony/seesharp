using SQLite;

namespace TestMAUIApp.Models;

public class LocalUser
{
    [PrimaryKey, AutoIncrement]
    public int ID { get; set; }

    public string ExternalId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
}
