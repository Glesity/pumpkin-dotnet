namespace Pumpkin.DotNet.Api.Commands;

[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
public sealed class ArgAttribute : Attribute
{
    public string? Name { get; init; }
    public double Min { get; init; } = double.NaN;
    public double Max { get; init; } = double.NaN;
    public StringArgMode StringMode { get; init; } = StringArgMode.SingleWord;
}

public enum StringArgMode { SingleWord, Quotable, Greedy }