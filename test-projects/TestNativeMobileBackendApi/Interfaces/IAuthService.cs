using TestNativeMobileBackendApi.Models.Auth;

namespace TestNativeMobileBackendApi.Interfaces;

public interface IAuthService
{
    AuthResponse Register(RegisterRequest request);
    AuthResponse? Login(LoginRequest request);
    AuthResponse? Refresh(RefreshTokenRequest request);
    bool RevokeRefreshToken(string refreshToken);
    void RevokeAllForUser(Guid userId);
}
