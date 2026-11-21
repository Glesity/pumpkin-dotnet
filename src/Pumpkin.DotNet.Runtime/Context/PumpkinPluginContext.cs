using Pumpkin.DotNet.Api;
using Pumpkin.DotNet.Api.Logging;
using Pumpkin.DotNet.Api.Plugins;
using Pumpkin.DotNet.Api.Scheduling;
using Pumpkin.DotNet.Api.Server;
using Pumpkin.DotNet.Runtime.Server;

namespace Pumpkin.DotNet.Runtime.Context;

internal sealed record PumpkinPluginContext(ILogger Logger) : IPluginContext
{
    public IServer Server => PumpkinRuntime.Instance.Server;
}