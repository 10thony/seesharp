using Npgsql;
using TestNativeMobileBackendApi.Interfaces;
using TestNativeMobileBackendApi.Models;

namespace TestNativeMobileBackendApi.Services;

public class UserRepository : IUserRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public UserRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public AppUser? FindByUserName(string userName)
    {
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            """
            SELECT id, user_name, email, password_hash, display_name, role, is_active, created_at, updated_at
            FROM app_users
            WHERE user_name = @userName AND is_active = TRUE
            """,
            connection);
        command.Parameters.AddWithValue("userName", userName);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public AppUser? FindById(Guid id)
    {
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            """
            SELECT id, user_name, email, password_hash, display_name, role, is_active, created_at, updated_at
            FROM app_users
            WHERE id = @id AND is_active = TRUE
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public bool UserNameExists(string userName)
    {
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            "SELECT 1 FROM app_users WHERE user_name = @userName LIMIT 1",
            connection);
        command.Parameters.AddWithValue("userName", userName);
        return command.ExecuteScalar() is not null;
    }

    public bool EmailExists(string email)
    {
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            "SELECT 1 FROM app_users WHERE email = @email LIMIT 1",
            connection);
        command.Parameters.AddWithValue("email", email);
        return command.ExecuteScalar() is not null;
    }

    public void Insert(AppUser user)
    {
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            """
            INSERT INTO app_users (id, user_name, email, password_hash, display_name, role, is_active, created_at, updated_at)
            VALUES (@id, @userName, @email, @passwordHash, @displayName, @role, @isActive, @createdAt, @updatedAt)
            """,
            connection);
        command.Parameters.AddWithValue("id", user.Id);
        command.Parameters.AddWithValue("userName", user.UserName);
        command.Parameters.AddWithValue("email", user.Email);
        command.Parameters.AddWithValue("passwordHash", user.PasswordHash);
        command.Parameters.AddWithValue("displayName", user.DisplayName);
        command.Parameters.AddWithValue("role", user.Role);
        command.Parameters.AddWithValue("isActive", user.IsActive);
        command.Parameters.AddWithValue("createdAt", user.CreatedAt);
        command.Parameters.AddWithValue("updatedAt", user.UpdatedAt);
        command.ExecuteNonQuery();
    }

    public bool UpdateRole(Guid userId, string role)
    {
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            """
            UPDATE app_users
            SET role = @role,
                updated_at = NOW()
            WHERE id = @id
            """,
            connection);
        command.Parameters.AddWithValue("id", userId);
        command.Parameters.AddWithValue("role", role);
        return command.ExecuteNonQuery() > 0;
    }

    public int CountByRole(string role)
    {
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            """
            SELECT COUNT(*)
            FROM app_users
            WHERE role = @role
              AND is_active = TRUE
            """,
            connection);
        command.Parameters.AddWithValue("role", role);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public IReadOnlyList<AppUser> ListActiveChatUsers(Guid? excludeUserId = null)
    {
        var users = new List<AppUser>();
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            """
            SELECT id, user_name, email, password_hash, display_name, role, is_active, created_at, updated_at
            FROM app_users
            WHERE is_active = TRUE
              AND role IN ('User', 'Admin')
              AND (@excludeUserId IS NULL OR id <> @excludeUserId)
            ORDER BY display_name, user_name
            """,
            connection);
        command.Parameters.AddWithValue("excludeUserId", (object?)excludeUserId ?? DBNull.Value);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            users.Add(Map(reader));
        }

        return users;
    }

    private static AppUser Map(NpgsqlDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            UserName = reader.GetString(1),
            Email = reader.GetString(2),
            PasswordHash = reader.GetString(3),
            DisplayName = reader.GetString(4),
            Role = reader.GetString(5),
            IsActive = reader.GetBoolean(6),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(7),
            UpdatedAt = reader.GetFieldValue<DateTimeOffset>(8)
        };
}
