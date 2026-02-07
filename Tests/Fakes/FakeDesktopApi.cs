using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StartUp.Services.Abstractions;

namespace StartUp.Tests.Fakes
{
    internal sealed class FakeDesktopApi : IDesktopApi
    {
        private readonly List<string> _names = new() { "Desktop 1" };
        public int Current { get; set; }
        public List<int> SwitchCalls { get; } = new();

        public int Count => _names.Count;

        public Task<int> EnsureNamed(string name, int? index = null)
        {
            if (index.HasValue)
            {
                while (_names.Count <= index.Value) _names.Add($"Desktop {_names.Count + 1}");
                _names[index.Value] = name;
                return Task.FromResult(index.Value);
            }

            var existing = _names.FindIndex(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0) return Task.FromResult(existing);

            _names.Add(name);
            return Task.FromResult(_names.Count - 1);
        }

        public Task EnsureCount(int desired)
        {
            while (_names.Count < desired) _names.Add($"Desktop {_names.Count + 1}");
            return Task.CompletedTask;
        }

        public int CurrentIndex() => Current;

        public Task SwitchTo(int index)
        {
            Current = index;
            SwitchCalls.Add(index);
            return Task.CompletedTask;
        }
    }
}
