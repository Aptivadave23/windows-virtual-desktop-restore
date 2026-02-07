using System.ComponentModel.DataAnnotations;

namespace StartUp.Services.Models;

public class AppOption
{
    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Path { get; set; } = string.Empty;

    public string? Args { get; set; }

    /// <summary>
    /// Target desktop name or index string.
    /// </summary>
    [Required]
    public string Desktop { get; set; } = string.Empty;
}
