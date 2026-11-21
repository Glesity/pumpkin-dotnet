using Pumpkin.DotNet.Api.Commands;
using Pumpkin.DotNet.Api.Events;
using Pumpkin.DotNet.Api.Events.Player;
using Pumpkin.DotNet.Api.Logging;
using Pumpkin.DotNet.Api.Permissions;
using Pumpkin.DotNet.Api.Plugins;
using Pumpkin.DotNet.Api.Scheduling;
using Pumpkin.DotNet.Api.Tasks;
using Pumpkin.DotNet.Api.Server;
using Pumpkin.DotNet.Api.Text;

namespace Pumpkin.DotNet.Sample;

public sealed class SamplePlugin : PumpkinPlugin
{
    private IScheduledTask? _tipTask;

    public override void OnLoad()
    {
        Logger.Info("SamplePlugin loaded.");

        _tipTask = PumpkinTask.RunRepeatingTicks(200, 1200, () =>
        {
            int online = Server.Players.Count();
            if (online > 0)
            {
                Logger.Info($"Players online: {online}");
            }
        });
    }

    public override void OnUnload()
    {
        _tipTask?.Cancel();
        Logger.Info("SamplePlugin unloaded.");
    }

    [EventHandler(Priority = EventPriority.Normal)]
    public void OnPlayerJoin(PlayerJoinEvent evt, ILogger logger)
    {
        logger.Info($"Player {evt.Player.Name} joined the server.");
        evt.Player.SendMessage(
            TextComponent.Text($"Welcome to Pumpkin, {evt.Player.Name}!").WithColor(TextColor.Green)
        );
    }

    [EventHandler(Priority = EventPriority.Low, IsModifying = true)]
    public void OnPlayerChat(PlayerChatEvent evt)
    {
        if (evt.Message.Equals("!ping", StringComparison.OrdinalIgnoreCase))
        {
            evt.Player.SendMessage(TextComponent.Text("Pong!").WithColor(TextColor.Aqua));
            evt.IsCancelled = true;
        }
    }
}

[Command("hello", "hi", Description = "Send greeting and announcement messages")]
public static class HelloCommands
{
    [Execute]
    public static void Root(ICommandContext ctx)
    {
        ctx.Sender.SendMessage(
            TextComponent.Text("Hello from Pumpkin.NET! Try: /hello countdown [seconds]")
                .WithColor(TextColor.Yellow)
        );
    }

    [Subcommand("countdown", PermissionLevel = PermissionLevel.Two)]
    [Execute]
    public static int Countdown(
        ICommandContext ctx,
        IServer server,
        [Arg(Name = "seconds", Min = 1, Max = 10)] int seconds = 3)
    {
        PumpkinTask.Spawn(async ct => await RunCountdownAsync(server, seconds, ct));
        ctx.Sender.SendMessage(TextComponent.Text($"Started {seconds}s countdown.").WithColor(TextColor.Aqua));
        return 1;
    }

    private static async ValueTask RunCountdownAsync(IServer server, int seconds, CancellationToken ct)
    {
        for (int i = seconds; i > 0; i--)
        {
            foreach (var player in server.Players.AsEnumerable())
            {
                player.SendMessage(TextComponent.Text($"[Countdown] {i}...").WithColor(TextColor.Yellow));
            }

            await PumpkinTask.DelayTicksAsync(20, ct);
        }

        foreach (var player in server.Players.AsEnumerable())
        {
            player.SendMessage(TextComponent.Text("[Countdown] Go!").WithColor(TextColor.Green));
        }
    }
}