using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Threading;
using System.Threading.Tasks;
using Startup;
using StartUp.Services;

namespace StartUp.Services.Abstractions;

public interface IProcessLauncher
{
    Task<LaunchResult> LaunchAsync(AppCfg app, int desktopIndex, CancellationToken cancellationToken = default);
}
