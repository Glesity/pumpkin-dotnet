using System;
using System.Collections.Generic;
using System.IO;
using Pumpkin.DotNet.Api.Logging;
using Pumpkin.DotNet.Runtime.Configuration;
using Pumpkin.DotNet.Runtime.Context;
using Xunit;

namespace Pumpkin.DotNet.Tests;

public sealed class PluginConfigurationRuntimeTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ConfigBinderRegistry _binders;
    private readonly PumpkinPluginContext _context;

    public sealed class TestConfig
    {
        public string Greeting { get; set; } = "Default";
        public int MaxWarps { get; set; } = 3;
    }

    public PluginConfigurationRuntimeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "pumpkin_cfg_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _binders = new ConfigBinderRegistry();
        _binders.Register<TestConfig>(
            bind: values =>
            {
                var cfg = new TestConfig();
                if (values.TryGetValue("greeting", out var g) && g is not null)
                    cfg.Greeting = g;
                if (values.TryGetValue("max_warps", out var w) && int.TryParse(w, out var parsed))
                    cfg.MaxWarps = parsed;
                return cfg;
            },
            serialize: cfg => new Dictionary<string, string?>
            {
                ["greeting"] = cfg.Greeting,
                ["max_warps"] = cfg.MaxWarps.ToString()
            });

        _context = new PumpkinPluginContext(
            Logger: new NoOpLogger(),
            DataDirectory: _tempDir,
            PluginAssembly: typeof(PluginConfigurationRuntimeTests).Assembly,
            Binders: _binders);
    }

    [Fact]
    public void SaveConfig_And_GetConfig_Work_Through_ConfigBinderRegistry()
    {
        var input = new TestConfig { Greeting = "Привет, Pumpkin!", MaxWarps = 15 };
        _context.SaveConfig(input, "config.toml");

        var loaded = _context.GetConfig<TestConfig>("config.toml");

        Assert.Equal("Привет, Pumpkin!", loaded.Greeting);
        Assert.Equal(15, loaded.MaxWarps);
    }

    [Fact]
    public void GetConfig_Throws_When_Binder_Not_Registered()
    {
        Assert.Throws<InvalidOperationException>(() => _context.GetConfig<UnregisteredConfig>());
    }

    [Fact]
    public void Should_Generate_BindConfigs_And_Auto_FsPermissions()
    {
        const string code = """
            using Pumpkin.DotNet.Api.Plugins;

            namespace MyPlugin;

            public sealed class MyConfig
            {
                public string Title { get; set; } = "Default";
                public int Limit { get; set; } = 10;
            }

            public sealed class SamplePlugin : PumpkinPlugin
            {
                public override void OnLoad()
                {
                    SaveDefaultConfig();
                    var cfg = GetConfig<MyConfig>();
                    SaveConfig(cfg);
                }
            }
            """;

        var result = GeneratorTestHelper.Run(code);

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.CompilationDiagnostics);
        Assert.NotNull(result.GeneratedEntryPoint);

        Assert.Contains("BindConfigs", result.GeneratedEntryPoint);
        Assert.Contains("registrar.Register<global::MyPlugin.MyConfig>(", result.GeneratedEntryPoint);
        Assert.Contains("\"fs.write.data\"", result.GeneratedEntryPoint);
        Assert.DoesNotContain("\"fs.read.data\"", result.GeneratedEntryPoint);
    }

    private sealed class NoOpLogger : ILogger
    {
        public void Log(LogLevel logLevel, string message) { }
    }

    public sealed class UnregisteredConfig { }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}