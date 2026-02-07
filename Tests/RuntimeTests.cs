using System.Collections.Generic;
using StartUp.Services;
using StartUp.Services.Abstractions;
using StartUp.Tests.Fakes;
using Startup;
using FluentAssertions;
using Xunit;

namespace StartUp.Tests
{
    public class RuntimeTests
    {
        private static WorkspaceConfig MakeConfig(params AppCfg[] apps)
        {
            return new WorkspaceConfig
            {
                Desktops = new List<DesktopCfg>
                {
                    new DesktopCfg { Index = 0, Name = "Thing 1" },
                    new DesktopCfg { Index = 1, Name = "Thing 2" }
                },
                Apps = new List<AppCfg>(apps),
                LaunchDelayMs = 0
            };
        }

        [Fact]
        public void Run_LaunchesCurrentDesktopFirst_ThenOthers()
        {
            // Arrange
            var desktopApi = new FakeDesktopApi { Current = 1 };
            var processLauncher = new FakeProcessLauncher();
            var config = MakeConfig(
                new AppCfg { Name = "AppA", Path = "A.exe", Desktop = "0" },
                new AppCfg { Name = "AppB", Path = "B.exe", Desktop = "1" }
            );
            var runtime = CreateRuntime(config, desktopApi, processLauncher);

            // Act
            runtime.Run("unused.json");

            // Assert: switches start with current (1) then 0
            desktopApi.SwitchCalls.Should().BeEquivalentTo(new[] { 1, 0 }, opts => opts.WithStrictOrdering());
            processLauncher.Launched.Should().BeEquivalentTo(new[]
            {
                (config.Apps[1], 1),
                (config.Apps[0], 0)
            }, opts => opts.WithStrictOrdering());
            desktopApi.Count.Should().BeGreaterOrEqualTo(2);
        }

        [Fact]
        public void Run_ResolvesNamedDesktops()
        {
            var desktopApi = new FakeDesktopApi { Current = 0 };
            var processLauncher = new FakeProcessLauncher();
            var config = MakeConfig(
                new AppCfg { Name = "AppNamed", Path = "C.exe", Desktop = "Thing 2" }
            );
            var runtime = CreateRuntime(config, desktopApi, processLauncher);

            runtime.Run("unused.json");

            desktopApi.SwitchCalls.Should().NotBeEmpty().And.Contain(1);
            processLauncher.Launched.Should().ContainSingle().Which.desktopIndex.Should().Be(1);
        }

        private static Runtime CreateRuntime(WorkspaceConfig cfg, FakeDesktopApi desktopApi, FakeProcessLauncher processLauncher)
        {
            var loader = new FakeConfigLoader(cfg);
            var fs = new NullFileSystem();
            var clock = new FakeClock();
            var log = new NullLog();
            return new Runtime(loader, desktopApi, processLauncher, fs, clock, log);
        }

        private sealed class FakeConfigLoader : IConfigLoader
        {
            private readonly WorkspaceConfig _cfg;
            public FakeConfigLoader(WorkspaceConfig cfg) => _cfg = cfg;
            public WorkspaceConfig Load(string path) => _cfg;
        }

        private sealed class NullFileSystem : IFileSystem
        {
            public bool DirectoryExists(string path) => false;
            public bool FileExists(string path) => false;
            public string ReadAllText(string path) => string.Empty;
            public void WriteAllText(string path, string content, bool append = false) { }
            public void Copy(string source, string dest, bool overwrite = false) { }
            public void CreateDirectory(string path) { }
        }

        private sealed class NullLog : ILog
        {
            public void Error(System.Exception ex, string message, object? context = null) { }
            public void Info(string message, object? context = null) { }
            public void Warn(string message, object? context = null) { }
        }
    }
}
