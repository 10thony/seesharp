using System.ComponentModel.DataAnnotations;

namespace TestNativeMobileBackendApi.Models.Admin;

public class UpdateUserRoleRequest
{
    [Required]
    public string Role { get; set; } = string.Empty;
}
