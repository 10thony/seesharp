using Npgsql;
using TestNativeMobileBackendApi.Interfaces;
using TestNativeMobileBackendApi.Models;

namespace TestNativeMobileBackendApi.Services;

public class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public RefreshTokenRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public void Insert(RefreshTokenRecord token)
    {
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            """
            INSERT INTO refresh_tokens (id, user_id, token_hash, expires_at, created_at, revoked_at, replaced_by_token_hash)
            VALUES (@id, @userId, @tokenHash, @expiresAt, @createdAt, @revokedAt, @replacedByTokenHash)
            """,
            connection);
        command.Parameters.AddWithValue("id", token.Id);
        command.Parameters.AddWithValue("userId", token.UserId);
        command.Parameters.AddWithValue("tokenHash", token.TokenHash);
        command.Parameters.AddWithValue("expiresAt", token.ExpiresAt);
        command.Parameters.AddWithValue("createdAt", token.CreatedAt);
        command.Parameters.AddWithValue("revokedAt", (object?)token.RevokedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("replacedByTokenHash", (object?)token.ReplacedByTokenHash ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public RefreshTokenRecord? FindByHash(string tokenHash)
    {
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            """
            SELECT id, user_id, token_hash, expires_at, created_at, revoked_at, replaced_by_token_hash
            FROM refresh_tokens
            WHERE token_hash = @tokenHash
            LIMIT 1
            """,
            connection);
        command.Parameters.AddWithValue("tokenHash", tokenHash);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public RefreshTokenRecord? FindActiveByHash(string tokenHash, DateTimeOffset now)
    {
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            """
            SELECT id, user_id, token_hash, expires_at, created_at, revoked_at, replaced_by_token_hash
            FROM refresh_tokens
            WHERE token_hash = @tokenHash
              AND revoked_at IS NULL
              AND expires_at > @now
            LIMIT 1
            """,
            connection);
        command.Parameters.AddWithValue("tokenHash", tokenHash);
        command.Parameters.AddWithValue("now", now);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public void Revoke(Guid tokenId, DateTimeOffset revokedAt, string? replacedByTokenHash)
    {
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            """
            UPDATE refresh_tokens
            SET revoked_at = @revokedAt,
                replaced_by_token_hash = COALESCE(@replacedByTokenHash, replaced_by_token_hash)
            WHERE id = @id
            """,
            connection);
        command.Parameters.AddWithValue("id", tokenId);
        command.Parameters.AddWithValue("revokedAt", revokedAt);
        command.Parameters.AddWithValue("replacedByTokenHash", (object?)replacedByTokenHash ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public void RevokeAllForUser(Guid userId, DateTimeOffset revokedAt)
    {
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            """
            UPDATE refresh_tokens
            SET revoked_at = @revokedAt
            WHERE user_id = @userId
              AND revoked_at IS NULL
            """,
            connection);
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("revokedAt", revokedAt);
        command.ExecuteNonQuery();
    }

    private static RefreshTokenRecord Map(NpgsqlDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            UserId = reader.GetGuid(1),
            TokenHash = reader.GetString(2),
            ExpiresAt = reader.GetFieldValue<DateTimeOffset>(3),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(4),
            RevokedAt = reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
            ReplacedByTokenHash = reader.IsDBNull(6) ? null : reader.GetString(6)
        };
}
