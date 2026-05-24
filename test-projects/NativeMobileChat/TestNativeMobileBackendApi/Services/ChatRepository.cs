using Npgsql;
using TestNativeMobileBackendApi.Interfaces;
using TestNativeMobileBackendApi.Models;

namespace TestNativeMobileBackendApi.Services;

public class ChatRepository : IChatRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public ChatRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public IEnumerable<ChatMessage> GetRecent(int limit)
    {
        var messages = new List<ChatMessage>();
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            """
            SELECT id, user_id, user_name, message, sent_at
            FROM chat_messages
            ORDER BY sent_at DESC
            LIMIT @limit
            """,
            connection);
        command.Parameters.AddWithValue("limit", limit);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            messages.Add(Map(reader));
        }

        messages.Reverse();
        return messages;
    }

    public ChatMessage Insert(Guid userId, string userName, string message)
    {
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            """
            INSERT INTO chat_messages (user_id, user_name, message)
            VALUES (@userId, @userName, @message)
            RETURNING id, user_id, user_name, message, sent_at
            """,
            connection);
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("userName", userName);
        command.Parameters.AddWithValue("message", message);
        using var reader = command.ExecuteReader();
        reader.Read();
        return Map(reader);
    }

    private static ChatMessage Map(NpgsqlDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            UserId = reader.GetGuid(1),
            UserName = reader.GetString(2),
            Message = reader.GetString(3),
            SentAt = reader.GetFieldValue<DateTimeOffset>(4)
        };
}
