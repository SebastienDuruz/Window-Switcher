namespace WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;

public interface IPwDumpWrapper : ICommandWrapper
{
    string Execute(int timeoutMs);
}
