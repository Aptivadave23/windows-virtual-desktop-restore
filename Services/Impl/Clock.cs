using System;
using System.Threading;
using System.Threading.Tasks;
using StartUp.Services.Abstractions;

namespace StartUp.Services.Impl;

internal sealed class Clock : IClock
{
    public DateTime Now => DateTime.Now;

    public Task Delay(TimeSpan delay, CancellationToken cancellationToken = default) =>
        Task.Delay(delay, cancellationToken);
}
