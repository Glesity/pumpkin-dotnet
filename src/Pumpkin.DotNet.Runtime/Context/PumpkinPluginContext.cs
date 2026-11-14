using Pumpkin.DotNet.Api;
using Pumpkin.DotNet.Api.Logging;
using Pumpkin.DotNet.Api.Plugins;
using Pumpkin.DotNet.Api.Scheduling;
using Pumpkin.DotNet.Api.Server;
using Pumpkin.DotNet.Runtime.Server;

namespace Pumpkin.DotNet.Runtime.Context;

internal record PumpkinPluginContext(
    IServer Server,
    ILogger Logger
) : IPluginContext;