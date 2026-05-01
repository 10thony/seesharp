using System.ComponentModel.DataAnnotations;

namespace TestNativeMobileBackendApi.Models;

public class TodoItem
{
    [Required]
    public string ID { get; set; } = string.Empty;

    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Notes { get; set; } = string.Empty;

    public bool Done { get; set; }
}
