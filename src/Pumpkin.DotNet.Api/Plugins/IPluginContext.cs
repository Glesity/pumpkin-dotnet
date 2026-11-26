using Microsoft.Extensions.Configuration;
using Pumpkin.DotNet.Api.Configuration;
using Pumpkin.DotNet.Api.Logging;
using Pumpkin.DotNet.Api.Server;

namespace Pumpkin.DotNet.Api.Plugins;

public interface IPluginContext
{
    IServer Server { get; }
    ILogger Logger { get; }

    IConfiguration Configuration { get; }

    void SaveDefaultConfig(string fileName = "config.toml", bool copyDefaults = false);
    void ReloadConfig();
    void SaveConfig();
    T GetConfig<T>(string fileName = "config.toml") where T : new();
    void SaveConfig<T>(T config, string fileName = "config.toml") where T : new();
    void RegisterConfigFormat(string extension, IConfigFormat format);
    IReadOnlyList<string> GetConfigFiles(string searchPattern = "*", bool recursive = false);
}