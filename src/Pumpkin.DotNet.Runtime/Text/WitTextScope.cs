using System.Runtime.CompilerServices;
using PluginWorld.wit.Imports.pumpkin.plugin.v0_1_0;
using Pumpkin.DotNet.Api.Logging;
using Pumpkin.DotNet.Api.Text;
using Pumpkin.DotNet.Runtime.Logging;

namespace Pumpkin.DotNet.Runtime.Text;

internal readonly ref struct WitTextScope
{
    private static ILogger logger = PumpkinLogger.CreateLogger("NativeTextScope");

    public ITextImports.TextComponent Handle
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {   
            if (field is null || field.Handle == 0)
                throw new ObjectDisposedException(
                    nameof(WitTextScope),
                    "WitTextScope resource handle is no longer valid"
                );

            return field;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public WitTextScope(Component? component)
    {
        string json = ComponentBridge.ToJson(component);
        Handle = ITextImports.TextComponent.FromJson(json);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public WitTextScope(ITextImports.TextComponent native)
    {
        Handle = native;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        try
        {
            Handle?.Dispose();
        }
        catch (ObjectDisposedException) {}
        catch (Exception ex)
        {
            logger.Error($"Failed to release handle: {ex}");
        }
    }
}