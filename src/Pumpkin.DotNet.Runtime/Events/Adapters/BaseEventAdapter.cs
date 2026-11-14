using PluginWorld.wit.Imports.pumpkin.plugin.v0_1_0;
using Pumpkin.DotNet.Api.Events;

namespace Pumpkin.DotNet.Runtime.Events.Adapters;

internal abstract class BaseEventAdapter<TApiEvent> : IEventAdapter 
    where TApiEvent : class, IEvent
{
    protected abstract ushort ExpectedTag { get; }

    public IEvent Unpack(IEventImports.Event rawEvent)
    {
        if (rawEvent.Tag != ExpectedTag)
        {
            throw new ArgumentException(
                $"Expected native event tag '{ExpectedTag}', but received '{rawEvent.Tag}'.",
                nameof(rawEvent));
        }

        return UnpackCore(rawEvent);
    }

    public void PackBack(IEvent apiEvent, ref IEventImports.Event rawEvent)
    {
        if (apiEvent is not TApiEvent typedApi)
        {
            throw new ArgumentException(
                $"Expected event of type '{typeof(TApiEvent).Name}', but received '{apiEvent?.GetType().Name ?? "null"}'.",
                nameof(apiEvent));
        }

        if (rawEvent.Tag != ExpectedTag)
        {
            throw new ArgumentException(
                $"Cannot pack back into mismatched event tag '{rawEvent.Tag}'. Expected '{ExpectedTag}'.",
                nameof(rawEvent));
        }

        PackBackCore(typedApi, ref rawEvent);
    }

    protected abstract TApiEvent UnpackCore(IEventImports.Event rawEvent);
    protected abstract void PackBackCore(TApiEvent apiEvent, ref IEventImports.Event rawEvent);
}