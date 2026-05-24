using Microsoft.AspNetCore.Identity;
using TestNativeMobileBackendApi.Models;

namespace TestNativeMobileBackendApi.Services;

public class PasswordHasherService
{
    private readonly PasswordHasher<AppUser> _hasher = new();

    public string HashPassword(AppUser user, string password) =>
        _hasher.HashPassword(user, password);

    public bool VerifyPassword(AppUser user, string password)
    {
        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
