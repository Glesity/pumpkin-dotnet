namespace Pumpkin.DotNet.Api.Commands;

[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
public sealed class SuggestAttribute : Attribute
{
    public string MethodName { get; }
    public SuggestAttribute(string methodName) => MethodName = methodName;
}