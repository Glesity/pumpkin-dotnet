namespace Pumpkin.DotNet.Tests;

using Microsoft.CodeAnalysis;
using Xunit;

public sealed class PumpkinPluginGeneratorTests
{
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
    public void Should_Generate_Valid_EntryPoint_With_Events_And_Commands()
    {
        const string code = """
            using System.Collections.Generic;
            using Pumpkin.DotNet.Api.Plugins;
            using Pumpkin.DotNet.Api.Commands;
            using Pumpkin.DotNet.Api.Server;
            using Pumpkin.DotNet.Api.Logging;

            namespace MyPlugin;

            public sealed class SamplePlugin : PumpkinPlugin { }

            [Command("warp", "w", Description = "Teleport to warp")]
            public sealed class WarpCommand
            {
                [Subcommand("tp")]
                [Execute]
                public void Teleport(
                    IServer server,
                    ILogger logger,
                    ICommandContext ctx,
                    [Suggest(nameof(SuggestWarps))] string target,
                    int delay = 0)
                {
                }

                public IEnumerable<string> SuggestWarps(IServer server, ILogger logger, string input)
                    => ["spawn", "hub"];
            }
            """;

        var props = new Dictionary<string, string>
        {
            ["build_property.PluginName"] = "SamplePlugin",
            ["build_property.Version"] = "1.2.3",
            ["build_property.Authors"] = "Alice, Bob",
            ["build_property.Description"] = "Test description"
        };

        var result = GeneratorTestHelper.Run(code, props);

        // 1. The generator reports no errors and the generated code compiles without C# errors.
        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.CompilationDiagnostics);
        Assert.NotNull(result.GeneratedEntryPoint);

        // 2. Verify key fragments in the generated PumpkinPluginEntryPoint.g.cs.
        Assert.Contains("[UnmanagedCallersOnly(EntryPoint = \"init-plugin\")]", result.GeneratedEntryPoint);
        Assert.Contains("new PluginMetadata(", result.GeneratedEntryPoint);
        Assert.Contains("\"SamplePlugin\",", result.GeneratedEntryPoint);
        Assert.Contains("\"1.2.3\",", result.GeneratedEntryPoint);
        Assert.Contains("new string[] { \"Alice\", \"Bob\" },", result.GeneratedEntryPoint);
        Assert.Contains("\"Test description\"", result.GeneratedEntryPoint);        
        Assert.Contains("Command.Create(\"warp\", \"w\")", result.GeneratedEntryPoint);
        Assert.Contains("Command.Literal(\"tp\")", result.GeneratedEntryPoint);
        Assert.Contains("Command.Argument<string>(\"target\")", result.GeneratedEntryPoint);
        Assert.Contains("Command.Argument<int>(\"delay\")", result.GeneratedEntryPoint);
        Assert.Contains("plugin.Server, plugin.Logger, ctx", result.GeneratedEntryPoint);
    }
}