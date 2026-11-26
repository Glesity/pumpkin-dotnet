using PluginWorld.wit.Imports.pumpkin.plugin.v0_1_0;
using Pumpkin.DotNet.Api.Commands;
using Pumpkin.DotNet.Api.Events;
using Pumpkin.DotNet.Api.Events.Player;
using Pumpkin.DotNet.Api.Plugins;
using Pumpkin.DotNet.Runtime.Commands;
using Pumpkin.DotNet.Runtime.Configuration;
using Pumpkin.DotNet.Runtime.Context;
using Pumpkin.DotNet.Runtime.Entities.Players;
using Pumpkin.DotNet.Runtime.Events;
using Pumpkin.DotNet.Runtime.Logging;
using Pumpkin.DotNet.Runtime.Scheduling;
using Pumpkin.DotNet.Runtime.Server;

namespace Pumpkin.DotNet.Runtime;

public class PumpkinRuntime
{
    public static PumpkinRuntime Instance => field ??= new PumpkinRuntime();

    public bool IsLoaded { get; private set; }

    private PumpkinRuntime() { }

    private PumpkinServer? _server;

    internal PumpkinServer Server =>
        _server ?? throw new InvalidOperationException("IServer is only available during an active host callback.");

    internal PumpkinLogger Logger { get; private set; } = PumpkinLogger.CreateLogger<PumpkinRuntime>();
    internal PumpkinScheduler Scheduler { get; private set; } = new();

    internal PumpkinPlugin Plugin { get; private set; } = null!;
    internal PluginMetadata Metadata { get; private set; } = null!;

    private Action<PumpkinPlugin, IEventRegistrar>? _eventBinder;
    private Action<PumpkinPlugin, ICommandManager>? _bindCommands;
    private readonly ConfigBinderRegistry _configBinders = new();

    private CommandManager? _commandManager;

    public void RegisterPlugin(
        PumpkinPlugin plugin,
        PluginMetadata metadata,
        Action<PumpkinPlugin, IEventRegistrar> eventBinds,
        Action<PumpkinPlugin, ICommandManager>? bindCommands = null,
        Action<IConfigBinderRegistrar>? bindConfigs = null)
    {
        if (Plugin is not null)
            throw new InvalidOperationException("Plugin is already registered.");

        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(metadata);

        Metadata = metadata;
        Plugin = plugin;

        _eventBinder = eventBinds;
        _bindCommands = bindCommands;

        _commandManager = new CommandManager(metadata.Name);

        bindConfigs?.Invoke(_configBinders);
    }

    internal void HandleOnLoad(IContextImports.Context context)
    {
        if (IsLoaded)
            throw new InvalidOperationException("Plugin is already loaded.");

        if (Plugin is null)
            throw new InvalidOperationException("Cannot load runtime: no plugin has been registered.");

        ArgumentNullException.ThrowIfNull(context);

        using var server = PumpkinServer.CreateOwned(context.GetServer());
        WithServer(server, () =>
        {
            try
            {
                var pluginContext = new PumpkinPluginContext(
                    PumpkinLogger.CreateLogger(Metadata.Name ?? "Plugin"),
                    context.GetDataFolder(),
                    Plugin.GetType().Assembly,
                    _configBinders
                );

                Plugin.BindContext(pluginContext);
                _commandManager!.BeginLoad(context);

                using (var eventRegistrar = new EventRegistrar(context, EventManager.Instance))
                {
                    _bindCommands?.Invoke(Plugin, _commandManager);

                    eventRegistrar.Register<PlayerLeaveEvent>(static e => PlayerCache.Remove(e.Player));
                    _eventBinder?.Invoke(Plugin, eventRegistrar);

                    Plugin.OnLoad();
                }

                ISchedulerImports.ScheduleRepeatingTask(0, 1, 1);
                IsLoaded = true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load plugin '{Metadata.Name}': {ex}");
                CleanupState();
                throw;
            }
        });
    }

    internal void HandleOnUnload(IContextImports.Context context)
    {
        if (!IsLoaded)
            throw new InvalidOperationException("Plugin is not loaded.");

        ArgumentNullException.ThrowIfNull(context);

        using var server = PumpkinServer.CreateOwned(context.GetServer());
        WithServer(server, () =>
        {
            try
            {
                Plugin.OnUnload();
            }
            catch (Exception ex)
            {
                Logger.Error($"Error occurred during plugin unload: {ex}");
            }
            finally
            {
                CleanupState();
            }
        });
    }

    private void CleanupState()
    {
        IsLoaded = false;

        try { Scheduler.Dispose(); }
        catch (Exception ex) { Logger.Error($"Failed to dispose scheduler: {ex}"); }

        try { EventManager.Instance.Clear(); }
        catch (Exception ex) { Logger.Error($"Failed to clear events: {ex}"); }

        try { CommandRegistry.Instance.Clear(); }
        catch (Exception ex) { Logger.Error($"Failed to clear commands: {ex}"); }

        try { PlayerCache.Clear(); }
        catch (Exception ex) { Logger.Error($"Failed to clear player cache: {ex}"); }
    }

    internal void HandleTask(IServerImports.Server server)
    {
        if (!IsLoaded)
            return;

        using var scopedServer = PumpkinServer.CreateBorrowed(server);
        WithServer(scopedServer, Scheduler.Tick);
    }

    internal IEventImports.Event HandleEvent(uint eventId, IEventImports.Event @event, IServerImports.Server server)
    {
        if (!IsLoaded)
            return @event;

        using var scopedServer = PumpkinServer.CreateBorrowed(server);
        return WithServer(scopedServer, () => EventDispatcher.Dispatch(eventId, @event));
    }

    internal int HandleCommand(uint commandId, ICommandImports.CommandSender sender, IServerImports.Server server, ICommandImports.ConsumedArgs args)
    {
        if (!IsLoaded)
            return 0;

        using var scopedServer = PumpkinServer.CreateBorrowed(server);
        return WithServer(scopedServer, () => CommandRegistry.Instance.DispatchExecute(commandId, sender, scopedServer, args));
    }

    internal ICommandImports.CommandSuggestions HandleSuggestion(uint handlerId, ICommandImports.CommandSender sender, IServerImports.Server server, ICommandImports.SuggestionRequest request)
    {
        if (!IsLoaded)
            return new ICommandImports.CommandSuggestions(request.start, (uint)request.remaining.Length, []);

        using var scopedServer = PumpkinServer.CreateBorrowed(server);
        return WithServer(scopedServer, () => CommandRegistry.Instance.DispatchSuggestion(handlerId, sender, scopedServer, request));
    }

    private void WithServer(PumpkinServer server, Action action)
    {
        var previousServer = _server;
        _server = server;
        try
        {
            action();
        }
        finally
        {
            _server = previousServer;
        }
    }

    private T WithServer<T>(PumpkinServer server, Func<T> action)
    {
        var previousServer = _server;
        _server = server;
        try
        {
            return action();
        }
        finally
        {
            _server = previousServer;
        }
    }
}