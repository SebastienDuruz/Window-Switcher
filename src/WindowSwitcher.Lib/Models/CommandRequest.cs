namespace WindowSwitcherLib.Models;

/// <summary>
/// Describes a process execution request without exposing OS-specific types.
/// </summary>
public sealed record CommandRequest
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    private CommandRequest(
        string? executablePath,
        IReadOnlyList<string> arguments,
        string? shellCommand
    )
    {
        ExecutablePath = executablePath;
        Arguments = arguments;
        ShellCommand = shellCommand;
    }

    /// <summary>
    /// Executable path to launch when command is executed directly.
    /// </summary>
    public string? ExecutablePath { get; }

    /// <summary>
    /// Structured process arguments used with <see cref="ExecutablePath"/>.
    /// </summary>
    public IReadOnlyList<string> Arguments { get; }

    /// <summary>
    /// Optional shell expression that must be executed through platform shell.
    /// </summary>
    public string? ShellCommand { get; }

    /// <summary>
    /// Per-command timeout. Defaults to 5 seconds.
    /// </summary>
    public TimeSpan Timeout { get; init; } = DefaultTimeout;

    /// <summary>
    /// Optional working directory used for process startup.
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Optional environment variable overrides.
    /// </summary>
    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Creates a request for a direct executable invocation.
    /// </summary>
    public static CommandRequest ForExecutable(string executablePath, params string[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);

        return new CommandRequest(executablePath, arguments.ToArray(), shellCommand: null);
    }

    /// <summary>
    /// Creates a request executed through platform shell adapter.
    /// </summary>
    public static CommandRequest ForShell(string shellCommand)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shellCommand);

        return new CommandRequest(
            executablePath: null,
            arguments: Array.Empty<string>(),
            shellCommand
        );
    }
}
