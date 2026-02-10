namespace WindowSwitcherLib.Data.Commands;

public interface IGdbusWrapper : ICommandWrapper
{
    string Execute(IReadOnlyList<string> args, int timeoutMs);
}
