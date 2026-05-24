using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TestNativeMobileBackendApi.Configuration;
using TestNativeMobileBackendApi.Interfaces;
using TestNativeMobileBackendApi.Models;
using TestNativeMobileBackendApi.Models.Auth;
using TestNativeMobileBackendApi.Services;
using Xunit;

namespace TestNativeMobileBackendApi.Tests;

public class AuthServiceUnitTests
{
    private static readonly JwtOptions Jwt = new()
    {
        Issuer = "tests",
        Audience = "tests",
        SigningKey = "unit-test-signing-key-at-least-32chars!",
        ExpiresMinutes = 30,
        RefreshTokenExpiresDays = 7,
    };

    [Fact]
    public void Refresh_WithReusedRotatedToken_RevokesAllUserRefreshTokens()
    {
        var user = NewUser("synthetic-unit");
        var users = new FakeUserRepository(user);
        var refreshRepo = new FakeRefreshTokenRepository();
        var auth = CreateService(users, refreshRepo);

        var login = auth.Login(new LoginRequest { UserName = user.UserName, Password = "Password1!" });
        Assert.NotNull(login);
        var firstRefreshToken = login!.RefreshToken;

        var rotated = auth.Refresh(new RefreshTokenRequest { RefreshToken = firstRefreshToken });
        Assert.NotNull(rotated);

        // Replay original token from before rotation: should trigger compromise handling.
        var replay = auth.Refresh(new RefreshTokenRequest { RefreshToken = firstRefreshToken });
        Assert.Null(replay);
        Assert.Contains(user.Id, refreshRepo.RevokeAllInvokedFor);

        // Latest rotated token must now also be invalid due to family revoke.
        var latest = auth.Refresh(new RefreshTokenRequest { RefreshToken = rotated!.RefreshToken });
        Assert.Null(latest);
    }

    [Fact]
    public void RevokeAllForUser_RevokesActiveTokens()
    {
        var user = NewUser("synthetic-unit-2");
        var users = new FakeUserRepository(user);
        var refreshRepo = new FakeRefreshTokenRepository();
        var auth = CreateService(users, refreshRepo);

        var login = auth.Login(new LoginRequest { UserName = user.UserName, Password = "Password1!" });
        Assert.NotNull(login);
        var refreshHash = TokenService.ComputeTokenHash(login!.RefreshToken);
        Assert.NotNull(refreshRepo.FindByHash(refreshHash));

        auth.RevokeAllForUser(user.Id);

        var refreshed = auth.Refresh(new RefreshTokenRequest { RefreshToken = login.RefreshToken });
        Assert.Null(refreshed);
    }

    private static AuthService CreateService(FakeUserRepository users, FakeRefreshTokenRepository refreshRepo)
    {
        var hasher = new PasswordHasherService();
        var seeded = users.User;
        seeded.PasswordHash = hasher.HashPassword(seeded, "Password1!");
        var tokenService = new TokenService(Options.Create(Jwt));
        return new AuthService(users, refreshRepo, hasher, tokenService, NullLogger<AuthService>.Instance);
    }

    private static AppUser NewUser(string userName) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            Email = $"{userName}@local.test",
            DisplayName = userName,
            Role = AppRoles.User,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    private sealed class FakeUserRepository : IUserRepository
    {
        public AppUser User { get; }

        public FakeUserRepository(AppUser user)
        {
            User = user;
        }

        public AppUser? FindByUserName(string userName) => userName == User.UserName ? User : null;

        public AppUser? FindById(Guid id) => id == User.Id ? User : null;

        public bool UserNameExists(string userName) => userName == User.UserName;

        public bool EmailExists(string email) => email == User.Email;

        public void Insert(AppUser user)
        {
        }

        public bool UpdateRole(Guid userId, string role)
        {
            if (userId != User.Id)
            {
                return false;
            }

            User.Role = role;
            return true;
        }

        public int CountByRole(string role) => User.Role == role ? 1 : 0;
    }

    private sealed class FakeRefreshTokenRepository : IRefreshTokenRepository
    {
        private readonly List<RefreshTokenRecord> _tokens = [];
        public List<Guid> RevokeAllInvokedFor { get; } = [];

        public void Insert(RefreshTokenRecord token)
        {
            _tokens.Add(Clone(token));
        }

        public RefreshTokenRecord? FindByHash(string tokenHash) =>
            _tokens.Where(t => t.TokenHash == tokenHash)
                .Select(Clone)
                .SingleOrDefault();

        public RefreshTokenRecord? FindActiveByHash(string tokenHash, DateTimeOffset now) =>
            _tokens.Where(t => t.TokenHash == tokenHash && t.RevokedAt is null && t.ExpiresAt > now)
                .Select(Clone)
                .SingleOrDefault();

        public void Revoke(Guid tokenId, DateTimeOffset revokedAt, string? replacedByTokenHash)
        {
            var token = _tokens.Single(t => t.Id == tokenId);
            token.RevokedAt = revokedAt;
            token.ReplacedByTokenHash = replacedByTokenHash ?? token.ReplacedByTokenHash;
        }

        public void RevokeAllForUser(Guid userId, DateTimeOffset revokedAt)
        {
            RevokeAllInvokedFor.Add(userId);
            foreach (var token in _tokens.Where(t => t.UserId == userId && t.RevokedAt is null))
            {
                token.RevokedAt = revokedAt;
            }
        }

        private static RefreshTokenRecord Clone(RefreshTokenRecord token) =>
            new()
            {
                Id = token.Id,
                UserId = token.UserId,
                TokenHash = token.TokenHash,
                ExpiresAt = token.ExpiresAt,
                CreatedAt = token.CreatedAt,
                RevokedAt = token.RevokedAt,
                ReplacedByTokenHash = token.ReplacedByTokenHash,
            };
    }
}
