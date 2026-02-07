// SPDX-License-Identifier: Unlicense

using System;
using System.IO;
using StartUp;
using StartUp.Services;
using StartUp.Services.Abstractions;
using StartUp.Services.Impl;
using Startup;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Core services
        IFileSystem fs = new FileSystem();
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BootWorkspace",
            "bootworkspace.log");
        ILog log = new LogWriter(logPath);
        IClock clock = new Clock();
        IDesktopApi desktopApi = new DesktopApiVirtualDesktop();
        IConfigLoader configLoader = new ConfigLoader(fs);
        IProcessLauncher processLauncher = new ProcessLauncher(fs, log);

        // App services
        var runtime = new Runtime(configLoader, desktopApi, processLauncher, fs, clock, log);
        var installer = new Installer(fs, log);

        if (args.Length > 0)
        {
            var command = args[0].Trim().ToLowerInvariant();
            switch (command)
            {
                case "--install":
                    var cfgPath = args.Length > 1 ? args[1] : null;
                    installer.Install(cfgPath);
                    return;
                case "--uninstall":
                    installer.Uninstall();
                    return;
            }
        }

        var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "workspace.json");
        runtime.Run(configPath);
    }
}
