using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace StartUp.Services.Models;

public class WorkspaceOptions
{
    public List<DesktopOption>? Desktops { get; set; }

    [Required]
    public List<AppOption> Apps { get; set; } = new();

    [Range(0, 60000)]
    public int LaunchDelayMs { get; set; } = 1500;
}
