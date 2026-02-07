using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StartUp.Services;
using StartUp.Services.Abstractions;
using Startup;

namespace StartUp.Tests.Fakes
{
    internal sealed class FakeProcessLauncher : IProcessLauncher
    {
        public readonly List<(AppCfg app, int desktopIndex)> Launched = new();

        public Task<LaunchResult> LaunchAsync(AppCfg app, int desktopIndex, CancellationToken cancellationToken = default)
        {
            Launched.Add((app, desktopIndex));
            return Task.FromResult(LaunchResult.Ok());
        }
    }
}
