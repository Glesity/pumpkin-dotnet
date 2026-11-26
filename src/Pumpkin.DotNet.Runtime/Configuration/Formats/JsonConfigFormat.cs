using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Pumpkin.DotNet.Api.Configuration;

namespace Pumpkin.DotNet.Runtime.Configuration.Formats;

internal sealed class JsonConfigFormat : IConfigFormat
{
    private static readonly JsonReaderOptions ReaderOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public void ParseInto(string content, Dictionary<string, string?> target)
    {
        if (string.IsNullOrWhiteSpace(content))
            return;

        var bytes = Encoding.UTF8.GetBytes(content);
        var reader = new Utf8JsonReader(bytes, ReaderOptions);

        ReadElement(ref reader, prefix: null, target);
    }

    private static void ReadElement(ref Utf8JsonReader reader, string? prefix, Dictionary<string, string?> target)
    {
        if (!reader.Read())
            return;

        switch (reader.TokenType)
        {
            case JsonTokenType.StartObject:
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType != JsonTokenType.PropertyName)
                        continue;

                    var propName = reader.GetString()!;
                    var nextKey = string.IsNullOrEmpty(prefix) ? propName : $"{prefix}:{propName}";
                    ReadElement(ref reader, nextKey, target);
                }
                break;

            case JsonTokenType.StartArray:
                int index = 0;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    var itemKey = $"{prefix}:{index++}";
                    ReadCurrentTokenValue(ref reader, itemKey, target);
                }
                break;

            default:
                if (!string.IsNullOrEmpty(prefix))
                    ReadCurrentTokenValue(ref reader, prefix, target);
                break;
        }
    }

    private static void ReadCurrentTokenValue(ref Utf8JsonReader reader, string key, Dictionary<string, string?> target)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.StartObject:
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        var propName = reader.GetString()!;
                        ReadElement(ref reader, $"{key}:{propName}", target);
                    }
                }
                break;

            case JsonTokenType.StartArray:
                int idx = 0;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    ReadCurrentTokenValue(ref reader, $"{key}:{idx++}", target);
                break;

            case JsonTokenType.String:
                target[key] = reader.GetString();
                break;

            case JsonTokenType.Number:
                target[key] = reader.TryGetInt64(out var l)
                    ? l.ToString(CultureInfo.InvariantCulture)
                    : reader.GetDouble().ToString(CultureInfo.InvariantCulture);
                break;

            case JsonTokenType.True:
                target[key] = "true";
                break;

            case JsonTokenType.False:
                target[key] = "false";
                break;

            case JsonTokenType.Null:
                target[key] = null;
                break;
        }
    }

    public string Serialize(IEnumerable<KeyValuePair<string, string?>> entries)
    {
        var root = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in entries)
        {
            if (value is null)
                continue;

            InsertNode(root, key.Split(':'), 0, value);
        }

        var buffer = new ArrayBufferWriter<byte>(512);
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            WriteNode(writer, root);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void InsertNode(Dictionary<string, object?> current, string[] parts, int idx, string rawValue)
    {
        var part = parts[idx];
        if (idx == parts.Length - 1)
        {
            current[part] = rawValue;
            return;
        }

        if (!current.TryGetValue(part, out var next) || next is not Dictionary<string, object?> nextDict)
        {
            nextDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            current[part] = nextDict;
        }

        InsertNode(nextDict, parts, idx + 1, rawValue);
    }

    private static void WriteNode(Utf8JsonWriter writer, Dictionary<string, object?> dict)
    {
        if (dict.Count > 0 && IsSequentialArray(dict, out var items))
        {
            writer.WriteStartArray();
            foreach (var item in items)
                WriteValue(writer, item);
            writer.WriteEndArray();
            return;
        }

        writer.WriteStartObject();
        foreach (var (k, v) in dict)
        {
            writer.WritePropertyName(k);
            WriteValue(writer, v);
        }
        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, object? val)
    {
        if (val is Dictionary<string, object?> subDict)
        {
            WriteNode(writer, subDict);
            return;
        }

        var str = val?.ToString() ?? string.Empty;
        if (bool.TryParse(str, out var b))
            writer.WriteBooleanValue(b);
        else if (long.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
            writer.WriteNumberValue(l);
        else if (double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            writer.WriteNumberValue(d);
        else
            writer.WriteStringValue(str);
    }

    private static bool IsSequentialArray(Dictionary<string, object?> dict, out List<object?> ordered)
    {
        ordered = new List<object?>(dict.Count);
        for (int i = 0; i < dict.Count; i++)
        {
            if (!dict.TryGetValue(i.ToString(CultureInfo.InvariantCulture), out var item))
                return false;
            ordered.Add(item);
        }
        return true;
    }
}