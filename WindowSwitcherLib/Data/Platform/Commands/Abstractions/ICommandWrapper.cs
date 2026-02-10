namespace WindowSwitcherLib.Data.Platform.Commands.Abstractions;

public interface ICommandWrapper
{
    public string Execute(string args);
}