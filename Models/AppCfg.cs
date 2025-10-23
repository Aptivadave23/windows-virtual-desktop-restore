// SPDX-License-Identifier: Unlicense

namespace Startup
{
    public sealed class AppCfg
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string? Args { get; set; }
        public string Desktop { get; set; } = "0"; // "Thing 1" | "Thing 2" | "0" | "1"
        public bool WaitForWindow { get; set; } = false; // Whether to wait for the app's window to appear before continuing
        public int LaunchTimeoutMs { get; set; } = 4500; // How long to wait for the app's window to appear before continuing
    }
}