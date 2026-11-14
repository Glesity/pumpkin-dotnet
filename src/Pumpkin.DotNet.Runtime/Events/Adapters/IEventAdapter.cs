using PluginWorld.wit.Imports.pumpkin.plugin.v0_1_0;
using Pumpkin.DotNet.Api.Events;

namespace Pumpkin.DotNet.Runtime.Events.Adapters;
internal interface IEventAdapter
{
    /// <summary>
    /// Unpacks a native WIT event into a high-level C# IEvent.
    /// </summary>
    public IEvent Unpack(IEventImports.Event rawEvent);

    /// <summary>
    /// Writes modified data (cancellation and changed fields) back to the WIT event.
    /// </summary>
    public void PackBack(IEvent apiEvent, ref IEventImports.Event rawEvent);
}