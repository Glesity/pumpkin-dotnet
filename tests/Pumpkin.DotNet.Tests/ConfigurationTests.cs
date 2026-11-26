using Pumpkin.DotNet.Tests;

public sealed class ConfigGeneratorTests
{
    [Fact]
    public void Generates_Valid_Binder_And_Dictionary_Serializer_For_Complex_Config()
    {
        const string pluginCode = """
            using System.Collections.Generic;
            using Pumpkin.DotNet.Api.Plugins;

            namespace MyTestPlugin;

            public sealed class ServerSettings
            {
                public string Motd { get; set; } = "Default";
                public int MaxPlayers { get; set; } = 20;
                public FeaturesSection Features { get; set; } = new();
                public List<SpawnNode> Spawns { get; set; } = [];

                public sealed class FeaturesSection
                {
                    public bool EnablePvp { get; set; } = true;
                    public List<string> Worlds { get; set; } = ["world"];
                }

                public sealed class SpawnNode
                {
                    public string Name { get; set; } = "";
                    public double X { get; set; }
                }
            }

            public sealed class MyPlugin : PumpkinPlugin
            {
                public override void OnLoad()
                {
                    var cfg = GetConfig<ServerSettings>();
                    SaveConfig(cfg);
                }
            }
            """;

        var result = GeneratorTestHelper.Run(pluginCode);

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.CompilationDiagnostics);
        Assert.NotNull(result.GeneratedEntryPoint);

        var generatedCode = result.GeneratedEntryPoint!;

        Assert.Contains("private static void BindConfigs(IConfigBinderRegistrar registrar)", generatedCode);
        Assert.Contains("registrar.Register<global::MyTestPlugin.ServerSettings>(BindConfig_0, SerializeConfig_0);", generatedCode);
        Assert.Contains("\"fs.write.data\"", generatedCode);
        Assert.DoesNotContain("\"fs.read.data\"", generatedCode);

        Assert.Contains("values.TryGetValue(\"motd\", out var", generatedCode);
        Assert.Contains("values.TryGetValue(\"max_players\", out var", generatedCode);
        Assert.Contains("values.TryGetValue(\"features:enable_pvp\", out var", generatedCode);
        Assert.Contains("values.ContainsKey(\"features:worlds:0\")", generatedCode);
        Assert.Contains("values.ContainsKey(\"spawns:0:name\")", generatedCode);

        Assert.Contains("dict[\"motd\"] =", generatedCode);
        Assert.Contains("dict[\"features:enable_pvp\"] = (config.Features.EnablePvp ? \"true\" : \"false\");", generatedCode);
    }

    [Fact]
    public void Grants_Only_ReadData_Permission_When_Config_Is_Only_Read()
    {
        const string pluginCode = """
            using Pumpkin.DotNet.Api.Plugins;

            namespace MyTestPlugin;

            public sealed class ReadOnlySettings
            {
                public string Motd { get; set; } = "Default";
            }

            public sealed class ReadOnlyPlugin : PumpkinPlugin
            {
                public override void OnLoad()
                {
                    var cfg = GetConfig<ReadOnlySettings>();
                }
            }
            """;

        var result = GeneratorTestHelper.Run(pluginCode);

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.CompilationDiagnostics);
        Assert.NotNull(result.GeneratedEntryPoint);
        Assert.Contains("\"fs.read.data\"", result.GeneratedEntryPoint!);
        Assert.DoesNotContain("\"fs.write.data\"", result.GeneratedEntryPoint!);
    }

    [Fact]
    public void Does_Not_Add_Fs_Permissions_When_Config_Is_Not_Used()
    {
        const string pluginCode = """
            using Pumpkin.DotNet.Api.Plugins;

            namespace MyTestPlugin;

            public sealed class EmptyPlugin : PumpkinPlugin
            {
                public override void OnLoad() { }
            }
            """;

        var result = GeneratorTestHelper.Run(pluginCode);

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.CompilationDiagnostics);
        Assert.NotNull(result.GeneratedEntryPoint);
        Assert.DoesNotContain("fs.read.data", result.GeneratedEntryPoint!);
        Assert.DoesNotContain("fs.write.data", result.GeneratedEntryPoint!);
    }
}