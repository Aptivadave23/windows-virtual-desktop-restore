using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System;
using System.Threading;
using System.Threading.Tasks;

namespace StartUp.Services.Abstractions;

public interface IClock
{
    DateTime Now { get; }
    Task Delay(TimeSpan delay, CancellationToken cancellationToken = default);
}
