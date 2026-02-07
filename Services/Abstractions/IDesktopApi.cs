using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Threading.Tasks;

namespace StartUp.Services.Abstractions;

public interface IDesktopApi
{
    int Count { get; }
    int CurrentIndex();
    Task EnsureCount(int desired);
    Task<int> EnsureNamed(string name, int? index = null);
    Task SwitchTo(int index);
}
