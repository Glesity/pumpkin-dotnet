using Microsoft.Extensions.Configuration;
using Pumpkin.DotNet.Api.Configuration;
using Pumpkin.DotNet.Api.Logging;
using Pumpkin.DotNet.Api.Server;

namespace Pumpkin.DotNet.Api.Plugins;

public abstract class PumpkinPlugin
{
    private const string DefaultConfigName = "config.toml";

    public IPluginContext Context { get; internal set; } = null!;

    public IServer Server => Context.Server;
    public ILogger Logger => Context.Logger;
    public IConfiguration Configuration => Context.Configuration;
    
    protected void RegisterConfigFormat(string extension, IConfigFormat format)
        => Context.RegisterConfigFormat(extension, format);
    protected void SaveDefaultConfig(string fileName = DefaultConfigName, bool copyDefaults = false) 
        => Context.SaveDefaultConfig(fileName, copyDefaults);
    protected T GetConfig<T>(string fileName = DefaultConfigName) where T : new() 
        => Context.GetConfig<T>(fileName);
    protected IReadOnlyList<string> GetConfigFiles(string searchPattern = "*", bool recursive = false) => Context.GetConfigFiles(searchPattern, recursive);
    protected void SaveConfig<T>(T config, string fileName = DefaultConfigName) where T : new() 
        => Context.SaveConfig(config, fileName);

    internal void BindContext(IPluginContext context)
    {
        Context = context;
    }

    public virtual void OnLoad() {}
    public virtual void OnUnload() {}
}
