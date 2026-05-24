using System.ComponentModel.DataAnnotations;

namespace TestNativeMobileBackendApi.Models.Auth;

public class RegisterRequest
{
    [Required]
    [MinLength(3)]
    public string UserName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MinLength(8)]
    public string Password { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
}
