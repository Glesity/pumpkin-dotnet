using PluginWorld;
using PluginWorld.wit.Exports.pumpkin.plugin.v0_1_0;
using PluginWorld.wit.Imports.pumpkin.plugin.v0_1_0;
using Pumpkin.DotNet.Runtime;
using Pumpkin.DotNet.Runtime.Logging;

internal class PluginWorldExportsImpl : IPluginWorldExports
{
    private static readonly PumpkinLogger Logger = PumpkinLogger.CreateLogger("PumpkinInterop");

    public static bool HandleAiGoalCanStart(uint goalId, IServerImports.Server server, IWorldImports.Entity entity)
    {
        using (server)
        using (entity)
        {
            Logger.Warn($"AI goal callback 'HandleAiGoalCanStart' (goalId={goalId}) is not implemented in this runtime version.");
            return false;
        }
    }

    public static bool HandleAiGoalShouldContinue(uint goalId, IServerImports.Server server, IWorldImports.Entity entity)
    {
        using (server)
        using (entity)
        {
            Logger.Warn($"AI goal callback 'HandleAiGoalShouldContinue' (goalId={goalId}) is not implemented in this runtime version.");
            return false;
        }
    }

    public static void HandleAiGoalStart(uint goalId, IServerImports.Server server, IWorldImports.Entity entity)
    {
        using (server)
        using (entity)
        {
        }
    }

    public static void HandleAiGoalStop(uint goalId, IServerImports.Server server, IWorldImports.Entity entity)
    {
        using (server)
        using (entity)
        {
        }
    }

    public static void HandleAiGoalTick(uint goalId, IServerImports.Server server, IWorldImports.Entity entity)
    {
        using (server)
        using (entity)
        {
        }
    }

    public static int HandleCommand(uint commandId, ICommandImports.CommandSender sender, IServerImports.Server server, ICommandImports.ConsumedArgs args)
    {
        using (server)
        using (sender)
        using (args)
        {
            try
            {
                return PumpkinRuntime.Instance.HandleCommand(commandId, sender, server, args);
            }
            catch (Exception ex)
            {
                Logger.Error($"CommandDispatch error: {ex}");
                return 0;
            }
        }
    }

    public static ICommandImports.CommandSuggestions HandleCommandSuggestion(uint handlerId, ICommandImports.CommandSender sender, IServerImports.Server server, ICommandImports.SuggestionRequest request)
    {
        using (server)
        using (sender)
        {
            try
            {
                return PumpkinRuntime.Instance.HandleSuggestion(handlerId, sender, server, request);
            }
            catch (Exception ex)
            {
                Logger.Error($"Command suggestion error (id={handlerId}): {ex}");
                return new ICommandImports.CommandSuggestions(request.start, (uint)request.remaining.Length, []);
            }
        }
    }

    public static IEventImports.Event HandleEvent(uint eventId, IServerImports.Server server, IEventImports.Event @event)
    {
        using (server)
        {
            try
            {
                return PumpkinRuntime.Instance.HandleEvent(eventId, @event, server);
            }
            catch (Exception ex)
            {
                Logger.Error($"EventDispatch error (eventId={eventId}): {ex}");
                return @event;
            }
        }
    }

    public static void HandleGeneratePhase(uint generatorId, IWorldImports.GenerationPhase phase, IWorldImports.ChunkBuffer chunk)
    {
        using (chunk)
        {
            Logger.Warn($"World generation callback 'HandleGeneratePhase' (generatorId={generatorId}) is not implemented in this runtime version.");
        }
    }

    public static byte[] HandleIpcMessage(string sender, byte[] message)
    {
        Logger.Warn($"IPC callback 'HandleIpcMessage' from '{sender}' is not implemented in this runtime version.");
        return [];
    }

    public static void HandleTask(uint handlerId, IServerImports.Server server)
    {
        using (server)
        {
            try
            {
                PumpkinRuntime.Instance.HandleTask(server);
            }
            catch (Exception ex)
            {
                Logger.Error($"Scheduler error: {ex}");
            }
        }
    }

    public static void InitPlugin() { }

    public static void OnLoad(IContextImports.Context context)
    {
        using (context)
        {
            try
            {
                PumpkinRuntime.Instance.HandleOnLoad(context);
            }
            catch (Exception ex)
            {
                Logger.Error($"Plugin fatal load: {ex}");
            }
        }
    }

    public static void OnUnload(IContextImports.Context context)
    {
        using (context)
        {
            try
            {
                PumpkinRuntime.Instance.HandleOnUnload(context);
            }
            catch (Exception ex)
            {
                Logger.Error($"Plugin fatal unload: {ex}");
            }
        }
    }
}

internal class MetadataExportsImpl : IMetadataExports
{
    public static IMetadataExports.PluginMetadata GetMetadata()
    {
        var metadata = PumpkinRuntime.Instance.Metadata;
        if (metadata is null)
        {
            return new IMetadataExports.PluginMetadata("Unknown", "0.0.0", [], string.Empty, [], []);
        }

        return new IMetadataExports.PluginMetadata(
            metadata.Name,
            metadata.Version,
            [.. metadata.Authors],
            metadata.Description,
            [],
            []
        );
    }
}