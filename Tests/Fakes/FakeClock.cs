using System;
using System.Threading;
using System.Threading.Tasks;
using StartUp.Services.Abstractions;

namespace StartUp.Tests.Fakes
{
    internal sealed class FakeClock : IClock
    {
        public DateTime Now { get; set; } = DateTime.UtcNow;

        public Task Delay(TimeSpan delay, CancellationToken cancellationToken = default)
        {
            // In tests, skip waiting; allow override if someone wants to simulate time.
            return Task.CompletedTask;
        }
    }
}
