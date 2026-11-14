namespace Pumpkin.DotNet.Runtime.Commands;

using System;
using System.Collections.Generic;
using PluginWorld.wit.Imports.pumpkin.plugin.v0_1_0;
using Pumpkin.DotNet.Api.Commands;
using Pumpkin.DotNet.Api.Entities;
using Pumpkin.DotNet.Api.Math;
using Pumpkin.DotNet.Api.Text;
using Pumpkin.DotNet.Runtime.Entities.Players;
using Pumpkin.DotNet.Runtime.Text;

internal static class WitArgumentMapper
{
    public static ICommandImports.ArgumentType MapType(Type paramType, ArgAttribute? attr)
    {
        var type = Nullable.GetUnderlyingType(paramType) ?? paramType;

        if (type == typeof(bool))
            return ICommandImports.ArgumentType.@bool();

        if (type == typeof(int))
        {
            int? min = attr is not null && !double.IsNaN(attr.Min) ? (int)attr.Min : null;
            int? max = attr is not null && !double.IsNaN(attr.Max) ? (int)attr.Max : null;
            return ICommandImports.ArgumentType.Integer((min, max));
        }

        if (type == typeof(long))
        {
            long? min = attr is not null && !double.IsNaN(attr.Min) ? (long)attr.Min : null;
            long? max = attr is not null && !double.IsNaN(attr.Max) ? (long)attr.Max : null;
            return ICommandImports.ArgumentType.@long((min, max));
        }

        if (type == typeof(float))
        {
            float? min = attr is not null && !double.IsNaN(attr.Min) ? (float)attr.Min : null;
            float? max = attr is not null && !double.IsNaN(attr.Max) ? (float)attr.Max : null;
            return ICommandImports.ArgumentType.@float((min, max));
        }

        if (type == typeof(double))
        {
            double? min = attr is not null && !double.IsNaN(attr.Min) ? attr.Min : null;
            double? max = attr is not null && !double.IsNaN(attr.Max) ? attr.Max : null;
            return ICommandImports.ArgumentType.@double((min, max));
        }

        if (type == typeof(string))
        {
            var mode = attr?.StringMode switch
            {
                StringArgMode.Quotable => ICommandImports.StringType.QUOTABLE,
                StringArgMode.Greedy   => ICommandImports.StringType.GREEDY,
                _                      => ICommandImports.StringType.SINGLE_WORD
            };
            return ICommandImports.ArgumentType.@string(mode);
        }

        if (type == typeof(IPlayer))
            return ICommandImports.ArgumentType.Entity();

        if (type == typeof(IReadOnlyList<IPlayer>) || type == typeof(IPlayer[]))
            return ICommandImports.ArgumentType.Players();

        if (type == typeof(Vector3))
            return ICommandImports.ArgumentType.Position3d();

        if (type == typeof(Component))
            return ICommandImports.ArgumentType.Component();

        throw new NotSupportedException($"Parameter type '{type.Name}' is not supported by the command system.");
    }

    public static object? ExtractValue(ICommandImports.Arg arg) => arg.Tag switch
    {
        ICommandImports.Arg.Tags.Simple           => arg.AsSimple,
        ICommandImports.Arg.Tags.Msg              => arg.AsMsg,
        ICommandImports.Arg.Tags.@bool            => arg.AsBool,
        ICommandImports.Arg.Tags.Time             => arg.AsTime,
        ICommandImports.Arg.Tags.Item             => arg.AsItem,
        ICommandImports.Arg.Tags.ItemPredicate    => arg.AsItemPredicate,
        ICommandImports.Arg.Tags.Block            => arg.AsBlock,
        ICommandImports.Arg.Tags.BlockPredicate   => arg.AsBlockPredicate,
        ICommandImports.Arg.Tags.ResourceLocation => arg.AsResourceLocation,
        ICommandImports.Arg.Tags.Particle         => arg.AsParticle,
        ICommandImports.Arg.Tags.DamageType       => arg.AsDamageType,
        ICommandImports.Arg.Tags.Effect           => arg.AsEffect,
        ICommandImports.Arg.Tags.Enchantment      => arg.AsEnchantment,
        ICommandImports.Arg.Tags.Advancement      => arg.AsAdvancement,
        ICommandImports.Arg.Tags.Num              => ExtractNumber(arg.AsNum),
        ICommandImports.Arg.Tags.Pos3d            => new Vector3(arg.AsPos3d.Item1, arg.AsPos3d.Item2, arg.AsPos3d.Item3),
        ICommandImports.Arg.Tags.BlockPos         => new Vector3(arg.AsBlockPos.x, arg.AsBlockPos.y, arg.AsBlockPos.z),
        ICommandImports.Arg.Tags.Players          => ExtractPlayers(arg.AsPlayers),
        ICommandImports.Arg.Tags.TextComponent    => ExtractTextComponent(arg.AsTextComponent),
        _                                         => null
    };

    private static object? ExtractNumber(PluginWorld.Result<ICommandImports.Number, ICommandImports.NotInBounds> numResult)
    {
        if (!numResult.IsOk)
            return null;

        var num = numResult.AsOk;
        return num.Tag switch
        {
            ICommandImports.Number.Tags.Int32   => num.AsInt32,
            ICommandImports.Number.Tags.Int64   => num.AsInt64,
            ICommandImports.Number.Tags.Float32 => num.AsFloat32,
            ICommandImports.Number.Tags.Float64 => num.AsFloat64,
            _                                   => null
        };
    }

    private static IReadOnlyList<IPlayer> ExtractPlayers(List<IPlayerImports.Player> nativePlayers)
    {
        var list = new List<IPlayer>(nativePlayers.Count);
        foreach (var p in nativePlayers)
        {
            list.Add(PlayerCache.GetBorrowed(p));
        }
        return list;
    }

    private static Component ExtractTextComponent(ITextImports.TextComponent tc)
    {
        using (tc)
        {
            return ComponentBridge.FromJson(tc.ToJson());
        }
    }
}