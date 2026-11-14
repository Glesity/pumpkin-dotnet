using PluginWorld.wit.Imports.pumpkin.plugin.v0_1_0;
using Pumpkin.DotNet.Api.Server;

namespace Pumpkin.DotNet.Runtime.Interop;

internal static class SystemInfoExtensions
{
    public static SystemInfo ToApi(this IServerImports.SysInfo sysInfo)
    {
        return new SystemInfo(
            sysInfo.cpuCount,
            sysInfo.totalMemory,
            sysInfo.usedMemory,
            sysInfo.osName,
            sysInfo.osVersion,
            sysInfo.pumpkinVersion
        );
    }
}