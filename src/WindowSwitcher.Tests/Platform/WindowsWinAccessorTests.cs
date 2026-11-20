using System.Diagnostics;
using System.Runtime.Versioning;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors;
using WindowSwitcher.Lib.Models;
using Xunit;

namespace WindowSwitcher.Tests.Platform;

/// <summary>
/// Runs the Win32 window enumeration, rename and process-name lookups against a real window
/// owned by another process (the accessor ignores its own process). Skipped outside Windows;
/// the Windows CI runner executes them.
/// </summary>
[Trait("Category", "Integration")]
[SupportedOSPlatform("windows")]
public sealed class WindowsWinAccessorTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task WindowOperations_ReturnFalse_ForInvalidWindowId()
    {
        RequireWindows();
        var sut = new WindowsWinAccessor();

        Assert.False(await sut.TryActivateWindowAsync("invalid"));
        Assert.False(await sut.TryRenameWindowAsync("invalid", "New title"));
        Assert.False(await sut.TryActivateWindowAsync("0"));
        Assert.False(await sut.TryRenameWindowAsync("0", "New title"));
    }

    [Fact]
    public async Task Enumeration_ListsAnotherProcessWindowWithItsProcessName()
    {
        RequireWindows();
        using var helper = HelperWindowProcess.Start();
        var sut = new WindowsWinAccessor();

        WindowConfig window = await WaitForWindowAsync(sut, helper.Title);

        Assert.Equal("powershell", window.ProcessName, ignoreCase: true);
        Assert.True(long.TryParse(window.WindowId, out long handle) && handle != 0);
    }

    [Fact]
    public async Task Rename_ChangesTheTitleSeenByEnumeration()
    {
        RequireWindows();
        using var helper = HelperWindowProcess.Start();
        var sut = new WindowsWinAccessor();
        WindowConfig window = await WaitForWindowAsync(sut, helper.Title);
        string renamed = helper.Title + " renamed é";

        Assert.True(await sut.TryRenameWindowAsync(window.WindowId, renamed));

        WindowConfig afterRename = await WaitForWindowAsync(sut, renamed);
        Assert.Equal(window.WindowId, afterRename.WindowId);
    }

    [Fact]
    public async Task Activate_AcceptsAnEnumeratedWindow()
    {
        RequireWindows();
        using var helper = HelperWindowProcess.Start();
        var sut = new WindowsWinAccessor();
        WindowConfig window = await WaitForWindowAsync(sut, helper.Title);

        // Windows may refuse to move the foreground; the call itself must succeed and not hang.
        Assert.True(await sut.TryActivateWindowAsync(window.WindowId));
    }

    [Fact]
    public void ProcessNameReader_ReadsTheCurrentProcess()
    {
        RequireWindows();
        using Process current = Process.GetCurrentProcess();

        Assert.Equal(current.ProcessName, WindowsProcessNameReader.Read((uint)current.Id));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(0xFFFFFFF0u)] // process identifiers are multiples of four below this value
    public void ProcessNameReader_ReturnsEmpty_ForMissingProcesses(uint processId)
    {
        RequireWindows();

        Assert.Equal(string.Empty, WindowsProcessNameReader.Read(processId));
    }

    private static void RequireWindows()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Requires Windows.");
    }

    private static async Task<WindowConfig> WaitForWindowAsync(WindowsWinAccessor sut, string title)
    {
        using var timeout = new CancellationTokenSource(WaitTimeout);
        while (true)
        {
            IReadOnlyCollection<WindowConfig>? windows = await sut.TryGetWindowsAsync(
                timeout.Token
            );
            WindowConfig? match = windows?.FirstOrDefault(window => window.WindowTitle == title);
            if (match is not null)
                return match;
            await Task.Delay(200, timeout.Token);
        }
    }

    /// <summary>
    /// A Windows Forms window shown by a separate PowerShell process with a unique title.
    /// </summary>
    private sealed class HelperWindowProcess : IDisposable
    {
        private readonly Process _process;

        private HelperWindowProcess(Process process, string title)
        {
            _process = process;
            Title = title;
        }

        public string Title { get; }

        public static HelperWindowProcess Start()
        {
            string title = "WindowSwitcher test " + Guid.NewGuid().ToString("N");
            var startInfo = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(
                "Add-Type -AssemblyName System.Windows.Forms; "
                    + "$form = New-Object System.Windows.Forms.Form; "
                    + $"$form.Text = '{title}'; "
                    + "$form.ShowInTaskbar = $true; "
                    + "[System.Windows.Forms.Application]::Run($form)"
            );
            Process process =
                Process.Start(startInfo)
                ?? throw new InvalidOperationException("PowerShell could not be started.");
            return new HelperWindowProcess(process, title);
        }

        public void Dispose()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(5000);
                }
            }
            catch (InvalidOperationException)
            {
                // The helper already exited; there is nothing left to stop.
            }
            finally
            {
                _process.Dispose();
            }
        }
    }
}
