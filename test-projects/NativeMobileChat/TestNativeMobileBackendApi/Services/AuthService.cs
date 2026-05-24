using TestNativeMobileBackendApi.Interfaces;
using TestNativeMobileBackendApi.Models;
using TestNativeMobileBackendApi.Models.Auth;
using Microsoft.Extensions.Logging;

namespace TestNativeMobileBackendApi.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly PasswordHasherService _passwordHasher;
    private readonly TokenService _tokenService;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IUserRepository users,
        IRefreshTokenRepository refreshTokens,
        PasswordHasherService passwordHasher,
        TokenService tokenService,
        ILogger<AuthService> logger)
    {
        _users = users;
        _refreshTokens = refreshTokens;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _logger = logger;
    }

    public AuthResponse Register(RegisterRequest request)
    {
        if (_users.UserNameExists(request.UserName))
        {
            throw new InvalidOperationException("UserNameInUse");
        }

        if (_users.EmailExists(request.Email))
        {
            throw new InvalidOperationException("EmailInUse");
        }

        var now = DateTimeOffset.UtcNow;
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = request.UserName.Trim(),
            Email = request.Email.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
                ? request.UserName.Trim()
                : request.DisplayName.Trim(),
            Role = AppRoles.User,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);
        _users.Insert(user);

        return IssueTokens(user);
    }

    public AuthResponse? Login(LoginRequest request)
    {
        var user = _users.FindByUserName(request.UserName.Trim());
        if (user is null || !_passwordHasher.VerifyPassword(user, request.Password))
        {
            return null;
        }

        return IssueTokens(user);
    }

    public AuthResponse? Refresh(RefreshTokenRequest request)
    {
        var incomingToken = request.RefreshToken.Trim();
        if (string.IsNullOrEmpty(incomingToken))
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var incomingHash = TokenService.ComputeTokenHash(incomingToken);
        var existing = _refreshTokens.FindActiveByHash(incomingHash, now);
        if (existing is null)
        {
            var anyMatch = _refreshTokens.FindByHash(incomingHash);
            if (anyMatch is not null && anyMatch.RevokedAt is not null && !string.IsNullOrEmpty(anyMatch.ReplacedByTokenHash))
            {
                _logger.LogWarning("Refresh token reuse detected for user {UserId}. Revoking all refresh tokens.", anyMatch.UserId);
                _refreshTokens.RevokeAllForUser(anyMatch.UserId, now);
            }
            return null;
        }

        var user = _users.FindById(existing.UserId);
        if (user is null)
        {
            _refreshTokens.Revoke(existing.Id, now, replacedByTokenHash: null);
            return null;
        }

        var rotated = _tokenService.CreateRefreshToken();
        _refreshTokens.Insert(new RefreshTokenRecord
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = rotated.TokenHash,
            ExpiresAt = rotated.ExpiresAt,
            CreatedAt = now
        });
        _refreshTokens.Revoke(existing.Id, now, rotated.TokenHash);

        return _tokenService.CreateAuthResponse(user, rotated.PlainToken, rotated.ExpiresAt);
    }

    public bool RevokeRefreshToken(string refreshToken)
    {
        var incomingToken = refreshToken.Trim();
        if (string.IsNullOrEmpty(incomingToken))
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var existing = _refreshTokens.FindActiveByHash(TokenService.ComputeTokenHash(incomingToken), now);
        if (existing is null)
        {
            return false;
        }

        _refreshTokens.Revoke(existing.Id, now, replacedByTokenHash: null);
        return true;
    }

    public void RevokeAllForUser(Guid userId)
    {
        _refreshTokens.RevokeAllForUser(userId, DateTimeOffset.UtcNow);
    }

    private AuthResponse IssueTokens(AppUser user)
    {
        var now = DateTimeOffset.UtcNow;
        var refresh = _tokenService.CreateRefreshToken();
        _refreshTokens.Insert(new RefreshTokenRecord
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = refresh.TokenHash,
            ExpiresAt = refresh.ExpiresAt,
            CreatedAt = now
        });

        return _tokenService.CreateAuthResponse(user, refresh.PlainToken, refresh.ExpiresAt);
    }
}
