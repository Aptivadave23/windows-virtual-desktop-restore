// SPDX-License-Identifier: Unlicense

namespace Startup
{
    public sealed class AppCfg
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string? Args { get; set; }
        public string Desktop { get; set; } = "0"; // "Thing 1" | "Thing 2" | "0" | "1"
    }
}