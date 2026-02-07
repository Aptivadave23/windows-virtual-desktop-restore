using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StartUp.Services.Abstractions;
using StartUp;
using Startup;

namespace StartUp.Services;

public sealed class Runtime
{
    private readonly IConfigLoader _configLoader;
    private readonly IDesktopApi _desktopApi;
    private readonly IProcessLauncher _processLauncher;
    private readonly IFileSystem _fs;
    private readonly IClock _clock;
    private readonly ILog _log;

    public Runtime(IConfigLoader configLoader,
                   IDesktopApi desktopApi,
                   IProcessLauncher processLauncher,
                   IFileSystem fs,
                   IClock clock,
                   ILog log)
    {
        _configLoader = configLoader;
        _desktopApi = desktopApi;
        _processLauncher = processLauncher;
        _fs = fs;
        _clock = clock;
        _log = log;
    }

    public void Run(string configPath) => RunAsync(configPath).GetAwaiter().GetResult();

    private async Task RunAsync(string configPath)
    {
        var cfg = _configLoader.Load(configPath);
        EnsureDesktops(cfg);

        var currentIdx = SafeCurrentDesktopIndex();
        var order = BuildDesktopOrder(cfg.Apps, cfg, currentIdx).ToList();

        foreach (var desktopIndex in order)
        {
            var appsForDesktop = cfg.Apps.Where(a => ResolveDesktopIndex(a, cfg) == desktopIndex).ToList();
            if (appsForDesktop.Count == 0) continue;

            await _desktopApi.SwitchTo(desktopIndex);

            foreach (var app in appsForDesktop)
            {
                var result = await _processLauncher.LaunchAsync(app, desktopIndex);
                if (!result.Success)
                    _log.Warn("Launch failed", new { app = app.Name, desktopIndex, result.Message });

                if (cfg.LaunchDelayMs > 0)
                    await _clock.Delay(TimeSpan.FromMilliseconds(cfg.LaunchDelayMs));
            }
        }

        _log.Info("Run complete", new { desktopsVisited = order.Count, appsLaunched = cfg.Apps.Count });
    }

    private void EnsureDesktops(WorkspaceConfig cfg)
    {
        var required = 1;

        if (cfg.Desktops?.Count > 0)
        {
            var maxCfg = cfg.Desktops.Max(d => d.Index);
            if (maxCfg + 1 > required) required = maxCfg + 1;
        }

        var numericTargets = cfg.Apps
            .Select(a => int.TryParse(a.Desktop?.Trim(), out var n) ? n : -1)
            .Where(n => n >= 0);

        if (numericTargets.Any()) required = Math.Max(required, numericTargets.Max() + 1);

        _desktopApi.EnsureCount(required).GetAwaiter().GetResult();

        if (cfg.Desktops != null)
        {
            foreach (var d in cfg.Desktops)
            {
                if (!string.IsNullOrWhiteSpace(d.Name))
                    _desktopApi.EnsureNamed(d.Name, d.Index).GetAwaiter().GetResult();
            }
        }
    }

    private static IEnumerable<int> BuildDesktopOrder(IEnumerable<AppCfg> apps, WorkspaceConfig cfg, int currentIdx)
    {
        var targets = apps
            .Select(a => ResolveDesktopIndex(a, cfg))
            .Distinct()
            .ToList();

        if (targets.Contains(currentIdx))
        {
            yield return currentIdx;
            foreach (var t in targets.Where(t => t != currentIdx))
                yield return t;
        }
        else
        {
            foreach (var t in targets)
                yield return t;
        }
    }

    private static int ResolveDesktopIndex(AppCfg app, WorkspaceConfig cfg)
    {
        if (string.IsNullOrWhiteSpace(app.Desktop)) return 0;
        var v = app.Desktop.Trim();

        if (int.TryParse(v, out var n)) return n;

        if (cfg.Desktops != null && cfg.Desktops.Count > 0)
        {
            var match = cfg.Desktops.FirstOrDefault(d => d.Name.Equals(v, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match.Index;
        }

        return 0;
    }

    private int SafeCurrentDesktopIndex()
    {
        try { return _desktopApi.CurrentIndex(); }
        catch { return 0; }
    }
}
