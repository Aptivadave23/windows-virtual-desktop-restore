using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using StartUp.Services;
using StartUp.Services.Abstractions;
using Startup;

namespace StartUp.Services.Impl;

internal sealed class ProcessLauncher : IProcessLauncher
{
    private readonly IFileSystem _fs;
    private readonly ILog _log;

    public ProcessLauncher(IFileSystem fs, ILog log)
    {
        _fs = fs;
        _log = log;
    }

    public Task<LaunchResult> LaunchAsync(AppCfg app, int desktopIndex, CancellationToken cancellationToken = default)
    {
        try
        {
            var exePath = Environment.ExpandEnvironmentVariables(app.Path);
            if (!_fs.FileExists(exePath))
            {
                return Task.FromResult(LaunchResult.Failure($"Path not found: {exePath}"));
            }

            var psi = new ProcessStartInfo(exePath)
            {
                Arguments = app.Args ?? string.Empty,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory
            };

            _log.Info("Launching app", new { app = app.Name, exePath, desktop = desktopIndex });
            var process = Process.Start(psi);
            if (process == null)
            {
                return Task.FromResult(LaunchResult.Failure("Process failed to start", null));
            }

            return Task.FromResult(LaunchResult.Ok());
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Launch failed", new { app = app.Name, desktop = desktopIndex });
            return Task.FromResult(LaunchResult.Failure("Exception while launching app", ex: ex));
        }
    }
}
