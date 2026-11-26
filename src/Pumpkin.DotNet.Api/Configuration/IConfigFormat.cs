namespace Pumpkin.DotNet.Api.Configuration;

public interface IConfigFormat
{
    void ParseInto(string context, Dictionary<string, string?> target);

    string Serialize(IEnumerable<KeyValuePair<string, string?>> entries);
}