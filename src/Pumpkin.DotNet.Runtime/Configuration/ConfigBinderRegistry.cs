namespace Pumpkin.DotNet.Runtime.Configuration;

internal sealed class ConfigBinderRegistry : IConfigBinderRegistrar
{
    private readonly Dictionary<Type, object> _binders = new();

    public void Register<T>(
        Func<IReadOnlyDictionary<string, string?>, T> bind,
        Func<T, Dictionary<string, string?>> serialize) where T : new()
    {
        _binders[typeof(T)] = new Entry<T>(bind, serialize);
    }

    public Entry<T> Get<T>() where T : new()
    {
        if (_binders.TryGetValue(typeof(T), out var entry))
            return (Entry<T>)entry;

        throw new InvalidOperationException(
            $"Config binder for '{typeof(T).FullName}' was not generated. Ensure Pumpkin.DotNet.SourceGenerator is referenced.");
    }

    public sealed record Entry<T>(
        Func<IReadOnlyDictionary<string, string?>, T> Bind,
        Func<T, Dictionary<string, string?>> Serialize);
}