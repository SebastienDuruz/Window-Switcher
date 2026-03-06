namespace WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;

public interface IGdbusWrapper : ICommandWrapper
{
    string Execute(IReadOnlyList<string> args, int timeoutMs);
}
