using TestNativeMobileBackendApi.Models;

namespace TestNativeMobileBackendApi.Interfaces;

public interface IRefreshTokenRepository
{
    void Insert(RefreshTokenRecord token);
    RefreshTokenRecord? FindByHash(string tokenHash);
    RefreshTokenRecord? FindActiveByHash(string tokenHash, DateTimeOffset now);
    void Revoke(Guid tokenId, DateTimeOffset revokedAt, string? replacedByTokenHash);
    void RevokeAllForUser(Guid userId, DateTimeOffset revokedAt);
}
