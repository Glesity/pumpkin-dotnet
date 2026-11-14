using Pumpkin.DotNet.Api.Text;

namespace Pumpkin.DotNet.Api.Entities;

public interface IPlayer
{
    Guid Id { get; }
    string Name { get; } 

    void SendMessage(Component message);
}