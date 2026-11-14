namespace Pumpkin.DotNet.Api.Commands;

internal interface IArgumentNode
{
    Type ValueType { get; }
    ArgAttribute? Constraints { get; }
}