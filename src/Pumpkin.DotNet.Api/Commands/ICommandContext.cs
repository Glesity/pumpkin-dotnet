using System.Diagnostics.CodeAnalysis;
using Pumpkin.DotNet.Api.Entities;
using Pumpkin.DotNet.Api.Text;

namespace Pumpkin.DotNet.Api.Commands;

public interface ICommandContext
{
    ICommandSender Sender { get; }
    bool IsPlayer => Sender.IsPlayer;
    IPlayer? AsPlayer => Sender.AsPlayer;
    bool Has(string name);
    T Get<T>(string name);
    T GetOrDefault<T>(string name, T defaultValue);
    bool TryGet<T>(string name, [MaybeNullWhen(false)] out T value);
    int Result { get; set; }
    void Fail(string message) => Fail(Component.Text(message, TextColor.Red));
    void Fail(Component message);
}