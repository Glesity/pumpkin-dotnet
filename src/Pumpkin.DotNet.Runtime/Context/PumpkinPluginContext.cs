namespace Pumpkin.DotNet.Runtime.Context;

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Pumpkin.DotNet.Api.Configuration;
using Pumpkin.DotNet.Api.Logging;
using Pumpkin.DotNet.Api.Plugins;
using Pumpkin.DotNet.Api.Server;
using Pumpkin.DotNet.Runtime.Configuration;

internal sealed record PumpkinPluginContext(
    ILogger Logger,
    string DataDirectory,
    Assembly PluginAssembly,
    ConfigBinderRegistry Binders) : IPluginContext
{
    private const string DefaultConfigName = "config.toml";
    private DictionaryConfiguration? _configuration;

    public IServer Server => PumpkinRuntime.Instance.Server;

    public IConfiguration Configuration =>
        _configuration ??= new DictionaryConfiguration(LoadMergedValues(DefaultConfigName));
    public void RegisterConfigFormat(string extension, IConfigFormat format) 
        => ConfigFormatResolver.RegisterFormat(extension, format);

    public IReadOnlyList<string> GetConfigFiles(string searchPattern = "*", bool recursive = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(searchPattern);

        var rootDir = GetMountedDataDirectory();
        if (rootDir is null)
            return Array.Empty<string>();

        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var results = new List<string>();

        foreach (var fullPath in Directory.EnumerateFiles(rootDir, searchPattern, option))
        {
            var normalized = fullPath.Replace('\\', '/');

            var relative = normalized.StartsWith(rootDir + "/", StringComparison.OrdinalIgnoreCase)
                ? normalized[(rootDir.Length + 1)..]
                : Path.GetFileName(normalized);

            if (searchPattern == "*" && !ConfigFormatResolver.IsSupportedExtension(relative))
                continue;

            results.Add(relative);
        }

        return results;
    }

    private string? GetMountedDataDirectory()
    {
        var dir = DataDirectory.Replace('\\', '/').Trim('/');
        if (string.IsNullOrEmpty(dir) || dir == ".")
            return Directory.Exists(".") ? "." : null;

        if (Directory.Exists("/" + dir))
            return "/" + dir;

        if (Directory.Exists(dir))
            return dir;

        return null;
    }

    public void SaveDefaultConfig(string fileName = DefaultConfigName, bool copyDefaults = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        if (!IsDataDirectoryMounted())
        {
            Logger.Warn($"WASI data directory '{DataDirectory}' is not mounted (missing 'fs.write.data' permission). Using embedded '{fileName}'.");
            return;
        }

        var targetPath = ResolvePath(fileName);
        if (!File.Exists(targetPath))
        {
            using var embedded = FindEmbeddedResource(fileName);
            if (embedded is null)
                return;

            EnsureParentDirectory(targetPath);
            using var file = File.Create(targetPath);
            embedded.CopyTo(file);
            return;
        }

        if (copyDefaults)
        {
            AppendMissingDefaults(targetPath, fileName);
        }
    }

    private void AppendMissingDefaults(string targetPath, string fileName)
    {
        using var embedded = FindEmbeddedResource(fileName);
        if (embedded is null)
            return;

        var format = ConfigFormatResolver.Resolve(fileName);

        using var reader = new StreamReader(embedded, leaveOpen: true);
        var embeddedValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        format.ParseInto(reader.ReadToEnd(), embeddedValues);

        var diskText = File.ReadAllText(targetPath);
        var diskValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        format.ParseInto(diskText, diskValues);

        var missingEntries = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in embeddedValues)
        {
            if (diskValues.ContainsKey(key) || IsExistingArrayElement(key, diskValues))
                continue;

            missingEntries[key] = value;
        }

        if (missingEntries.Count == 0)
            return;

        var appendedBlock = format.Serialize(missingEntries);
        var separator = diskText.EndsWith('\n') ? "\n" : "\n\n";

        File.AppendAllText(targetPath, $"{separator}# Added in plugin update\n{appendedBlock}");

        if (fileName.Equals(DefaultConfigName, StringComparison.OrdinalIgnoreCase))
            ReloadConfig();
    }

    private static bool IsExistingArrayElement(string key, Dictionary<string, string?> diskValues)
    {
        var lastColon = key.LastIndexOf(':');
        if (lastColon <= 0 || !int.TryParse(key.AsSpan(lastColon + 1), out _))
            return false;

        var firstElementKey = $"{key[..lastColon]}:0";
        return diskValues.ContainsKey(firstElementKey);
    }

    public void ReloadConfig()
    {
        var freshValues = LoadMergedValues(DefaultConfigName);

        if (_configuration is null)
            _configuration = new DictionaryConfiguration(freshValues);
        else
            _configuration.Reload(freshValues);
    }

    public T GetConfig<T>(string fileName = DefaultConfigName) where T : new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var values = LoadMergedValues(fileName);
        return Binders.Get<T>().Bind(values);
    }

    public void SaveConfig<T>(T config, string fileName = DefaultConfigName) where T : new()
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var format = ConfigFormatResolver.Resolve(fileName);

        if (!IsDataDirectoryMounted())
        {
            Logger.Warn($"WASI data directory '{DataDirectory}' is not mounted (missing 'fs.write.data' permission). Cannot save '{fileName}'.");
            return;
        }

        var path = ResolvePath(fileName);
        var merged = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        if (File.Exists(path))
        {
            format.ParseInto(File.ReadAllText(path), merged);
        }

        var patchValues = Binders.Get<T>().Serialize(config);

        PruneReplacedArrayPrefixes(merged, patchValues.Keys);

        foreach (var (key, value) in patchValues)
        {
            merged[key] = value;
        }

        EnsureParentDirectory(path);
        File.WriteAllText(path, format.Serialize(merged));

        if (fileName.Equals(DefaultConfigName, StringComparison.OrdinalIgnoreCase))
        {
            ReloadConfig();
        }
    }

    public void SaveConfig()
    {
        if (!IsDataDirectoryMounted())
        {
            Logger.Warn($"WASI data directory '{DataDirectory}' is not mounted (missing 'fs.write.data' permission). Cannot save '{DefaultConfigName}'.");
            return;
        }

        var path = ResolvePath(DefaultConfigName);
        EnsureParentDirectory(path);

        var tomlText = ConfigFormatResolver.Resolve(DefaultConfigName).Serialize(Configuration.AsEnumerable());
        File.WriteAllText(path, tomlText);
    }

    private IConfiguration BuildConfiguration(string fileName)
    {
        var values = LoadMergedValues(fileName);
        return new DictionaryConfiguration(values);
    }

    private Dictionary<string, string?> LoadMergedValues(string fileName)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var format = ConfigFormatResolver.Resolve(fileName);

        using var embedded = FindEmbeddedResource(fileName);
        if (embedded is not null)
        {
            using var reader = new StreamReader(embedded, leaveOpen: true);
            format.ParseInto(reader.ReadToEnd(), values);
        }

        var diskPath = ResolvePath(fileName);
        if (File.Exists(diskPath))
        {
            format.ParseInto(File.ReadAllText(diskPath), values);
        }

        return values;
    }

    private string ResolvePath(string fileName)
    {
        var cleanFile = fileName.Replace('\\', '/').TrimStart('/');
        var dir = DataDirectory.Replace('\\', '/').Trim('/');

        if (!string.IsNullOrEmpty(dir) && dir != "." &&
            cleanFile.StartsWith(dir + "/", StringComparison.OrdinalIgnoreCase))
        {
            cleanFile = cleanFile[(dir.Length + 1)..];
        }

        if (!string.IsNullOrEmpty(dir) && dir != ".")
        {
            if (Directory.Exists("/" + dir))
                return $"/{dir}/{cleanFile}";

            if (Directory.Exists(dir))
                return $"{dir}/{cleanFile}";
        }

        return cleanFile;
    }

    private bool IsDataDirectoryMounted() => GetMountedDataDirectory() is not null;

    private static void EnsureParentDirectory(string filePath)
    {
        var parentDir = Path.GetDirectoryName(filePath)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }
    }

    private static void PruneReplacedArrayPrefixes(
        Dictionary<string, string?> target,
        IEnumerable<string> newKeys)
    {
        var arrayPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in newKeys)
        {
            var parts = key.Split(':');
            for (int i = 0; i < parts.Length; i++)
            {
                if (int.TryParse(parts[i], out _))
                {
                    arrayPrefixes.Add(string.Join(':', parts[..i]) + ":");
                    break;
                }
            }
        }

        if (arrayPrefixes.Count == 0)
            return;

        var keysToRemove = new List<string>();
        foreach (var existingKey in target.Keys)
        {
            foreach (var prefix in arrayPrefixes)
            {
                if (existingKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    keysToRemove.Add(existingKey);
                    break;
                }
            }
        }

        foreach (var key in keysToRemove)
        {
            target.Remove(key);
        }
    }

    private Stream? FindEmbeddedResource(string fileName)
    {
        var direct = PluginAssembly.GetManifestResourceStream(fileName);
        if (direct is not null)
            return direct;

        var normalizedSuffix = "." + fileName.Replace('/', '.').Replace('\\', '.');
        foreach (var name in PluginAssembly.GetManifestResourceNames())
        {
            if (name.EndsWith(normalizedSuffix, StringComparison.OrdinalIgnoreCase))
                return PluginAssembly.GetManifestResourceStream(name);
        }

        return null;
    }
}