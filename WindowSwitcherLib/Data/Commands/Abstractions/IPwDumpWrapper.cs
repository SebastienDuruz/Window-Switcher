namespace WindowSwitcherLib.Data.Commands;

public interface IPwDumpWrapper : ICommandWrapper
{
    string Execute(int timeoutMs);
}
