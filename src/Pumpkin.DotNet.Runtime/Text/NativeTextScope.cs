namespace Pumpkin.DotNet.Runtime.Text;

using System;
using System.Runtime.CompilerServices;
using PluginWorld.wit.Imports.pumpkin.plugin.v0_1_0;
using Pumpkin.DotNet.Api.Logging;
using Pumpkin.DotNet.Api.Text;
using Pumpkin.DotNet.Runtime.Logging;

internal readonly ref struct NativeTextScope
{
    private static ILogger logger = PumpkinLogger.CreateLogger("NativeTextScope");

    public ITextImports.TextComponent Native
    {
        get
        {   
            if (field is null || field.Handle == 0)
                throw new ObjectDisposedException(
                    nameof(NativeTextScope),
                    "NativeTextScope resource handle is no longer valid"
                );

            return field;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public NativeTextScope(Component? component)
    {
        string json = ComponentBridge.ToJson(component);
        Native = ITextImports.TextComponent.FromJson(json);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public NativeTextScope(ITextImports.TextComponent native)
    {
        Native = native;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        try
        {
            Native?.Dispose();
        }
        catch (ObjectDisposedException) {}
        catch (Exception ex)
        {
            logger.Error($"Failed to release handle: {ex}");
        }
    }
}