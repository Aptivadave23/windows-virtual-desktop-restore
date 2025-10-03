// SPDX-License-Identifier: Unlicense

using System.Text.Json;
using System.Text.Json.Serialization;
using Startup;

namespace StartUp
{
    public sealed class WorkspaceConfig
    {
        [JsonPropertyName("desktops")] public List<DesktopCfg> Desktops { get; set; } = new();
        [JsonPropertyName("apps")] public List<AppCfg> Apps { get; set; } = new();
        [JsonPropertyName("launchDelayMs")] public int LaunchDelayMs { get; set; } = 800;
    }
}