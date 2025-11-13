// SPDX-License-Identifier: Unlicense

using Figgle;
using Figgle.Fonts;
using Spectre.Console;
using Startup;
using StartUp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using WindowsDesktop;
using ArgumentOutOfRangeException = System.ArgumentOutOfRangeException;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var logPath = Path.Combine(InstallDir, "bootworkspace.log");

        // single instance check
        if (!AcquireSingleInstanceMutex(out var mtx))
        {
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Another instance is already running. Exiting.{Environment.NewLine}");
            return;
        }

        // Display banner
        DisplayBanner();

        try
        {
            // Debounce quick repeat runs
            if (ShouldExitDueToRecentRun(InstallDir, seconds: 3))
            {
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Recent run detected. Exiting.{Environment.NewLine}");
                return;
            }

            Directory.CreateDirectory(InstallDir);
            File.AppendAllText(logPath, $"---- BootWorkspace run at {DateTime.Now} ----{Environment.NewLine}");

            // small cushion at logon
            Thread.Sleep(5000);

            // wait for explorer
            WaitForExplorerReady();
            File.AppendAllText(logPath, $"Explorer is running. Current desktop index: {GetCurrentIndex()}{Environment.NewLine}");

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

            // load config
            var configFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "workspace.json");
            File.AppendAllText(logPath, $"BaseDir={AppDomain.CurrentDomain.BaseDirectory}\nConfig={configFile}\n");

            WorkspaceConfig cfg;
            if (File.Exists(configFile)) cfg = LoadConfig(configFile);
            else throw new FileNotFoundException($"Config file '{configFile}' not found");

            File.AppendAllText(logPath, $"Apps={cfg.Apps?.Count ?? 0}\n");

            // ensure desktops
            File.AppendAllText(logPath, $"Desktops(before)={WindowsDesktop.VirtualDesktop.GetDesktops().Length}\n");
            EnsureDesktopsFor(cfg);
            File.AppendAllText(logPath, $"Desktops(after)={WindowsDesktop.VirtualDesktop.GetDesktops().Length}\n");

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
                try { mtx.ReleaseMutex(); }
                catch (ObjectDisposedException) { }
                catch (ApplicationException) { }
                finally
                {
                    try { mtx.Dispose(); } catch { }
                }
            }
        }
    }

    //------- Display ---------//
    private static void DisplayBanner()
    {
        string asciiArt = FiggleFonts.Slant.Render("Windows Startup Utility");

        AnsiConsole.Write(
            new Panel(asciiArt)
                .Border(BoxBorder.Double)
                .BorderStyle(new Style(Color.Green))
                .Padding(1, 1)
                .Expand()
        );

        AnsiConsole.MarkupLine("[bold green]Halito! Thanks for using Restore Virtual Desktop Workspaces for Windows 11![/]");
        AnsiConsole.MarkupLine("[bold green]Developed by Aptiva Dave[/]");
        AnsiConsole.MarkupLine("[bold green]For updates and instructions, visit 🚀 [link=https://github.com/Aptivadave23/windows-virtual-desktop-restore][u]Project Repository[/][/][/]");
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
        AnsiConsole.MarkupLine($"[bold yellow]Currently running v{version}[/]");
    }

    //------- Self Install ---------//
    private static string InstallDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BootWorkspace");

    private static string StartupShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Boot Workspace.lnk");

    private static void InstallSelf(string? configPath)
    {
        Directory.CreateDirectory(InstallDir);

        var sourceExe = Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Cannot locate running EXE.");
        var sourceDir = Path.GetDirectoryName(sourceExe)!;
        var destExe = Path.Combine(InstallDir, Path.GetFileName(sourceExe));

        var files = Directory.GetFiles(sourceDir);
        long totalBytes = files.Sum(f => new FileInfo(f).Length);

        AnsiConsole.Progress()
            .Columns(new TaskDescriptionColumn(),
                     new ProgressBarColumn(),
                     new PercentageColumn(),
                     new RemainingTimeColumn(),
                     new SpinnerColumn())
            .Start(ctx =>
            {
                var copyTask = ctx.AddTask("[green]Copying files...[/]", maxValue: totalBytes);

                foreach (var file in files)
                {
                    var name = Path.GetFileName(file);
                    var dest = Path.Combine(InstallDir, name);
                    CopyFileWithProgress(file, dest, (bytes) => copyTask.Increment(bytes));
                }

                var cfgTask = ctx.AddTask("[cyan]Installing config[/]", maxValue: 1);
                string destConfig = Path.Combine(InstallDir, "workspace.json");
                string? cfgSrc = null;

                if (!string.IsNullOrWhiteSpace(configPath) && File.Exists(configPath))
                    cfgSrc = configPath;
                else
                {
                    var localCfg = Path.Combine(sourceDir, "workspace.json");
                    if (File.Exists(localCfg)) cfgSrc = localCfg;
                }

                if (cfgSrc != null)
                {
                    File.Copy(cfgSrc, destConfig, overwrite: true);
                }
                else
                {
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
                cfgTask.Increment(1);

                var shortcutTask = ctx.AddTask("[yellow]Creating startup shortcut[/]", maxValue: 1);
                CreateShortcut(StartupShortcutPath, destExe, "", InstallDir, "Boot Workspace");
                shortcutTask.Increment(1);
            });

        AnsiConsole.MarkupLine($"[bold green]Installed to:[/]  {Markup.Escape(InstallDir)}");
        AnsiConsole.MarkupLine($"[bold green]Startup shortcut:[/]  {Markup.Escape(StartupShortcutPath)}");
        AnsiConsole.MarkupLine("[bold green]✅ Installation complete![/]");

        var configPathFinal = Path.Combine(InstallDir, "workspace.json");
        AnsiConsole.Write(
            new Panel($"Edit your workspace configuration here:\n[bold yellow]{Markup.Escape(configPathFinal)}[/]")
                .Header(":gear: [bold cyan]Next Step[/]")
                .BorderColor(Color.Green)
                .Expand());
    }

    private static void CopyFileWithProgress(string source, string destination, Action<long> reportBytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        const int BufferSize = 128 * 1024; // 128 KB
        using var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: false);
        using var dst = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: false);

        var buffer = new byte[BufferSize];
        int read;
        while ((read = src.Read(buffer, 0, buffer.Length)) > 0)
        {
            dst.Write(buffer, 0, read);
            reportBytes(read);
        }
    }

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
        shortcut.Save();
    }

    private static void UninstallSelf()
    {
        var confirmation = AnsiConsole.Prompt(new ConfirmationPrompt("[red]Are you sure want to uninstall?[/]"));
        if (!confirmation)
        {
            AnsiConsole.MarkupLine("[bold green]Oh, thank you for letting me stay![/]");
            return;
        }

        AnsiConsole.Progress()
            .Columns(new TaskDescriptionColumn(),
                     new ProgressBarColumn(),
                     new PercentageColumn(),
                     new RemainingTimeColumn(),
                     new SpinnerColumn())
            .Start(ctx =>
            {
                var shortcutTask = ctx.AddTask("[red]Removing startup shortcut...[/]", maxValue: 1);
                if (File.Exists(StartupShortcutPath))
                {
                    try { File.Delete(StartupShortcutPath); } catch { }
                }
                shortcutTask.Increment(1);

                if (Directory.Exists(InstallDir))
                {
                    var files = Directory.GetFiles(InstallDir, "*", SearchOption.AllDirectories);
                    long totalBytes = files.Sum(f => new FileInfo(f).Length);
                    var dirTask = ctx.AddTask("[red]Removing installation directory...[/]", maxValue: totalBytes);
                    foreach (var file in files)
                    {
                        try
                        {
                            var info = new FileInfo(file);
                            long fileSize = info.Length;
                            File.Delete(file);
                            dirTask.Increment(fileSize);
                        }
                        catch { }
                    }

                    // best-effort delete directory once files are gone
                    try { Directory.Delete(InstallDir, recursive: true); } catch { }
                }
            });

        AnsiConsole.MarkupLine("[bold green]✅ Uninstallation complete![/]");
    }

    //------- Desktops & Config ---------//
    private static void EnsureDesktopsFor(WorkspaceConfig cfg)
    {
        int required = 1;

        if (cfg.Desktops?.Count > 0)
        {
            var maxCfg = cfg.Desktops.Max(d => d.Index);
            if (maxCfg + 1 > required) required = maxCfg + 1;
        }

        var numericTargets = cfg.Apps
            .Select(a => int.TryParse(a.Desktop?.Trim(), out var n) ? n : -1)
            .Where(n => n >= 0);

        if (numericTargets.Any()) required = Math.Max(required, numericTargets.Max() + 1);

        var desks = VirtualDesktop.GetDesktops().ToList();
        while (desks.Count < required)
        {
            VirtualDesktop.Create();
            desks = VirtualDesktop.GetDesktops().ToList();
        }

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
                    catch { /* name may not be supported on this OS version */ }
                }
            }
        }
    }

    private static int ParseDesktop(string value, WorkspaceConfig cfg)
    {
        if (string.IsNullOrEmpty(value)) return 0;
        var v = value.Trim();

        if (int.TryParse(v, out var n)) return n;

        if (v.StartsWith("desktop ", StringComparison.OrdinalIgnoreCase))
        {
            var tail = v.Substring("desktop ".Length).Trim();
            if (int.TryParse(tail, out var oneBased) && oneBased > 0)
                return oneBased - 1;
        }

        if (cfg.Desktops != null && cfg.Desktops.Count > 0)
        {
            var match = cfg.Desktops.FirstOrDefault(d => d.Name.Equals(v, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match.Index;
        }

        var all = VirtualDesktop.GetDesktops();
        for (int i = 0; i < all.Length; i++)
        {
            try
            {
                if (all[i].Name.Equals(v, StringComparison.OrdinalIgnoreCase)) return i;
            }
            catch { }
        }

        var low = v.ToLowerInvariant();
        if (low.Contains("thing 1")) return 0;
        if (low.Contains("thing 2")) return 1;

        return 0;
    }

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
            }) ?? throw new InvalidOperationException("Invalid config (null)");
            if (cfg.Apps is null || cfg.Apps.Count == 0)
                throw new InvalidOperationException("Config has no 'apps' entries.");
            return cfg;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error loading config file '{configFile}': {ex}");
            return new WorkspaceConfig();
        }
    }

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

    //------- Progress-based Launcher (redirects child output; clean summary) ---------//

    private static void Launcher(
        WorkspaceConfig cfg,
        string logPath,
        int settleDelayMS = 400,
        CancellationToken ct = default)
    {
        Console.OutputEncoding = Encoding.UTF8;

        int currentIdx = GetCurrentIndex();
        var desktopOrder = BuildDesktopOrder(cfg, currentIdx);

        var allAppsOrdered = desktopOrder
            .SelectMany(dIdx =>
                cfg.Apps
                   .Where(a => ParseDesktop(a.Desktop, cfg) == dIdx)
                   .OrderByDescending(a => a.WaitForWindow)
                   .ThenBy(a => a.Name)
                   .Select(a => (dIdx, a)))
            .ToList();

        if (allAppsOrdered.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No apps defined in workspace.json.[/]");
            return;
        }

        string DesktopName(int idx) =>
            cfg.Desktops?.FirstOrDefault(d => d.Index == idx)?.Name ?? $"Desktop {idx}";

        // Prepare label pieces (plain text)
        var labels = allAppsOrdered
            .Select(t =>
            {
                var name = string.IsNullOrWhiteSpace(t.a.Name) ? "(unnamed)" : t.a.Name!;
                var desk = DesktopName(t.dIdx);
                return (t.dIdx, t.a, name, desk);
            })
            .ToList();

        // Track final results for the summary
        var results = labels.ToDictionary(x => x.a, _ => "Pending"); // Done | Skipped: No path | Skipped: Not found | Error

        AnsiConsole.Progress()
            .AutoClear(true)       // remove progress block when done
            .HideCompleted(true)   // hide finished rows while running
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn()
            )
            .Start(ctx =>
            {
                var overall = ctx.AddTask("[bold cyan]Overall[/]", maxValue: labels.Count);

                // one task per app
                var appTasks = labels.ToDictionary(
                    x => x.a,
                    x => ctx.AddTask($"[grey]{Markup.Escape(x.name)}[/] [dim]|[/] {Markup.Escape(x.desk)}")
                );

                int? currentDesktop = null;

                foreach (var (dIdx, app, name, desk) in labels)
                {
                    ct.ThrowIfCancellationRequested();

                    var task = appTasks[app];
                    void SetDesc(string prefix) =>
                        task.Description = $"{prefix} {Markup.Escape(name)} [dim]|[/] {Markup.Escape(desk)}";

                    try
                    {
                        if (currentDesktop != dIdx)
                        {
                            currentDesktop = dIdx;
                            SwitchToDesktopIndex(dIdx);
                            if (settleDelayMS > 0) Sleep(settleDelayMS);
                        }

                        var exe = ResolveExe(app.Path);
                        if (string.IsNullOrWhiteSpace(exe))
                        {
                            results[app] = "Skipped: No path";
                            SetDesc("⚠ [yellow]Skipped[/]");
                            task.Value = 100;
                            overall.Increment(1);
                            continue;
                        }

                        // Build ProcessStartInfo: redirect output for EXEs; use shell only for URIs
                        ProcessStartInfo psi;
                        bool isUri = LooksLikeUri(exe);
                        if (isUri)
                        {
                            psi = new ProcessStartInfo
                            {
                                UseShellExecute = true,
                                FileName = exe,
                            };
                        }
                        else
                        {
                            psi = new ProcessStartInfo
                            {
                                UseShellExecute = false,          // allows redirection, prevents console inheritance
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                CreateNoWindow = true,            // no child console flashes
                                FileName = exe,
                                Arguments = string.IsNullOrWhiteSpace(app.Args) ? "" : app.Args!,
                                WorkingDirectory = Path.GetDirectoryName(exe) ?? ""
                            };
                        }

                        if (!isUri && !File.Exists(psi.FileName))
                        {
                            results[app] = "Skipped: Not found";
                            SetDesc("⚠ [yellow]Skipped[/]");
                            File.AppendAllText(logPath, $"[SKIP] Not found: {exe}{Environment.NewLine}");
                            task.Value = 100;
                            overall.Increment(1);
                            continue;
                        }

                        // Launching…
                        SetDesc("🚀 [cyan]Launching[/]");
                        task.IsIndeterminate = true;

                        File.AppendAllText(logPath,
                            $"Launching '{app.Name}' on desktop index={dIdx}: {exe} {app.Args}{Environment.NewLine}");

                        var process = Process.Start(psi);

                        // If we redirected, consume async and write to log (avoid blocking / console spam)
                        if (!isUri && process != null)
                        {
                            process.OutputDataReceived += (_, e) =>
                            {
                                if (!string.IsNullOrEmpty(e.Data))
                                    try { File.AppendAllText(logPath, $"[{app.Name}] OUT: {e.Data}{Environment.NewLine}"); } catch { }
                            };
                            process.ErrorDataReceived += (_, e) =>
                            {
                                if (!string.IsNullOrEmpty(e.Data))
                                    try { File.AppendAllText(logPath, $"[{app.Name}] ERR: {e.Data}{Environment.NewLine}"); } catch { }
                            };
                            process.BeginOutputReadLine();
                            process.BeginErrorReadLine();

                            // Dispose when the child exits (fire-and-forget; do not block launcher)
                            _ = Task.Run(async () =>
                            {
                                try { await process.WaitForExitAsync(); } catch { }
                                try { process.Dispose(); } catch { }
                            });
                        }

                        if (app.WaitForWindow)
                        {
                            SetDesc("⏳ [yellow]Waiting[/]");
                            WaitForWindow(process, app, logPath,
                                timeoutMS: app.LaunchTimeoutMs > 0 ? app.LaunchTimeoutMs : 45_000);
                        }

                        if (cfg.LaunchDelayMs > 0)
                            Sleep(cfg.LaunchDelayMs);

                        results[app] = "Done";
                        task.IsIndeterminate = false;
                        task.Value = 100;
                        SetDesc("✅ [green]Done[/]");

                        overall.Increment(1);
                    }
                    catch (Exception ex)
                    {
                        results[app] = "Error";
                        task.IsIndeterminate = false;
                        task.Value = 100;
                        SetDesc("❌ [red]Error[/]");
                        try { File.AppendAllText(logPath, $"[ERROR] {app.Name}: {ex}{Environment.NewLine}"); } catch { }
                        overall.Increment(1);
                    }
                }
            });

        // Static summary after the progress block is cleared
        var table = new Table().Expand();
        table.AddColumn(new TableColumn("[bold]App[/]").LeftAligned());
        table.AddColumn(new TableColumn("[bold]Desktop[/]").LeftAligned());
        table.AddColumn(new TableColumn("[bold]Status[/]").Centered());
        table.AddColumn(new TableColumn("[bold]Details[/]").LeftAligned());

        foreach (var (dIdx, app, name, _) in labels)
        {
            var status = results[app] switch
            {
                "Done" => "[green]Done[/] ✅",
                var s when s.StartsWith("Skipped") => "[yellow]Skipped[/]",
                "Error" => "[red]Error[/]",
                _ => "[grey]Pending[/]"
            };

            var details = results[app] switch
            {
                "Done" => "Launched successfully",
                "Skipped: No path" => "No path specified",
                "Skipped: Not found" => "Executable not found",
                "Error" => "See log for details",
                _ => ""
            };

            table.AddRow(
                Markup.Escape(name),
                Markup.Escape(DesktopName(dIdx)),
                status,
                Markup.Escape(details)
            );
        }

        AnsiConsole.Write(
            new Panel(table)
                .Header("[bold cyan]Apps[/]")
                .Border(BoxBorder.Rounded)
                .Expand()
        );

        AnsiConsole.MarkupLine("[bold green]✅ Launch sequence complete.  Party on, Wayne!  Party on, Garth![/]");
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
        {
            foreach (var i in allTargets)
                yield return i;
        }
    }

    // Helpers //

    private static bool LooksLikeUri(string s) => s.Contains("://") || s.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);

    private static string ResolveExe(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath)) return "";
        var expanded = Environment.ExpandEnvironmentVariables(rawPath.Trim().Trim('"'));
        return expanded;
    }

    private static string ExpandUser(string p) => p.Replace("%USERNAME%", Environment.UserName);

    private static void Sleep(int ms) => Thread.Sleep(ms);

    private static void WaitForExplorerReady(int timeoutMs = 30000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var explorers = Process.GetProcessesByName("explorer");
            if (explorers.Length > 0 && FindWindow("Shell_TrayWnd", null) != IntPtr.Zero)
                return;
            Thread.Sleep(500);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    private static bool AcquireSingleInstanceMutex(out Mutex? mtx)
    {
        mtx = null;
        try
        {
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
        catch { }
        return false;
    }

    //private static bool StartTarget(string target, string? args, string logPath)
    //{
    //    var exe = ResolveExe(target);
    //    var isUri = LooksLikeUri(exe);

    //    if (!isUri && !File.Exists(exe))
    //    {
    //        File.AppendAllText(logPath, $"[SKIP] Not found: {exe}\n");
    //        return false;
    //    }

    //    var psi = new ProcessStartInfo { UseShellExecute = true, FileName = exe };
    //    if (!isUri)
    //    {
    //        psi.WorkingDirectory = Path.GetDirectoryName(exe)!;
    //        if (!string.IsNullOrWhiteSpace(args)) psi.Arguments = args!;
    //    }

    //    Process.Start(psi);
    //    return true;
    //}

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
