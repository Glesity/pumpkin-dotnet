namespace Pumpkin.DotNet.Api.Commands;

public interface ICommandRegistrar
{
    CommandBuilder BuildCommand();
}