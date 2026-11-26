using System.ComponentModel;

namespace Pumpkin.DotNet.Runtime.Configuration;

[EditorBrowsable(EditorBrowsableState.Never)]
public interface IConfigBinderRegistrar
{
    void Register<T>(
        Func<IReadOnlyDictionary<string, string?>, T> bind,
        Func<T, Dictionary<string, string?>> serialize) where T : new();
}