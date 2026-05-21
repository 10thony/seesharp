using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TestNativeMobileBackendApi.Configuration;
using TestNativeMobileBackendApi.Models;
using TestNativeMobileBackendApi.Models.Auth;

namespace TestNativeMobileBackendApi.Services;

public class TokenService
{
    private readonly JwtOptions _options;

    public TokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public AuthResponse CreateAuthResponse(
        AppUser user,
        string refreshToken,
        DateTimeOffset refreshTokenExpiresAt)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_options.ExpiresMinutes);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new AuthResponse
        {
            AccessToken = new JwtSecurityTokenHandler().WriteToken(token),
            RefreshToken = refreshToken,
            ExpiresAt = expiresAt,
            RefreshTokenExpiresAt = refreshTokenExpiresAt,
            User = ToProfile(user)
        };
    }

    public (string PlainToken, string TokenHash, DateTimeOffset ExpiresAt) CreateRefreshToken()
    {
        var rawBytes = RandomNumberGenerator.GetBytes(64);
        var plainToken = Convert.ToBase64String(rawBytes);
        var tokenHash = ComputeTokenHash(plainToken);
        var expiresAt = DateTimeOffset.UtcNow.AddDays(_options.RefreshTokenExpiresDays);
        return (plainToken, tokenHash, expiresAt);
    }

    public static string ComputeTokenHash(string token)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hashBytes);
    }

    public static UserProfile ToProfile(AppUser user) =>
        new()
        {
            Id = user.Id,
            UserName = user.UserName,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Role = user.Role
        };
}
