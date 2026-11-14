using System.Buffers.Binary;
using PluginWorld.wit.Imports.pumpkin.plugin.v0_1_0;

namespace Pumpkin.DotNet.Runtime.Interop;

internal static class UuidExtensions
{
    public static IUuidImports.Uuid ToWit(this Guid guid)
    {
        Span<byte> bytes = stackalloc byte[16];
        guid.TryWriteBytes(bytes, bigEndian: true, out _);

        ulong high = BinaryPrimitives.ReadUInt64BigEndian(bytes[..8]);
        ulong low = BinaryPrimitives.ReadUInt64BigEndian(bytes[8..]);

        return new IUuidImports.Uuid(high, low);
    }

    public static Guid ToGuid(this IUuidImports.Uuid uuid)
    {
        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteUInt64BigEndian(bytes[..8], uuid.high);
        BinaryPrimitives.WriteUInt64BigEndian(bytes[8..], uuid.low);

        return new Guid(bytes, bigEndian: true);
    }
}