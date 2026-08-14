using System.Runtime.InteropServices;
using WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Commands.Dependencies;
using WindowSwitcher.Lib.Data.Platform.Diagnostics;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.NoOp;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Pipewire;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.X11;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.Factories;

/// <summary>
/// Creates the single preview provider selected for the current platform session.
/// </summary>
public sealed class RuntimePreviewFrameProviderFactory(
    ILinuxDependencyRegistry? linuxDependencies = null,
    PlatformCapabilityStatus? capabilityStatus = null
)
{
    private readonly ILinuxDependencyRegistry _linuxDependencies =
        linuxDependencies ?? LinuxDependencies.Instance;
    private readonly PlatformCapabilityStatus _capabilityStatus =
        capabilityStatus ?? new PlatformCapabilityStatus();
    private readonly Func<bool> _supportsX11PreviewCapture = X11PreviewFrameProvider.IsSupported;
    private readonly Func<bool> _supportsLibPipeWire = LibPipeWireNative.IsAvailable;
    private readonly Func<bool> _supportsNativePipeWireAdapter =
        WindowSwitcherPipeWireNative.IsAvailable;
    private readonly Func<LinuxSessionKind> _sessionKindResolver =
        LinuxSessionDetector.GetSessionKind;
    private readonly Func<string, string?> _environmentResolver =
        Environment.GetEnvironmentVariable;

    internal RuntimePreviewFrameProviderFactory(
        ILinuxDependencyRegistry? linuxDependencies,
        Func<bool> supportsX11PreviewCapture,
        Func<bool> supportsLibPipeWire,
        Func<bool> supportsNativePipeWireAdapter,
        Func<LinuxSessionKind> sessionKindResolver,
        Func<string, string?>? environmentResolver = null,
        PlatformCapabilityStatus? capabilityStatus = null
    )
        : this(linuxDependencies, capabilityStatus)
    {
        ArgumentNullException.ThrowIfNull(supportsX11PreviewCapture);
        ArgumentNullException.ThrowIfNull(supportsLibPipeWire);
        ArgumentNullException.ThrowIfNull(supportsNativePipeWireAdapter);
        ArgumentNullException.ThrowIfNull(sessionKindResolver);
        _supportsX11PreviewCapture = supportsX11PreviewCapture;
        _supportsLibPipeWire = supportsLibPipeWire;
        _supportsNativePipeWireAdapter = supportsNativePipeWireAdapter;
        _sessionKindResolver = sessionKindResolver;
        if (environmentResolver is not null)
            _environmentResolver = environmentResolver;
    }

    /// <summary>
    /// Creates the preview provider selected for the current platform and session.
    /// </summary>
    public IPreviewFrameProvider Create(WinAccessorBase accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new NoOpPreviewFrameProvider();
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            throw new PlatformNotSupportedException("Only Windows and Linux are supported.");

        LinuxSessionKind session = _sessionKindResolver();
        return session switch
        {
            LinuxSessionKind.X11 => CreateX11Provider(session, accessor),
            LinuxSessionKind.Wayland => CreateWaylandProvider(session, accessor),
            _ => CreateUnsupportedProvider(session),
        };
    }

    private IPreviewFrameProvider CreateX11Provider(
        LinuxSessionKind session,
        WinAccessorBase accessor
    )
    {
        bool available = _supportsX11PreviewCapture();
        _capabilityStatus.Update(
            new PlatformCapabilitySnapshot(
                session,
                "EWMH",
                available ? "XComposite" : "None",
                available,
                available ? null : "XComposite or XDamage is unavailable."
            )
        );
        return available
            ? new X11PreviewFrameProvider(accessor)
            : new NoOpPreviewFrameProvider();
    }

    private IPreviewFrameProvider CreateWaylandProvider(
        LinuxSessionKind session,
        WinAccessorBase accessor
    )
    {
        bool hasXWayland = !string.IsNullOrWhiteSpace(_environmentResolver("DISPLAY"));
        bool hasLibPipeWire = _supportsLibPipeWire();
        bool hasNativeAdapter = _supportsNativePipeWireAdapter();
        if (!hasLibPipeWire)
            _linuxDependencies.ReportMissingOnce(LibPipeWireNative.LibraryName);
        if (!hasNativeAdapter)
            _linuxDependencies.ReportMissingOnce(WindowSwitcherPipeWireNative.LibraryName);

        var failures = new List<string>();
        if (!hasLibPipeWire)
            failures.Add($"{LibPipeWireNative.LibraryName} is unavailable.");
        if (!hasNativeAdapter)
            failures.Add($"{WindowSwitcherPipeWireNative.LibraryName} is unavailable.");
        if (!hasXWayland)
            failures.Insert(
                0,
                "DISPLAY is unavailable; XWayland window discovery is disabled."
            );

        bool available = hasLibPipeWire && hasNativeAdapter;
        string? failure = failures.Count == 0 ? null : string.Join(' ', failures);

        _capabilityStatus.Update(
            new PlatformCapabilitySnapshot(
                session,
                hasXWayland ? "EWMH (XWayland)" : "Unavailable",
                available ? "PipeWire" : "None",
                available,
                failure
            )
        );
        return available ? new PipeWireFrameProvider(accessor) : new NoOpPreviewFrameProvider();
    }

    private IPreviewFrameProvider CreateUnsupportedProvider(LinuxSessionKind session)
    {
        _capabilityStatus.Update(
            new PlatformCapabilitySnapshot(
                session,
                "Unavailable",
                "None",
                false,
                "No supported Linux display session was detected."
            )
        );
        return new NoOpPreviewFrameProvider();
    }
}
