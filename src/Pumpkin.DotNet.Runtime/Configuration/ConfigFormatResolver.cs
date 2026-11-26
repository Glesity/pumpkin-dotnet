using Pumpkin.DotNet.Api.Configuration;
using Pumpkin.DotNet.Runtime.Configuration.Formats;

namespace Pumpkin.DotNet.Runtime.Configuration;

internal static class ConfigFormatResolver
{
    private static readonly Dictionary<string, IConfigFormat> _formats = new(StringComparer.OrdinalIgnoreCase)
    {
        [".toml"] = new TomlConfigFormat(),
        [".json"] = new JsonConfigFormat()
    };

    public static void RegisterFormat(string extension, IConfigFormat format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        ArgumentNullException.ThrowIfNull(format);

        if (!extension.StartsWith('.'))
            extension = "." + extension;

        _formats[extension] = format;
    }

    public static IConfigFormat Resolve(string fileName)
    {
        var extension = Path.GetExtension(fileName);

        if (string.IsNullOrEmpty(extension))
            return _formats[".toml"];

        if (_formats.TryGetValue(extension, out var format))
            return format;

        throw new NotSupportedException(
            $"Config format '{extension}' is not registered. Call Context.RegisterFormat(\"{extension}\", ...) first.");
    }

    public static bool IsSupportedExtension(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return !string.IsNullOrEmpty(ext) && _formats.ContainsKey(ext);
    }
}