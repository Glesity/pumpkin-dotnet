using Pumpkin.DotNet.Api.Logging;
using Pumpkin.DotNet.Api.Server;

namespace Pumpkin.DotNet.Api.Plugins;

public interface IPluginContext
{
    IServer Server { get; }
    ILogger Logger { get; }
    // TODO: Add configuration support.
}