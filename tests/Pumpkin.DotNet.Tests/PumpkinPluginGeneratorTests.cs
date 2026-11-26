namespace Pumpkin.DotNet.Tests;

using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Xunit;

public sealed class PumpkinPluginGeneratorTests
{
    private static readonly Dictionary<string, string> DefaultProps = new()
    {
        ["build_property.PluginName"] = "SamplePlugin",
        ["build_property.Version"] = "1.2.3",
        ["build_property.Authors"] = "Alice, Bob",
        ["build_property.Description"] = "Test description"
    };

    [Fact]
    public void Should_Report_PMP001_When_PluginClass_Is_Missing()
    {
        const string code = """
            namespace MyPlugin;

            public class NotAPlugin { }
            """;

        var result = GeneratorTestHelper.Run(code);

        Assert.Null(result.GeneratedEntryPoint);
        var diag = Assert.Single(result.GeneratorDiagnostics);
        Assert.Equal("PMP001", diag.Id);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
    }

    [Fact]
    public void Should_Report_PMP002_When_Multiple_PluginClasses_Exist()
    {
        const string code = """
            using Pumpkin.DotNet.Api.Plugins;

            namespace MyPlugin;

            public sealed class FirstPlugin : PumpkinPlugin { }
            public sealed class SecondPlugin : PumpkinPlugin { }
            """;

        var result = GeneratorTestHelper.Run(code);

        Assert.Null(result.GeneratedEntryPoint);
        var diag = Assert.Single(result.GeneratorDiagnostics);
        Assert.Equal("PMP002", diag.Id);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("SecondPlugin", diag.GetMessage());
    }

    [Fact]
    public void Should_Report_PMP003_When_Handler_Class_Has_No_Parameterless_Constructor()
    {
        const string code = """
            using Pumpkin.DotNet.Api.Commands;
            using Pumpkin.DotNet.Api.Plugins;

            namespace MyPlugin;

            public sealed class SamplePlugin : PumpkinPlugin { }

            [Command("broken")]
            public sealed class BrokenCommand
            {
                public BrokenCommand(int requiredArg) { }

                [Execute]
                public void Run(ICommandContext ctx) { }
            }
            """;

        var result = GeneratorTestHelper.Run(code, DefaultProps);

        var diag = Assert.Single(result.GeneratorDiagnostics);
        Assert.Equal("PMP003", diag.Id);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("BrokenCommand", diag.GetMessage());
    }

    [Fact]
    public void Should_Report_PMP004_When_Suggest_Method_Has_Invalid_Parameter()
    {
        const string code = """
            using System.Collections.Generic;
            using Pumpkin.DotNet.Api.Commands;
            using Pumpkin.DotNet.Api.Plugins;

            namespace MyPlugin;

            public sealed class SamplePlugin : PumpkinPlugin { }

            [Command("bad_suggest")]
            public sealed class BadSuggestCommand
            {
                [Execute]
                public void Run([Suggest(nameof(BadSuggest))] string target) { }

                public IEnumerable<string> BadSuggest(int unsupportedParam) => [];
            }
            """;

        var result = GeneratorTestHelper.Run(code, DefaultProps);

        var diag = Assert.Single(result.GeneratorDiagnostics);
        Assert.Equal("PMP004", diag.Id);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("BadSuggest", diag.GetMessage());
    }

    [Fact]
    public void Should_Report_PMP005_When_EventHandler_Is_Private()
    {
        const string code = """
            using Pumpkin.DotNet.Api.Events;
            using Pumpkin.DotNet.Api.Events.Player;
            using Pumpkin.DotNet.Api.Plugins;

            namespace MyPlugin;

            public sealed class SamplePlugin : PumpkinPlugin
            {
                [EventHandler]
                private void SecretHandler(PlayerJoinEvent evt) { }
            }
            """;

        var result = GeneratorTestHelper.Run(code, DefaultProps);

        var diag = Assert.Single(result.GeneratorDiagnostics);
        Assert.Equal("PMP005", diag.Id);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
    }

    [Fact]
    public void Should_Report_PMP006_When_EventHandler_Has_Invalid_Return_Type()
    {
        const string code = """
            using System.Threading.Tasks;
            using Pumpkin.DotNet.Api.Events;
            using Pumpkin.DotNet.Api.Events.Player;
            using Pumpkin.DotNet.Api.Plugins;

            namespace MyPlugin;

            public sealed class SamplePlugin : PumpkinPlugin
            {
                [EventHandler]
                public Task<int> InvalidHandler(PlayerJoinEvent evt) => Task.FromResult(42);
            }
            """;

        var result = GeneratorTestHelper.Run(code, DefaultProps);

        var diag = Assert.Single(result.GeneratorDiagnostics);
        Assert.Equal("PMP006", diag.Id);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
    }

    [Fact]
    public void Should_Generate_Valid_EntryPoint_With_Events_Commands_And_IConfiguration_Injection()
    {
        const string code = """
            using System.Collections.Generic;
            using Microsoft.Extensions.Configuration;
            using Pumpkin.DotNet.Api.Plugins;
            using Pumpkin.DotNet.Api.Commands;
            using Pumpkin.DotNet.Api.Events;
            using Pumpkin.DotNet.Api.Events.Player;
            using Pumpkin.DotNet.Api.Server;
            using Pumpkin.DotNet.Api.Logging;

            namespace MyPlugin;

            public sealed class SamplePlugin : PumpkinPlugin
            {
                [EventHandler(Priority = EventPriority.High, IsModifying = false)]
                public void OnJoin(PlayerJoinEvent evt, IConfiguration config, ILogger logger) { }
            }

            [Command("warp", "w", Description = "Teleport to warp")]
            public sealed class WarpCommand
            {
                [Subcommand("tp")]
                [Execute]
                public void Teleport(
                    IServer server,
                    ILogger logger,
                    IConfiguration config,
                    ICommandContext ctx,
                    [Suggest(nameof(SuggestWarps))] string target,
                    int delay = 0)
                {
                }

                public IEnumerable<string> SuggestWarps(IServer server, ILogger logger, IConfiguration config, string input)
                    => ["spawn", "hub"];
            }
            """;

        var result = GeneratorTestHelper.Run(code, DefaultProps);

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.CompilationDiagnostics);
        Assert.NotNull(result.GeneratedEntryPoint);

        Assert.Contains("[UnmanagedCallersOnly(EntryPoint = \"init-plugin\")]", result.GeneratedEntryPoint);
        Assert.Contains("new PluginMetadata(", result.GeneratedEntryPoint);
        Assert.Contains("\"SamplePlugin\",", result.GeneratedEntryPoint);
        Assert.Contains("\"1.2.3\",", result.GeneratedEntryPoint);
        Assert.Contains("new string[] { \"Alice\", \"Bob\" },", result.GeneratedEntryPoint);
        Assert.Contains("\"Test description\"", result.GeneratedEntryPoint);

        Assert.Contains("plugin.OnJoin(evt, plugin.Configuration, plugin.Logger);", result.GeneratedEntryPoint);

        Assert.Contains("Command.Create(\"warp\", \"w\")", result.GeneratedEntryPoint);
        Assert.Contains("Command.Literal(\"tp\")", result.GeneratedEntryPoint);
        Assert.Contains("Command.Argument<string>(\"target\")", result.GeneratedEntryPoint);
        Assert.Contains("Command.Argument<int>(\"delay\")", result.GeneratedEntryPoint);
        Assert.Contains("plugin.Server, plugin.Logger, plugin.Configuration, ctx", result.GeneratedEntryPoint);
        Assert.Contains("SuggestWarps(plugin.Server, plugin.Logger, plugin.Configuration, input)", result.GeneratedEntryPoint);
    }

    [Fact]
    public void Should_Support_Static_Command_Classes()
    {
        const string code = """
            using Microsoft.Extensions.Configuration;
            using Pumpkin.DotNet.Api.Commands;
            using Pumpkin.DotNet.Api.Plugins;

            namespace MyPlugin;

            public sealed class SamplePlugin : PumpkinPlugin { }

            [Command("ping")]
            public static class PingCommand
            {
                [Execute]
                public static void Ping(ICommandContext ctx, IConfiguration config) { }
            }
            """;

        var result = GeneratorTestHelper.Run(code, DefaultProps);

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.CompilationDiagnostics);
        Assert.NotNull(result.GeneratedEntryPoint);
        Assert.Contains("global::MyPlugin.PingCommand.Ping(ctx, plugin.Configuration);", result.GeneratedEntryPoint);
    }
}