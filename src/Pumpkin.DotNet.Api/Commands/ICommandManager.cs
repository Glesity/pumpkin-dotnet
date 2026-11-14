namespace Pumpkin.DotNet.Api.Commands;

public interface ICommandManager
{
    /// <summary>
    /// Registers a command built with the fluent Brigadier API (Commands.Create(...)).
    /// </summary>
    void Register(CommandBuilder builder);

    /// <summary>
    /// Registers a classic command derived from the abstract Command class.
    /// </summary>
    void Register(Command command);

    /// <summary>
    /// Registers a command instance generated from the [Command] attribute through ICommandRegistrar.
    /// </summary>
    void Register<T>(T commandInstance) where T : class, ICommandRegistrar;

    /// <summary>
    /// Creates and registers a command instance marked with [Command] (through ICommandRegistrar).
    /// </summary>
    void Register<T>() where T : class, ICommandRegistrar, new() => Register(new T());
}