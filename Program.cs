// SPDX-License-Identifier: Unlicense

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using WindowsDesktop;
using ArgumentOutOfRangeException = System.ArgumentOutOfRangeException;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.InteropServices;
using System.Globalization;
using ABI.Windows.ApplicationModel.Core;
using Startup;
using StartUp;
internal static class Program
{
    

    [STAThread]
    private static void Main(string[] args)
    {
        var logPath = Path.Combine(InstallDir, "bootworkspace.log");

        //single instance check
        if (!AcquireSingleInstanceMutex(out var mtx))
        {
            //another instance is running
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Another instance is already running. Exiting.{Environment.NewLine}");
            return;
        }


        try
        {

            //Debounce quick repeat runs (e.g., if Explorer crashes and restarts)
            if (ShouldExitDueToRecentRun(InstallDir, seconds: 300))
            {
                File.AppendAllText(logPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Recent run detected. Exiting.{Environment.NewLine}");
                return;
            }

            Directory.CreateDirectory(InstallDir);
            File.AppendAllText(logPath, $"---- BootWorkspace run at {DateTime.Now} ----{Environment.NewLine}");

            //small cushion at logon
            Thread.Sleep(5000);

            //wait for explorer to be running (so virtual desktops are available)
            WaitForExplorerReady();
            File.AppendAllText(logPath,
                $"Explorer is running. Current desktop index: {GetCurrentIndex()}{Environment.NewLine}");

            //--install/uninstall handlers--//
            if (args.Length > 0 && args[0].Equals("--install", StringComparison.OrdinalIgnoreCase))
            {
                string? cfgArg = args.Length > 1 ? args[1] : null;
                InstallSelf(cfgArg);
                return;
            }

            if (args.Length > 0 && args[0].Equals("--uninstall", StringComparison.OrdinalIgnoreCase))
            {
                UninstallSelf();
                return;
            }

            //get the config file that specifies what to launch and where to launch it
            var configFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "workspace.json");
            File.AppendAllText(logPath, $"BaseDir={AppDomain.CurrentDomain.BaseDirectory}\nConfig={configFile}\n");

            WorkspaceConfig cfg;
            if (File.Exists(configFile)) cfg = LoadConfig(configFile);
            else throw new FileNotFoundException($"Config file '{configFile}' not found");

            File.AppendAllText(logPath, $"Apps={cfg.Apps?.Count ?? 0}\n");

            // ensure we have at least two desktops and name them
            File.AppendAllText(logPath, $"Desktops(before)={WindowsDesktop.VirtualDesktop.GetDesktops().Length}\n");
            EnsureDesktopsFor(cfg);
            File.AppendAllText(logPath, $"Desktops(after)={WindowsDesktop.VirtualDesktop.GetDesktops().Length}\n");

            // read config and launch apps
            //foreach (var app in cfg.Apps)
            //{
            //    // switch to the requested desktop for the app
            //    var index = ParseDesktop(app.Desktop, cfg);
            //    SwitchToDesktopIndex(index);

            //    //launch app
            //    var exe = ResolveExe(app.Path);
            //    if (string.IsNullOrWhiteSpace(exe))
            //    {
            //        Console.Error.WriteLine($"Skipping app '{app.Name}': no path specified.");
            //        continue;
            //    }
            //    var psi = new ProcessStartInfo {UseShellExecute = true};

            //    if(LooksLikeUri(exe))
            //    {
            //        //e.g., micros-edge:https://www.bing.com, or shell:AppsFolder\...
            //        psi.FileName = exe;
            //    }
            //    else
            //    {
            //        //normal exe path or something on PATH (e.g., "notepad.exe")
            //        psi.FileName = exe;
            //        psi.WorkingDirectory = Path.GetDirectoryName(exe)!;
            //        if (!string.IsNullOrWhiteSpace(app.Args)) psi.Arguments = app.Args!;
            //    }

            //    if (!LooksLikeUri(exe) && !File.Exists(psi.FileName))
            //    {
            //        File.AppendAllText(logPath, $"[SKIP] Not found:  {exe}\n");
            //        continue;
            //    }
            //    File.AppendAllText(logPath, $"Launching '{app.Name}' on desktop idx={index}: {exe} {app.Args}\n");
            //    Process.Start(psi);

            //    if(cfg.LaunchDelayMs > 0) Thread.Sleep(cfg.LaunchDelayMs);
            //}

            Launcher(cfg, logPath, 500);

        }
        catch (Exception ex)
        {
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR: {ex}{Environment.NewLine}");
            Console.Error.WriteLine(ex);
            Environment.ExitCode = 1;
        }
        finally
        {
            if (mtx != null)
            {
                try
                {
                    mtx.ReleaseMutex();
                }
                catch (ObjectDisposedException)
                {
                    //ignore
                }
                catch (ApplicationException)
                {
                    //ignore   
                }
                finally
                {
                    try
                    {
                        mtx.Dispose();
                    }
                    catch
                    {
                        //ignore
                    }
                }
            }
        }
    }

    //-------Self Install-----------//

    /// <summary>
    /// Gets the installation directory path for the application.
    /// </summary>
    private static string InstallDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BootWorkspace");

    /// <summary>
    /// Gets the file path of the startup shortcut for the application.
    /// </summary>
    private static string StartupShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Boot Workspace.lnk");

    /// <summary>
    /// does a self-install to the InstallDir, copies a config file if provided (or next to EXE), and creates a Startup shortcut.
    /// </summary>
    /// <param name="configPath"></param>
    /// <exception cref="InvalidOperationException"></exception>
    private static void InstallSelf(string? configPath)
    {
        Directory.CreateDirectory(InstallDir);

        var sourceExe = Process.GetCurrentProcess().MainModule?.FileName ??
                        throw new InvalidOperationException("Cannot locate running EXE.");
        var sourceDir = Path.GetDirectoryName(sourceExe)!;
        var destExe = Path.Combine(InstallDir, Path.GetFileName(sourceExe));

        //copy all files from current dir to install dir (overwrite)
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var name = Path.GetFileName(file);
            var dest = Path.Combine(InstallDir, name);
            File.Copy(file, dest, overwrite: true);
        }

        //Pick a config to install
        //1) explicit path argument, or 2)workspace.json next to the current EXE, else 3) create a minimal default
        string destConfig = Path.Combine(InstallDir, "workspace.json");
        string? cfgSrc = null;

        if (!string.IsNullOrWhiteSpace(configPath) && File.Exists(configPath))
            cfgSrc = configPath;
        else
        {
            var localCfg = Path.Combine(sourceDir, "workspace.json");
            if (File.Exists(localCfg)) cfgSrc = localCfg;
        }

        if (cfgSrc != null) File.Copy(cfgSrc, destConfig, overwrite: true);
        else
        {
            //create a minimum default json 
            var defaultJson =
                @"{
  ""desktops"": [
    { ""index"": 0, ""name"": ""Thing 1"" },
    { ""index"": 1, ""name"": ""Thing 2"" }
  ],
  ""apps"": [
    { ""name"": ""Outlook"", ""path"": ""C:\\\\Program Files\\\\Microsoft Office\\\\root\\\\Office16\\\\OUTLOOK.EXE"", ""desktop"": ""Thing 1"" }
  ],
  ""launchDelayMs"": 1500
}";
            File.WriteAllText(destConfig, defaultJson);
        }

        //create/replace Startup shortcut point to the installed EXE (no args; JSON sits beside EXE
        CreateShortcut(StartupShortcutPath, destExe, "", InstallDir, "Boot Workspace");

        Console.WriteLine($"Installed to:  {InstallDir}");
        Console.WriteLine($"Startup shortcut:  {StartupShortcutPath}");
    }
    // Create a .lnk via WSH (no extra references needed)
    private static void CreateShortcut(string shortcutPath, string targetPath, string arguments, string workingDir, string description)
    {
        var wshType = Type.GetTypeFromProgID("WScript.Shell")
                      ?? throw new InvalidOperationException("WScript.Shell COM not available.");
        dynamic wsh = Activator.CreateInstance(wshType)!;
        dynamic shortcut = wsh.CreateShortcut(shortcutPath);
        shortcut.TargetPath = targetPath;
        shortcut.Arguments = arguments;
        shortcut.WorkingDirectory = workingDir;
        shortcut.WindowStyle = 1; // normal
        shortcut.Description = description;
        // shortcut.IconLocation = targetPath + ",0";
        shortcut.Save();
    }

    /// <summary>
    /// Performs uninstallation by removing the startup shortcut and optionally deleting the installation directory.
    /// </summary>
    private static void UninstallSelf()
    {
        // Remove shortcut
        if (File.Exists(StartupShortcutPath))
        {
            try { File.Delete(StartupShortcutPath); } catch { /* ignore */ }
        }

        // Optionally remove install dir (leave config if you prefer)
        if (Directory.Exists(InstallDir))
        {
            try { Directory.Delete(InstallDir, recursive: true); } catch { /* ignore */ }
        }

        Console.WriteLine("Uninstalled autostart and removed install folder (if possible).");
    }

    /// <summary>
    /// Verifies that the required number of virtual desktops exist, creates them if needed, and names them as per config.
    /// </summary>
    /// <param name="cfg">config object</param>
    private static void EnsureDesktopsFor(WorkspaceConfig cfg)
    {
        //how many desktops do we need?
        int required = 1; //at least one, duh

        //highest desktop index mentioned in config
        if (cfg.Desktops?.Count > 0)
        {
            var maxCfg = cfg.Desktops.Max(d => d.Index);
            if (maxCfg + 1 > required) required = maxCfg + 1;
        }

        //apps that specify a numeric desktop string (e.g., "0", "1", "2", etc)
        var numericTargets = cfg.Apps
            .Select(a => int.TryParse(a.Desktop?.Trim(), out var n) ? n : -1)
            .Where(n => n >= 0);

        if (numericTargets.Any()) required = Math.Max(required, numericTargets.Max() + 1);

        //ensure count
        var desks = VirtualDesktop.GetDesktops().ToList();
        while (desks.Count < required)
        {
            VirtualDesktop.Create();
            desks = VirtualDesktop.GetDesktops().ToList();
        }

        //name the desktops, if needed
        if (cfg.Desktops != null)
        {
            foreach (var d in cfg.Desktops)
            {
                if (d.Index >= 0 && d.Index < desks.Count && !string.IsNullOrWhiteSpace(d.Name))
                {
                    try
                    {
                        if (!string.Equals(desks[d.Index].Name, d.Name, StringComparison.Ordinal))
                            desks[d.Index].Name = d.Name;
                    }
                    catch
                    {
                        //Name may not be supported on this OS version, so just ignore any errors
                    }
                }
            }
        }
    }

    /// <summary>
    /// Parses a desktop identifier string from config and returns the corresponding zero-based desktop index.
    /// </summary>
    /// <param name="value"></param>
    /// <param name="cfg"></param>
    /// <returns></returns>
    private static int ParseDesktop(string value, WorkspaceConfig cfg)
    {
        if (string.IsNullOrEmpty(value)) return 0;
        var v = value.Trim();

        //is it a number?
        if (int.TryParse(v, out var n)) return n;
        //"Desktop 1" (1-based)
        if (v.StartsWith("desktop ", StringComparison.OrdinalIgnoreCase))
        {
            var tail = v.Substring("desktop ".Length).Trim();
            if(int.TryParse(tail, out var oneBased) && oneBased > 0) 
                return oneBased - 1;
        }

        //looke for a configured desktop name (exact, case-insensitive match)
        if (cfg.Desktops != null && cfg.Desktops.Count > 0)
        {
            var match = cfg.Desktops.FirstOrDefault( d=>
                d.Name.Equals(v, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match.Index;
        }

        //look for current system desktop names
        var all = VirtualDesktop.GetDesktops();
        for (int i = 0; i < all.Length; i++)
        {
            try
            {
                if (all[i].Name.Equals(v, StringComparison.OrdinalIgnoreCase)) return i;
            }
            catch
            {
                /*name not available; ignore*/
            }
        }

        //Friendly fallback
        var low = v.ToLowerInvariant();
        if (low.Contains("thing 1")) return 0;
        if (low.Contains("thing 2")) return 1;

        //Default
        return 0;
    }

    /// <summary>
    /// Loads and parses the workspace configuration from a JSON file.
    /// </summary>
    /// <param name="configFile"></param>
    /// <returns></returns>
    private static WorkspaceConfig LoadConfig(string configFile)
    {
        try
        {
            var json = File.ReadAllText(configFile);
            var cfg = JsonSerializer.Deserialize<WorkspaceConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
            })?? throw new InvalidOperationException("Invalid config (null)");
            if (cfg is null) throw new InvalidOperationException("Invalid config (null).");
            if (cfg.Apps is null || cfg.Apps.Count == 0)
                throw new InvalidOperationException("Config has no 'apps' entries.");
            else return cfg;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error loading config file '{configFile}': {ex}");
            return new WorkspaceConfig();
        }
    }

    /// <summary>
    /// Switches to the virtual desktop at the specified zero-based index.
    /// </summary>
    /// <param name="index"></param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    private static void SwitchToDesktopIndex(int index)
    {
        var desktops = VirtualDesktop.GetDesktops();
        if (index < 0 || index >= desktops.Length) throw new ArgumentOutOfRangeException(nameof(index));
        desktops[index].Switch();
    }

    private static int GetCurrentIndex()
    {
        var all = VirtualDesktop.GetDesktops();
        var current = VirtualDesktop.Current;
        for (int i = 0; i < all.Length; i++)
            if (all[i].Id == current.Id)
                return i;
        return -1;
    }
    /// <summary>
    /// launches apps as per config, switching to the appropriate desktop for each app.  Makes sure to stay on current desktop while all apps are launched before switching away.
    /// </summary>
    /// <param name="cfg"></param>
    /// <param name="logPath"></param>
    /// <param name="settleDelayMS"></param>
    /// <param name="ct"></param>
    private static void Launcher(WorkspaceConfig cfg, string logPath, int settleDelayMS = 400, CancellationToken ct = default)
    {
        var currentIdx = GetCurrentIndex();
        var desktopOrder = BuildDesktopOrder(cfg, currentIdx);

        foreach (var dIdx in desktopOrder)
        {
            ct.ThrowIfCancellationRequested();

            // Activate the desktop once, then do all of its apps
            SwitchToDesktopIndex(dIdx);
            if(settleDelayMS > 0) Sleep(settleDelayMS); //let the desktop switch settle

            var appsForDesktop = cfg.Apps
                .Where(a => ParseDesktop(a.Desktop, cfg) == dIdx)
                //optonal:  slow/critical apps (like Outlook) first
                .OrderByDescending(a => a.WaitForWindow)
                .ThenBy(a => a.Name)
                .ToList();

            foreach (var app in appsForDesktop)
            {
                ct.ThrowIfCancellationRequested();

                var exe = ResolveExe(app.Path);
                if (string.IsNullOrWhiteSpace(exe))
                {
                    Console.Error.WriteLine($"Skipping app '{app.Name}':  no path specified");
                    continue;
                }

                var psi = new ProcessStartInfo { UseShellExecute = true };

                if (LooksLikeUri(exe))
                {
                    //e.g., edge:, shell:AppsFolder\...
                    psi.FileName = exe;
                }
                else
                {
                    psi.FileName = exe;
                    var wd = Path.GetDirectoryName(exe);
                    if (!string.IsNullOrEmpty(wd)) psi.WorkingDirectory = wd;
                    if(!string.IsNullOrWhiteSpace(app.Args)) psi.Arguments = app.Args!;
                }

                if (!LooksLikeUri(exe) && !File.Exists(psi.FileName))
                {
                    File.AppendAllText(logPath, $"[SKIP] Not found:  {exe}{Environment.NewLine}");
                    continue;
                }

                File.AppendAllText(logPath, $"Launching '{app.Name}' on desktop index={dIdx}:  {exe} {app.Args}{Environment.NewLine}");

                var process = Process.Start(psi);

                //optionally wait for a window to appear (e.g., for Outlook to finish starting)
                if (app.WaitForWindow)
                    WaitForWindow(process, app, logPath,
                        timeoutMS: app.LaunchTimeoutMs > 0 ? app.LaunchTimeoutMs : 45000);
                if (cfg.LaunchDelayMs > 0) Sleep(cfg.LaunchDelayMs);

            }
        }
    }

    private static IEnumerable<int> BuildDesktopOrder(WorkspaceConfig cfg, int currentIdx)
    {
        var allTargets = cfg.Apps
            .Select(a => ParseDesktop(a.Desktop, cfg))
            .Distinct()
            .ToList();

        if (currentIdx >= 0 && allTargets.Contains(currentIdx))
        {
            yield return currentIdx;
            foreach (var i in allTargets.Where(i => i != currentIdx))
                yield return i;
        }
        else
            foreach (var i in allTargets)
                yield return i;
    }
    // helpers//

    /// <summary>
    /// Looks like a URI scheme or shell: link?
    /// </summary>
    /// <param name="s"></param>
    /// <returns></returns>
    private static bool LooksLikeUri(string s) => s.Contains("://") || s.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves environment variables and trims quotes from an executable path.
    /// </summary>
    /// <param name="rawPath"></param>
    /// <returns></returns>
    private static string ResolveExe(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath)) return "";
        var expanded = Environment.ExpandEnvironmentVariables(rawPath.Trim().Trim('"'));
        return expanded;
    }

    /// <summary>
    /// Replaces the placeholder "%USERNAME%" in the specified string with the current user's username.
    /// </summary>
    /// <param name="p">The input string that may contain the "%USERNAME%" placeholder.</param>
    /// <returns>A new string with all occurrences of "%USERNAME%" replaced by the username of the currently logged-in user. If
    /// the input string does not contain the placeholder, the original string is returned unchanged.</returns>
    private static string ExpandUser(string p) => p.Replace("%USERNAME%", Environment.UserName);

    /// <summary>
    /// sleeps for the specified number of milliseconds.
    /// </summary>
    /// <param name="ms"></param>
    private static void Sleep(int ms) => Thread.Sleep(ms);


    /// <summary>
    /// Waits for the Windows Explorer process to be running and the taskbar window to be available, indicating that the desktop environment is ready.
    /// </summary>
    /// <param name="timeoutMs"></param>
    private static void WaitForExplorerReady(int timeoutMs = 30000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var explorers = Process.GetProcessesByName("explorer");
            if(explorers.Length > 0 && FindWindow("Shell_TrayWnd", null) != IntPtr.Zero)
                return;
            Thread.Sleep(500);
        }
    }

    /// <summary>
    /// Finds a window by its class name and window name using the Windows API.
    /// </summary>
    /// <param name="lpClassName"></param>
    /// <param name="lpWindowName"></param>
    /// <returns></returns>
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    /// <summary>
    /// Acquires a named mutex to ensure that only a single instance of the application is running per user session.
    /// </summary>
    /// <param name="mtx"></param>
    /// <returns></returns>
    private static bool AcquireSingleInstanceMutex(out Mutex? mtx)
    {
        mtx = null;
        try
        {
            // Local\ is per-session; Global\ would span sessions. We want per-user-session.
            mtx = new Mutex(initiallyOwned: true, @"Local\BootWorkspace_SingleInstance", out bool created);
            if (!created)
            {
                mtx.Dispose();
                mtx = null;
                return false;
            }
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Determines if the application should exit due to a recent run within the specified number of seconds, aka debouncing.
    /// </summary>
    /// <param name="dir"></param>
    /// <param name="seconds"></param>
    /// <returns></returns>
    private static bool ShouldExitDueToRecentRun(string dir, int seconds = 300)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var stamp = Path.Combine(dir, "last_run.txt");
            var now = DateTime.UtcNow;

            if (File.Exists(stamp) &&
                DateTime.TryParse(File.ReadAllText(stamp), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var prev) &&
                (now - prev).TotalSeconds < seconds)
            {
                return true;
            }

            File.WriteAllText(stamp, now.ToString("o", CultureInfo.InvariantCulture));
        }
        catch { /* ignore */ }
        return false;
    }

    /// <summary>
    /// Starts the specified target application or URI with optional arguments, logging the action to the specified log file.
    /// For possible future use, not currently called.
    /// </summary>
    /// <param name="target"></param>
    /// <param name="args"></param>
    /// <param name="logPath"></param>
    /// <returns></returns>
    private static bool StartTarget(string target, string? args, string logPath)
    {
        var exe = ResolveExe(target);
        var isUri = LooksLikeUri(exe);

        if (!isUri && !File.Exists(exe))
        {
            File.AppendAllText(logPath, $"[SKIP] Not found: {exe}\n");
            return false;
        }

        var psi = new ProcessStartInfo { UseShellExecute = true, FileName = exe };
        if (!isUri)
        {
            psi.WorkingDirectory = Path.GetDirectoryName(exe)!;
            if (!string.IsNullOrWhiteSpace(args)) psi.Arguments = args!;
        }

        Process.Start(psi);
        return true;
    }

    private static void WaitForWindow(Process? p, AppCfg app, string logPath, int timeoutMS = 45000)
    {
        if (p == null) return;
        try
        {
            p.WaitForInputIdle(Math.Min(8000, timeoutMS));
        }
        catch
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMS)
            {
                p.Refresh();
                if (p.MainWindowHandle != IntPtr.Zero)
                    return;
                Thread.Sleep(150);
            }


            File.AppendAllText(logPath, $"[WARN] {app.Name} window not detected within {timeoutMS}ms{Environment.NewLine}");
        }
    }


}


