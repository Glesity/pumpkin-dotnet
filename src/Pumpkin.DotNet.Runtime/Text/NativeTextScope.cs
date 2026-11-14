namespace Pumpkin.DotNet.Runtime.Text;

using System;
using System.Runtime.CompilerServices;
using PluginWorld.wit.Imports.pumpkin.plugin.v0_1_0;
using Pumpkin.DotNet.Api.Text;

internal readonly ref struct NativeTextScope
{
    public ITextImports.TextComponent Native { get; }

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
        Native?.Dispose();
    }
}