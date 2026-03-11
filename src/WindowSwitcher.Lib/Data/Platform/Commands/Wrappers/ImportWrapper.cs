using System.Diagnostics;
using WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Commands.Dependencies;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.Commands.Wrappers;

public class ImportWrapper() : CommandBase("import"), ICommandWrapper
{
    public MemoryStream? CaptureScreenshotStream(string client, ScreenshotRequest request)
    {
        return CaptureScreenshotStreamAsync(client, request, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    public MemoryStream? CaptureScreenshotStream(string client)
    {
        return CaptureScreenshotStream(client, new ScreenshotRequest());
    }

    public async Task<MemoryStream?> CaptureScreenshotStreamAsync(
        string client,
        ScreenshotRequest request,
        CancellationToken cancellationToken
    )
    {
        if (!LinuxDependencies.IsImportAvailable)
        {
            LinuxDependencies.ReportMissingOnce("import");
            return null;
        }

        int quality = 80;

        int timeoutMs = request.TimeoutMs;
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(timeoutMs);
        CancellationToken linkedToken = linkedCts.Token;

        using var process = CreateProcess();
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.ArgumentList.Clear();
        process.StartInfo.ArgumentList.Add("-window");
        process.StartInfo.ArgumentList.Add(client);
        AppendResizeArguments(process.StartInfo.ArgumentList, request);
        process.StartInfo.ArgumentList.Add("-strip");
        process.StartInfo.ArgumentList.Add("-quality");
        process.StartInfo.ArgumentList.Add(quality.ToString());
        process.StartInfo.ArgumentList.Add("jpg:-");

        try
        {
            process.Start();
        }
        catch (Exception)
        {
            return null;
        }

        var outputStream = new MemoryStream();
        Task<string> errorTask = process.StandardError.ReadToEndAsync(linkedToken);
        try
        {
            await process
                .StandardOutput.BaseStream.CopyToAsync(outputStream, linkedToken)
                .ConfigureAwait(false);
            await process.WaitForExitAsync(linkedToken).ConfigureAwait(false);
            _ = await errorTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKillProcess(process);
            try
            {
                _ = await errorTask.ConfigureAwait(false);
            }
            catch { }
            outputStream.Dispose();
            return null;
        }
        catch (Exception)
        {
            TryKillProcess(process);
            try
            {
                _ = await errorTask.ConfigureAwait(false);
            }
            catch { }
            outputStream.Dispose();
            return null;
        }

        if (process.ExitCode != 0 || outputStream.Length == 0)
        {
            outputStream.Dispose();
            return null;
        }

        outputStream.Position = 0;
        return outputStream;
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best-effort cancellation/cleanup.
        }
    }

    private static void AppendResizeArguments(
        ICollection<string> arguments,
        ScreenshotRequest request
    )
    {
        ArgumentNullException.ThrowIfNull(arguments);

        int? w = request.MaxWidthPx is > 0 ? request.MaxWidthPx : null;
        int? h = request.MaxHeightPx is > 0 ? request.MaxHeightPx : null;
        if (w is null && h is null)
            return;

        // Keep aspect ratio: downscale to fit within the requested box.
        // (The UI will stretch as needed.)
        string geometry =
            $"{(w is null ? "" : w.Value.ToString())}x{(h is null ? "" : h.Value.ToString())}";
        arguments.Add("-thumbnail");
        arguments.Add(geometry);
    }

    public string Execute(string client)
    {
        using var stream = CaptureScreenshotStream(client);
        return stream is null ? "error" : string.Empty;
    }
}
