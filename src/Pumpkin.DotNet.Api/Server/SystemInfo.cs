namespace Pumpkin.DotNet.Api.Server;

public readonly record struct SystemInfo(
    uint? CpuCount,
    ulong? TotalMemory,
    ulong? UsedMemory,
    string? OsName,
    string? OsVersion,
    string? PumkinVersion
);