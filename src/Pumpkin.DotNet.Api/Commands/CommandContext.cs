using System.Diagnostics.CodeAnalysis;
using Pumpkin.DotNet.Api.Text;

namespace Pumpkin.DotNet.Api.Commands;

public sealed class CommandContext : ICommandContext
{
    private readonly IReadOnlyDictionary<string, object?> _arguments;

    public ICommandSender Sender { get; }
    public int Result { get; set; } = 1;

    public CommandContext(ICommandSender sender, IReadOnlyDictionary<string, object?> arguments)
    {
        Sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _arguments = arguments;
    }

    public bool Has(string name) => _arguments.ContainsKey(name);

    public T Get<T>(string name)
    {
        if (!_arguments.TryGetValue(name, out var val))
            throw new KeyNotFoundException($"Argument '{name}' was not found in the command context.");

        if (val is T typed)
            return typed;

        throw new InvalidCastException($"Argument '{name}' has type {val?.GetType().Name ?? "null"}; expected {typeof(T).Name}.");
    }

    public T GetOrDefault<T>(string name, T defaultValue)
        => TryGet<T>(name, out var val) ? val : defaultValue;

    public bool TryGet<T>(string name, [MaybeNullWhen(false)] out T value)
    {
        if (_arguments.TryGetValue(name, out var raw) && raw is T typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }

    public void Fail(Component message)
    {
        Sender.SendError(message);
        Result = 0;
    }
}