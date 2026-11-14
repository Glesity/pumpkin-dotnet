using System.Runtime.CompilerServices;
using PluginWorld.wit.Imports.pumpkin.plugin.v0_1_0;
using Pumpkin.DotNet.Api.Text;
using Pumpkin.DotNet.Runtime.Text;

internal static class ComponentExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ITextImports.TextComponent ToNative(this Component? component)
    {
        string json = ComponentBridge.ToJson(component);
        
        try
        {
            return ITextImports.TextComponent.FromJson(json);
        }
        catch (PluginWorld.WitException<string> ex)
        {
            throw new InvalidOperationException($"[Pumpkin TextComponent] error from json: {ex.Value}");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Component ToComponent(this ITextImports.TextComponent witComponent)
    {
        ArgumentNullException.ThrowIfNull(witComponent);

        try
        {
            return ComponentBridge.FromJson(witComponent.ToJson());
        }
        finally
        {
            witComponent.Dispose();
        }
    }
}