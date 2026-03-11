using System.Diagnostics;
using WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;
using WindowSwitcher.Lib.Models;
using Xunit;

namespace WindowSwitcher.Tests.Platform;

public sealed class ProcessExecutionTests
{
    [Fact]
    public async Task RunAsync_ReturnsOutput_WhenCommandSucceeds()
    {
        using Process process = CreateShellProcess(WriteToStdoutCommand("hello"));

        CommandResult result = await ProcessExecution.RunAsync(
            process,
            TimeSpan.FromSeconds(2)
        );

        Assert.True(result.IsSuccess);
        Assert.False(result.TimedOut);
        Assert.Equal("hello", result.StandardOutput.Trim());
        Assert.Equal(string.Empty, result.StandardError);
    }

    [Fact]
    public async Task RunAsync_ReturnsTimedOutResult_WhenCommandExceedsTimeout()
    {
        using Process process = CreateShellProcess(GetSleepCommand(seconds: 3));

        CommandResult result = await ProcessExecution.RunAsync(
            process,
            TimeSpan.FromMilliseconds(150)
        );

        Assert.True(result.TimedOut);
        Assert.Equal(-1, result.ExitCode);
    }

    [Fact]
    public async Task RunAsync_PropagatesCallerCancellation()
    {
        using Process process = CreateShellProcess(GetSleepCommand(seconds: 3));
        using var cts = new CancellationTokenSource(millisecondsDelay: 150);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ProcessExecution.RunAsync(process, TimeSpan.FromSeconds(5), cts.Token)
        );
    }

    [Fact]
    public async Task RunAsync_CapturesNonZeroExitCodeAndStdError()
    {
        using Process process = CreateShellProcess(FailWithStdErrorCommand("boom", exitCode: 7));

        CommandResult result = await ProcessExecution.RunAsync(
            process,
            TimeSpan.FromSeconds(2)
        );

        Assert.False(result.IsSuccess);
        Assert.False(result.TimedOut);
        Assert.Equal(7, result.ExitCode);
        Assert.Contains("boom", result.StandardError, StringComparison.Ordinal);
    }

    private static Process CreateShellProcess(string command)
    {
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        if (OperatingSystem.IsWindows())
        {
            startInfo.FileName = "cmd.exe";
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(command);
        }
        else
        {
            startInfo.FileName = "/bin/sh";
            startInfo.ArgumentList.Add("-lc");
            startInfo.ArgumentList.Add(command);
        }

        return new Process { StartInfo = startInfo };
    }

    private static string WriteToStdoutCommand(string value)
    {
        return OperatingSystem.IsWindows()
            ? $"echo {value}"
            : $"printf '%s' '{value}'";
    }

    private static string GetSleepCommand(int seconds)
    {
        return OperatingSystem.IsWindows()
            ? $"powershell -NoProfile -Command \"Start-Sleep -Seconds {seconds}\""
            : $"sleep {seconds}";
    }

    private static string FailWithStdErrorCommand(string error, int exitCode)
    {
        return OperatingSystem.IsWindows()
            ? $"powershell -NoProfile -Command \"[Console]::Error.WriteLine('{error}'); exit {exitCode}\""
            : $"printf '%s\\n' '{error}' 1>&2; exit {exitCode}";
    }
}
