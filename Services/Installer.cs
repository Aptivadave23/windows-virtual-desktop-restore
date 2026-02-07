using Spectre.Console;
using System.Diagnostics;
using System.Linq;
using System.IO;
using StartUp.Services.Abstractions;

namespace StartUp.Services;

public class Installer
{
    private readonly IFileSystem _fs;
    private readonly ILog _log;
    private readonly string _installDir;
    private readonly string _startupShortcutPath;

    private const string DefaultConfigJson =
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

    public Installer(IFileSystem fs, ILog log, string? installDir = null, string? startupShortcutPath = null)
    {
        _fs = fs;
        _log = log;
        _installDir = installDir ?? DefaultInstallDir;
        _startupShortcutPath = startupShortcutPath ?? DefaultStartupShortcutPath;
    }

    private static string DefaultInstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BootWorkspace");

    private static string DefaultStartupShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Boot Workspace.lnk");

    public void Install(string? configPath)
    {
        _fs.CreateDirectory(_installDir);

        var sourceExe = Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Cannot locate running EXE.");
        var sourceDir = Path.GetDirectoryName(sourceExe)!;
        var destExe = Path.Combine(_installDir, Path.GetFileName(sourceExe));

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var dest = Path.Combine(_installDir, Path.GetFileName(file));
            CopyFileWithProgress(file, dest, _ => _log.Info($"Installing {Path.GetFileName(file)}"));
        }

        var destConfigPath = Path.Combine(_installDir, "workspace.json");
        var cfgSrc = !string.IsNullOrWhiteSpace(configPath) && _fs.FileExists(configPath)
            ? configPath
            : Path.Combine(sourceDir, "workspace.json");

        if (_fs.FileExists(cfgSrc))
            _fs.Copy(cfgSrc, destConfigPath, overwrite: true);
        else
            _fs.WriteAllText(destConfigPath, DefaultConfigJson);

        CreateShortcut(_startupShortcutPath, destExe, "", _installDir, "Boot Workspace");
        _log.Info("Install complete", new { _installDir, _startupShortcutPath });
    }

    public void Uninstall()
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
                if (File.Exists(_startupShortcutPath))
                {
                    try { File.Delete(_startupShortcutPath); } catch { }
                }
                shortcutTask.Increment(1);

                if (Directory.Exists(_installDir))
                {
                    var files = Directory.GetFiles(_installDir, "*", SearchOption.AllDirectories);
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

                    try { Directory.Delete(_installDir, recursive: true); } catch { }
                }
            });

        AnsiConsole.MarkupLine("[bold green]✅ Uninstallation complete![/]");
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
}
