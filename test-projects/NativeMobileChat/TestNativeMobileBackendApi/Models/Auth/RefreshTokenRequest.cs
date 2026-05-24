using System.ComponentModel.DataAnnotations;

namespace TestNativeMobileBackendApi.Models.Auth;

public class RefreshTokenRequest
{
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}
