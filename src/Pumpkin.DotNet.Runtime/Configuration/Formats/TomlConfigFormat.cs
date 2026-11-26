using System.Globalization;
using System.Text;
using Pumpkin.DotNet.Api.Configuration;

namespace Pumpkin.DotNet.Runtime.Configuration.Formats;

internal class TomlConfigFormat : IConfigFormat
{
    public void ParseInto(string configText, Dictionary<string, string?> target)
    {
        var currentPrefix = string.Empty;
        var arrayCounters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in configText.AsSpan().EnumerateLines())
        {
            var line = StripComment(rawLine).Trim();
            if (line.IsEmpty)
                continue;

            // [[array_of_tables]]
            if (line.StartsWith("[[") && line.EndsWith("]]"))
            {
                var tableName = NormalizeSectionName(line[2..^2]);
                var index = arrayCounters.TryGetValue(tableName, out var count) ? count : 0;
                arrayCounters[tableName] = index + 1;
                currentPrefix = $"{tableName}:{index}";
                continue;
            }

            // [table]
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                currentPrefix = NormalizeSectionName(line[1..^1]);
                continue;
            }

            var eqIdx = line.IndexOf('=');
            if (eqIdx <= 0)
                continue;

            var key = line[..eqIdx].Trim().Trim('"').ToString();
            var valueSpan = line[(eqIdx + 1)..].Trim();
            var fullKey = string.IsNullOrEmpty(currentPrefix) ? key : $"{currentPrefix}:{key}";

            if (valueSpan.StartsWith("[") && valueSpan.EndsWith("]"))
                ParseInlineArray(valueSpan[1..^1], fullKey, target);
            else
                target[fullKey] = ParseScalar(valueSpan);
        }
    }

    public string Serialize(IEnumerable<KeyValuePair<string, string?>> entries)
    {
        var sb = new StringBuilder();
        var rootKeys = new List<(string Key, string RawValue)>();
        var sections = new SortedDictionary<string, List<(string Key, string RawValue)>>(StringComparer.OrdinalIgnoreCase);
        var scalarArrays = new SortedDictionary<string, SortedDictionary<int, string>>(StringComparer.OrdinalIgnoreCase);
        var arrayTables = new SortedDictionary<string, SortedDictionary<int, List<(string Key, string RawValue)>>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (fullKey, rawValue) in entries)
        {
            if (rawValue is null)
                continue;

            var parts = fullKey.Split(':');

            if (parts.Length >= 2 && int.TryParse(parts[^1], out var scalarIdx))
            {
                var arrayFullPath = string.Join(':', parts[..^1]);
                if (!scalarArrays.TryGetValue(arrayFullPath, out var items))
                    scalarArrays[arrayFullPath] = items = new();
                items[scalarIdx] = rawValue;
                continue;
            }

            if (parts.Length >= 3 && int.TryParse(parts[^2], out var tableIdx))
            {
                var tablePath = string.Join('.', parts[..^2]);
                if (!arrayTables.TryGetValue(tablePath, out var items))
                    arrayTables[tablePath] = items = new();
                if (!items.TryGetValue(tableIdx, out var props))
                    items[tableIdx] = props = [];
                props.Add((parts[^1], rawValue));
                continue;
            }

            if (parts.Length == 1)
            {
                rootKeys.Add((parts[0], rawValue));
                continue;
            }

            var sectionPath = string.Join('.', parts[..^1]);
            if (!sections.TryGetValue(sectionPath, out var secProps))
                sections[sectionPath] = secProps = [];
            secProps.Add((parts[^1], rawValue));
        }

        foreach (var (arrayFullPath, items) in scalarArrays)
        {
            var formattedArray = "[" + string.Join(", ", items.Values.Select(FormatScalar)) + "]";
            var lastColon = arrayFullPath.LastIndexOf(':');
            if (lastColon < 0)
            {
                rootKeys.Add((arrayFullPath, "\0RAW:" + formattedArray));
            }
            else
            {
                var sectionPath = arrayFullPath[..lastColon].Replace(':', '.');
                var keyName = arrayFullPath[(lastColon + 1)..];
                if (!sections.TryGetValue(sectionPath, out var secProps))
                    sections[sectionPath] = secProps = [];
                secProps.Add((keyName, "\0RAW:" + formattedArray));
            }
        }

        foreach (var (key, val) in rootKeys)
            sb.Append(key).Append(" = ").AppendLine(FormatOrRaw(val));

        foreach (var (section, props) in sections)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.Append('[').Append(section).AppendLine("]");
            foreach (var (key, val) in props)
                sb.Append(key).Append(" = ").AppendLine(FormatOrRaw(val));
        }

        foreach (var (tableName, items) in arrayTables)
        {
            foreach (var (_, props) in items)
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.Append("[[").Append(tableName).AppendLine("]]");
                foreach (var (key, val) in props)
                    sb.Append(key).Append(" = ").AppendLine(FormatOrRaw(val));
            }
        }

        return sb.ToString();
    }

    private static string FormatScalar(string raw)
    {
        if (bool.TryParse(raw, out var b))
            return b ? "true" : "false";

        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ||
            double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            return raw;

        var escaped = raw
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r");

        return $"\"{escaped}\"";
    }

    private static void ParseInlineArray(ReadOnlySpan<char> inner, string prefix, Dictionary<string, string?> target)
    {
        int index = 0, start = 0;
        bool inQuotes = false;

        for (int i = 0; i <= inner.Length; i++)
        {
            if (i < inner.Length && inner[i] == '"' && (i == 0 || inner[i - 1] != '\\'))
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (i == inner.Length || (!inQuotes && inner[i] == ','))
            {
                var item = inner[start..i].Trim();
                if (!item.IsEmpty)
                    target[$"{prefix}:{index++}"] = ParseScalar(item);
                start = i + 1;
            }
        }
    }

    private static string ParseScalar(ReadOnlySpan<char> value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            return value[1..^1].ToString().Replace("\\\"", "\"").Replace("\\\\", "\\").Replace("\\n", "\n").Replace("\\r", "\r");

        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
            return value[1..^1].ToString();

        return value.ToString();
    }

    private static string NormalizeSectionName(ReadOnlySpan<char> section)
    {
        var sb = new StringBuilder(section.Length);
        foreach (var ch in section.Trim())
        {
            if (ch == '.') sb.Append(':');
            else if (!char.IsWhiteSpace(ch) && ch != '"') sb.Append(ch);
        }
        return sb.ToString();
    }

    private static ReadOnlySpan<char> StripComment(ReadOnlySpan<char> line)
    {
        bool inDouble = false, inSingle = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"' && !inSingle && (i == 0 || line[i - 1] != '\\')) inDouble = !inDouble;
            else if (c == '\'' && !inDouble) inSingle = !inSingle;
            else if (c == '#' && !inDouble && !inSingle) return line[..i];
        }
        return line;
    }

    private static string FormatOrRaw(string val) =>
        val.StartsWith("\0RAW:", StringComparison.Ordinal) ? val[5..] : FormatScalar(val);
}