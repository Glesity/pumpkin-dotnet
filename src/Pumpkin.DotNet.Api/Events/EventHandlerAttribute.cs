namespace Pumpkin.DotNet.Api.Events;

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public class EventHandlerAttribute : Attribute
{
    public EventPriority Priority { get; set; } = EventPriority.Normal;
    public bool IsModifying { get; set; } = true;
}