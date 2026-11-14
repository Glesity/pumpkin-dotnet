using Pumpkin.DotNet.Api.Entities;
using Pumpkin.DotNet.Api.Permissions;
using Pumpkin.DotNet.Api.Text;

namespace Pumpkin.DotNet.Api.Commands;

public interface ICommandSender
{
    string Name { get; }

    CommandSenderType SenderType { get; }

    bool IsPlayer { get; }
    bool IsConsole { get; }

    bool HasPermission(string permission);
    bool HasPermissionLevel(PermissionLevel permissionLevel);

    void SendMessage(Component component);
    void SendError(Component component);

    IPlayer? AsPlayer { get; }
}