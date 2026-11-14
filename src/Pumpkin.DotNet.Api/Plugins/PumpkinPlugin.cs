using Pumpkin.DotNet.Api.Logging;
using Pumpkin.DotNet.Api.Scheduling;
using Pumpkin.DotNet.Api.Server;

namespace Pumpkin.DotNet.Api.Plugins;

public abstract class PumpkinPlugin
{
    public IPluginContext Context { get; internal set; } = null!;

    public IServer Server => Context.Server;
    public ILogger Logger => Context.Logger;

    internal void BindContext(IPluginContext context)
    {
        Context = context;
    }

    public virtual void OnLoad() {}
    public virtual void OnUnload() {}
}
