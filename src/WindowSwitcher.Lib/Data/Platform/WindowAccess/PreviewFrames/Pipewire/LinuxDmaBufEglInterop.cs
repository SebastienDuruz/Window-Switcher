using System.Runtime.InteropServices;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Pipewire;

/// <summary>
/// Imports Linux DMA-BUF preview frames into the current EGL context.
/// </summary>
public sealed class LinuxDmaBufEglInterop
{
    private const int EglNone = 0x3038;
    private const int EglHeight = 0x3056;
    private const int EglWidth = 0x3057;
    private const int EglExtensions = 0x3055;
    private const int EglLinuxDmaBuf = 0x3270;
    private const int EglLinuxDrmFourCc = 0x3271;
    private const int EglDmaBufPlane0Fd = 0x3272;
    private const int EglDmaBufPlane0Offset = 0x3273;
    private const int EglDmaBufPlane0Pitch = 0x3274;
    private const int EglDmaBufPlane0ModifierLow = 0x3443;
    private const int EglDmaBufPlane0ModifierHigh = 0x3444;

    private readonly IntPtr _display;
    private readonly EglCreateImage _createImage;
    private readonly EglDestroyImage _destroyImage;
    private readonly GlEglImageTargetTexture _bindImage;

    private LinuxDmaBufEglInterop(
        IntPtr display,
        EglCreateImage createImage,
        EglDestroyImage destroyImage,
        GlEglImageTargetTexture bindImage
    )
    {
        _display = display;
        _createImage = createImage;
        _destroyImage = destroyImage;
        _bindImage = bindImage;
    }

    /// <summary>
    /// Creates an importer for the current EGL context.
    /// </summary>
    /// <param name="imageTargetTextureAddress">
    /// Address of the current context's <c>glEGLImageTargetTexture2DOES</c> function.
    /// </param>
    /// <returns>An initialized EGL DMA-BUF importer.</returns>
    /// <exception cref="PlatformNotSupportedException">The current platform is not Linux.</exception>
    /// <exception cref="NotSupportedException">The required EGL or OpenGL extension is missing.</exception>
    public static LinuxDmaBufEglInterop Create(IntPtr imageTargetTextureAddress)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException(
                "DMA-BUF EGL import is only available on Linux."
            );
        if (imageTargetTextureAddress == IntPtr.Zero)
            throw new NotSupportedException("GL_OES_EGL_image is unavailable.");

        IntPtr display = EglGetCurrentDisplay();
        if (display == IntPtr.Zero)
            throw new NotSupportedException(
                "No current EGL display is available for DMA-BUF import."
            );
        string extensions = Marshal.PtrToStringAnsi(EglQueryString(display, EglExtensions)) ?? "";
        if (
            !extensions
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains("EGL_EXT_image_dma_buf_import")
        )
            throw new NotSupportedException("EGL_EXT_image_dma_buf_import is unavailable.");

        var interop = new LinuxDmaBufEglInterop(
            display,
            LoadEglFunction<EglCreateImage>("eglCreateImageKHR"),
            LoadEglFunction<EglDestroyImage>("eglDestroyImageKHR"),
            Marshal.GetDelegateForFunctionPointer<GlEglImageTargetTexture>(
                imageTargetTextureAddress
            )
        );
        PipeWireDmaBufCapabilities.Set(interop.QueryCapabilities(extensions));
        return interop;
    }

    /// <summary>
    /// Imports a leased DMA-BUF frame into an EGL image.
    /// </summary>
    /// <param name="frame">DMA-BUF frame to import.</param>
    /// <returns>A disposable EGL image.</returns>
    public LinuxDmaBufEglImage Import(LinuxDmaBufPreviewFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        Span<int> attributes = stackalloc int[17];
        int index = 0;
        AddAttribute(attributes, ref index, EglWidth, frame.WidthPx);
        AddAttribute(attributes, ref index, EglHeight, frame.HeightPx);
        AddAttribute(attributes, ref index, EglLinuxDrmFourCc, unchecked((int)frame.DrmFormat));
        AddAttribute(attributes, ref index, EglDmaBufPlane0Fd, frame.FileDescriptor);
        AddAttribute(attributes, ref index, EglDmaBufPlane0Offset, frame.Offset);
        AddAttribute(attributes, ref index, EglDmaBufPlane0Pitch, frame.Stride);
        if (frame.Modifier != LinuxDmaBufPreviewFrame.InvalidModifier)
        {
            AddAttribute(
                attributes,
                ref index,
                EglDmaBufPlane0ModifierLow,
                unchecked((int)frame.Modifier)
            );
            AddAttribute(
                attributes,
                ref index,
                EglDmaBufPlane0ModifierHigh,
                unchecked((int)(frame.Modifier >> 32))
            );
        }
        attributes[index] = EglNone;

        IntPtr image;
        unsafe
        {
            fixed (int* attributePointer = attributes)
            {
                image = _createImage(
                    _display,
                    IntPtr.Zero,
                    EglLinuxDmaBuf,
                    IntPtr.Zero,
                    new IntPtr(attributePointer)
                );
            }
        }
        if (image == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"eglCreateImageKHR failed with EGL error 0x{EglGetError():X}."
            );
        }
        return new LinuxDmaBufEglImage(image, () => _destroyImage(_display, image));
    }

    /// <summary>
    /// Binds an imported EGL image to the currently bound OpenGL texture.
    /// </summary>
    /// <param name="textureTarget">OpenGL texture target.</param>
    /// <param name="image">Imported EGL image.</param>
    public void BindTexture(int textureTarget, LinuxDmaBufEglImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        _bindImage(textureTarget, image.Handle);
    }

    private static void AddAttribute(Span<int> attributes, ref int index, int name, int value)
    {
        attributes[index++] = name;
        attributes[index++] = value;
    }

    private static T LoadEglFunction<T>(string name)
        where T : Delegate
    {
        IntPtr address = EglGetProcAddress(name);
        if (address == IntPtr.Zero)
            throw new NotSupportedException($"EGL function {name} is unavailable.");
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    private IReadOnlyCollection<PipeWireDmaBufFormatModifier> QueryCapabilities(string extensions)
    {
        uint[] supportedFormats = [0x34325241, 0x34325258, 0x34324241, 0x34324258];
        if (
            !extensions
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains("EGL_EXT_image_dma_buf_import_modifiers")
        )
        {
            return supportedFormats
                .Select(format => new PipeWireDmaBufFormatModifier(
                    format,
                    LinuxDmaBufPreviewFrame.InvalidModifier
                ))
                .ToArray();
        }

        EglQueryDmaBufFormats queryFormats = LoadEglFunction<EglQueryDmaBufFormats>(
            "eglQueryDmaBufFormatsEXT"
        );
        EglQueryDmaBufModifiers queryModifiers = LoadEglFunction<EglQueryDmaBufModifiers>(
            "eglQueryDmaBufModifiersEXT"
        );
        if (queryFormats(_display, 0, IntPtr.Zero, out int formatCount) == 0 || formatCount <= 0)
            return [];

        var formats = new int[formatCount];
        unsafe
        {
            fixed (int* formatPointer = formats)
            {
                if (
                    queryFormats(
                        _display,
                        formats.Length,
                        new IntPtr(formatPointer),
                        out formatCount
                    ) == 0
                )
                    return [];
            }
        }

        var result = new List<PipeWireDmaBufFormatModifier>();
        foreach (
            uint drmFormat in supportedFormats.Where(format =>
                formats.Contains(unchecked((int)format))
            )
        )
        {
            if (
                queryModifiers(
                    _display,
                    unchecked((int)drmFormat),
                    0,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    out int count
                ) == 0
                || count <= 0
            )
                continue;
            var modifiers = new ulong[count];
            var externalOnly = new int[count];
            unsafe
            {
                fixed (ulong* modifierPointer = modifiers)
                fixed (int* externalPointer = externalOnly)
                {
                    if (
                        queryModifiers(
                            _display,
                            unchecked((int)drmFormat),
                            count,
                            new IntPtr(modifierPointer),
                            new IntPtr(externalPointer),
                            out count
                        ) == 0
                    )
                        continue;
                }
            }
            for (int index = 0; index < count; index++)
            {
                if (externalOnly[index] == 0)
                    result.Add(new PipeWireDmaBufFormatModifier(drmFormat, modifiers[index]));
            }
        }
        return result;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr EglCreateImage(
        IntPtr display,
        IntPtr context,
        int target,
        IntPtr buffer,
        IntPtr attributes
    );

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int EglDestroyImage(IntPtr display, IntPtr image);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlEglImageTargetTexture(int target, IntPtr image);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int EglQueryDmaBufFormats(
        IntPtr display,
        int maximumFormats,
        IntPtr formats,
        out int formatCount
    );

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int EglQueryDmaBufModifiers(
        IntPtr display,
        int format,
        int maximumModifiers,
        IntPtr modifiers,
        IntPtr externalOnly,
        out int modifierCount
    );

    [DllImport("libEGL.so.1", EntryPoint = "eglGetCurrentDisplay")]
    private static extern IntPtr EglGetCurrentDisplay();

    [DllImport("libEGL.so.1", EntryPoint = "eglGetProcAddress")]
    private static extern IntPtr EglGetProcAddress(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name
    );

    [DllImport("libEGL.so.1", EntryPoint = "eglQueryString")]
    private static extern IntPtr EglQueryString(IntPtr display, int name);

    [DllImport("libEGL.so.1", EntryPoint = "eglGetError")]
    private static extern int EglGetError();
}

internal readonly record struct PipeWireDmaBufFormatModifier(uint DrmFormat, ulong Modifier);

internal static class PipeWireDmaBufCapabilities
{
    private static PipeWireDmaBufFormatModifier[] _current = [];

    internal static IReadOnlyCollection<PipeWireDmaBufFormatModifier> Current =>
        Volatile.Read(ref _current);

    internal static void Set(IReadOnlyCollection<PipeWireDmaBufFormatModifier> capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        Volatile.Write(ref _current, capabilities.Distinct().ToArray());
    }
}

/// <summary>
/// Represents an imported EGL image and releases it when disposed.
/// </summary>
public sealed class LinuxDmaBufEglImage : IDisposable
{
    private Action? _release;

    internal LinuxDmaBufEglImage(IntPtr handle, Action release)
    {
        Handle = handle;
        _release = release;
    }

    internal IntPtr Handle { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
