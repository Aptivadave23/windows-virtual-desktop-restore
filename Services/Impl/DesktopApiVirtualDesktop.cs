using System.Threading.Tasks;
using StartUp.Services.Abstractions;
using WindowsDesktop;

namespace StartUp.Services.Impl;

internal sealed class DesktopApiVirtualDesktop : IDesktopApi
{
    public int Count => VirtualDesktop.GetDesktops().Length;

    public int CurrentIndex()
    {
        var desktops = VirtualDesktop.GetDesktops();
        var current = VirtualDesktop.Current;
        for (var i = 0; i < desktops.Length; i++)
        {
            if (desktops[i].Id == current.Id)
                return i;
        }
        return 0;
    }

    public Task EnsureCount(int desired)
    {
        while (VirtualDesktop.GetDesktops().Length < desired)
        {
            VirtualDesktop.Create();
        }
        return Task.CompletedTask;
    }

    public Task<int> EnsureNamed(string name, int? index = null)
    {
        var desktops = VirtualDesktop.GetDesktops();
        if (index.HasValue && index.Value < desktops.Length)
        {
            desktops[index.Value].Name = name;
            return Task.FromResult(index.Value);
        }

        // try find existing by name
        for (var i = 0; i < desktops.Length; i++)
        {
            if (desktops[i].Name == name) return Task.FromResult(i);
        }

        // create new if needed
        var newDesktop = VirtualDesktop.Create();
        newDesktop.Name = name;
        var all = VirtualDesktop.GetDesktops();
        return Task.FromResult(all.Length - 1);
    }

    public Task SwitchTo(int index)
    {
        var desktops = VirtualDesktop.GetDesktops();
        if (index >= 0 && index < desktops.Length)
        {
            desktops[index].Switch();
        }
        return Task.CompletedTask;
    }
}
