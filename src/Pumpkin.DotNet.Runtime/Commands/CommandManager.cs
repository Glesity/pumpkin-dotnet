using PluginWorld.wit.Imports.pumpkin.plugin.v0_1_0;
using Pumpkin.DotNet.Api.Commands;

namespace Pumpkin.DotNet.Runtime.Commands;
internal sealed class CommandManager : ICommandManager
{
    private readonly string _pluginNamespace;
    private readonly List<CommandRootModel> _pendingCommands = [];
    private IContextImports.Context? _activeLoadContext;
    private bool _isLoaded;

    public CommandManager(string pluginNamespace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginNamespace);
        _pluginNamespace = pluginNamespace;
    }

    /// <summary>
    /// Called by PumpkinRuntime.HandleOnLoad(context) before invoking the user-defined OnLoad().
    /// </summary>
    internal void BeginLoad(IContextImports.Context context)
    {
        _activeLoadContext = context;
        _isLoaded = true;

        if (_pendingCommands.Count > 0)
        {
            foreach (var model in _pendingCommands)
            {
                CommandRegistry.Instance.RegisterToHost(context, model, _pluginNamespace);
            }
            _pendingCommands.Clear();
        }
    }

    /// <summary>
    /// Called by PumpkinRuntime.HandleOnLoad(context) after the user-defined OnLoad() completes.
    /// </summary>
    internal void EndLoad()
    {
        _activeLoadContext = null;
    }

    public void Register(CommandBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var model = BuilderToModelCompiler.Compile(builder);
        RegisterModel(model);
    }

    public void Register(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var model = LegacyCommandAdapter.ToRootModel(command);
        RegisterModel(model);
    }

    public void Register<T>(T commandInstance) where T : class, ICommandRegistrar
    {
        ArgumentNullException.ThrowIfNull(commandInstance);
        Register(commandInstance.BuildCommand());
    }

    public void Register<T>() where T : class, ICommandRegistrar, new()
    {
        Register(new T());
    }

    private void RegisterModel(CommandRootModel model)
    {
        if (model.Names.Length == 0 || string.IsNullOrWhiteSpace(model.Names[0]))
            throw new ArgumentException("A command must have at least one valid name.");

        if (_activeLoadContext is not null)
        {
            CommandRegistry.Instance.RegisterToHost(_activeLoadContext, model, _pluginNamespace);
        }
        else if (!_isLoaded)
        {
            _pendingCommands.Add(model);
        }
        else
        {
            throw new InvalidOperationException(
                $"Cannot register command '/{model.Names[0]}' after OnLoad has completed: the Pumpkin host context is closed.");
        }
    }
}