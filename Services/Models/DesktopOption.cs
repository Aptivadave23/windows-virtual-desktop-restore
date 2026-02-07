using System.ComponentModel.DataAnnotations;

namespace StartUp.Services.Models;

public class DesktopOption
{
    [Range(0, 99)]
    public int? Index { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;
}
