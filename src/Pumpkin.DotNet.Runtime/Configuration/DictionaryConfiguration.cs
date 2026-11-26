using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace Pumpkin.DotNet.Runtime.Configuration;

internal sealed class DictionaryConfiguration : IConfigurationSection
{
    private sealed class DataBox(Dictionary<string, string?> values)
    {
        public Dictionary<string, string?> Values = values;
    }

    private readonly DataBox _box;
    private readonly string _prefix;

    public DictionaryConfiguration(Dictionary<string, string?> data, string prefix = "")
        : this(new DataBox(data), prefix)
    {
    }

    private DictionaryConfiguration(DataBox box, string prefix)
    {
        _box = box;
        _prefix = prefix;
    }

    public void Reload(Dictionary<string, string?> newValues)
    {
        _box.Values = newValues;
    }

    public string? this[string key]
    {
        get => _box.Values.TryGetValue(CombineKey(key), out var val) ? val : null;
        set => _box.Values[CombineKey(key)] = value;
    }

    public string Key
    {
        get
        {
            if (string.IsNullOrEmpty(_prefix)) return string.Empty;
            var idx = _prefix.LastIndexOf(':');
            return idx < 0 ? _prefix : _prefix[(idx + 1)..];
        }
    }

    public string Path => _prefix;

    public string? Value
    {
        get => _box.Values.TryGetValue(_prefix, out var val) ? val : null;
        set => _box.Values[_prefix] = value;
    }

    public IConfigurationSection GetSection(string key) =>
        new DictionaryConfiguration(_box, CombineKey(key));

    public IEnumerable<IConfigurationSection> GetChildren()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var prefixWithColon = string.IsNullOrEmpty(_prefix) ? string.Empty : _prefix + ":";

        foreach (var k in _box.Values.Keys)
        {
            if (!k.StartsWith(prefixWithColon, StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = k.AsSpan(prefixWithColon.Length);
            if (rest.IsEmpty)
                continue;

            var colonIdx = rest.IndexOf(':');
            var childSegment = (colonIdx < 0 ? rest : rest[..colonIdx]).ToString();

            if (seen.Add(childSegment))
            {
                yield return new DictionaryConfiguration(_box, prefixWithColon + childSegment);
            }
        }
    }

    public IChangeToken GetReloadToken() => NullChangeToken.Instance;

    private string CombineKey(string key) =>
        string.IsNullOrEmpty(_prefix) ? key : $"{_prefix}:{key}";

    private sealed class NullChangeToken : IChangeToken
    {
        public static readonly NullChangeToken Instance = new();
        public bool HasChanged => false;
        public bool ActiveChangeCallbacks => false;
        public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) => EmptyDisposable.Instance;
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();
        public void Dispose() { }
    }
}