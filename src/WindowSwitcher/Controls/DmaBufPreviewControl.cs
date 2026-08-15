using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Pipewire;
using static Avalonia.OpenGL.GlConsts;

namespace WindowSwitcher.Controls;

internal sealed class DmaBufPreviewControl : OpenGlControlBase
{
    private const int GlTextureMagFilter = 0x2800;
    private const int GlTextureWrapS = 0x2802;
    private const int GlTextureWrapT = 0x2803;
    private const int GlClampToEdge = 0x812F;
    private const int GlTriangleStrip = 0x0005;

    private readonly object _frameSync = new();
    private readonly TaskCompletionSource<bool> _availability = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private LinuxDmaBufPreviewFrame? _pendingFrame;
    private LinuxDmaBufEglInterop? _eglInterop;
    private int _program;
    private int _vertexBuffer;
    private int _texture;
    private bool _failureReported;
    private bool _renderingDisabled;

    internal event EventHandler? ImportFailed;

    internal async Task<bool> WaitForAvailabilityAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await _availability
                .Task.WaitAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    internal void Present(LinuxDmaBufPreviewFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        LinuxDmaBufPreviewFrame? replaced;
        lock (_frameSync)
        {
            if (_renderingDisabled)
            {
                frame.Dispose();
                return;
            }
            replaced = _pendingFrame;
            _pendingFrame = frame;
        }
        replaced?.Dispose();
        RequestNextFrameRendering();
    }

    internal void ClearFrame()
    {
        TakePendingFrame()?.Dispose();
        lock (_frameSync)
        {
            if (_renderingDisabled)
                return;
        }
        RequestNextFrameRendering();
    }

    internal void DisableRendering()
    {
        lock (_frameSync)
            _renderingDisabled = true;
        TakePendingFrame()?.Dispose();
    }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        try
        {
            IntPtr imageTargetAddress = gl.GetProcAddress("glEGLImageTargetTexture2DOES");
            _eglInterop = LinuxDmaBufEglInterop.Create(imageTargetAddress);
            _program = CreateProgram(gl);
            _vertexBuffer = gl.GenBuffer();
            _texture = gl.GenTexture();

            ReadOnlySpan<float> vertices =
            [
                -1f,
                -1f,
                0f,
                1f,
                1f,
                -1f,
                1f,
                1f,
                -1f,
                1f,
                0f,
                0f,
                1f,
                1f,
                1f,
                0f,
            ];
            unsafe
            {
                fixed (float* vertexPointer = vertices)
                {
                    gl.BindBuffer(GL_ARRAY_BUFFER, _vertexBuffer);
                    gl.BufferData(
                        GL_ARRAY_BUFFER,
                        new IntPtr(vertices.Length * sizeof(float)),
                        new IntPtr(vertexPointer),
                        GL_STATIC_DRAW
                    );
                }
            }
            _availability.TrySetResult(true);
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
        }
    }

    protected override void OnOpenGlRender(GlInterface gl, int framebuffer)
    {
        LinuxDmaBufPreviewFrame? frame = TakePendingFrame();

        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        int width = Math.Max(1, (int)Math.Round(Bounds.Width * scaling));
        int height = Math.Max(1, (int)Math.Round(Bounds.Height * scaling));
        gl.Viewport(0, 0, width, height);
        gl.ClearColor(0, 0, 0, 0);
        gl.Clear(GL_COLOR_BUFFER_BIT);
        if (frame is null)
            return;

        try
        {
            RenderFrame(gl, frame);
            gl.Finish();
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
        }
        finally
        {
            frame.Dispose();
        }
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        TakePendingFrame()?.Dispose();
        if (_texture != 0)
            gl.DeleteTexture(_texture);
        if (_vertexBuffer != 0)
            gl.DeleteBuffer(_vertexBuffer);
        if (_program != 0)
            gl.DeleteProgram(_program);
        _texture = 0;
        _vertexBuffer = 0;
        _program = 0;
        _eglInterop = null;
    }

    protected override void OnOpenGlLost()
    {
        TakePendingFrame()?.Dispose();
        ReportFailure(new InvalidOperationException("The OpenGL preview context was lost."));
    }

    private void ReportFailure(Exception exception)
    {
        System.Diagnostics.Trace.TraceError($"DMA-BUF preview rendering failed: {exception}");
        if (_failureReported)
            return;
        _failureReported = true;
        DisableRendering();
        _availability.TrySetResult(false);
        ImportFailed?.Invoke(this, EventArgs.Empty);
    }

    private void RenderFrame(GlInterface gl, LinuxDmaBufPreviewFrame frame)
    {
        if (_eglInterop is null)
            throw new InvalidOperationException("DMA-BUF renderer is not initialized.");
        using (LinuxDmaBufEglImage image = _eglInterop.Import(frame))
        {
            gl.ActiveTexture(GL_TEXTURE0);
            gl.BindTexture(GL_TEXTURE_2D, _texture);
            gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
            gl.TexParameteri(GL_TEXTURE_2D, GlTextureMagFilter, GL_LINEAR);
            gl.TexParameteri(GL_TEXTURE_2D, GlTextureWrapS, GlClampToEdge);
            gl.TexParameteri(GL_TEXTURE_2D, GlTextureWrapT, GlClampToEdge);
            _eglInterop.BindTexture(GL_TEXTURE_2D, image);

            gl.UseProgram(_program);
            gl.Uniform1i(gl.GetUniformLocationString(_program, "previewTexture"), 0);
            gl.BindBuffer(GL_ARRAY_BUFFER, _vertexBuffer);
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(0, 2, GL_FLOAT, 0, 4 * sizeof(float), IntPtr.Zero);
            gl.EnableVertexAttribArray(1);
            gl.VertexAttribPointer(
                1,
                2,
                GL_FLOAT,
                0,
                4 * sizeof(float),
                new IntPtr(2 * sizeof(float))
            );
            gl.DrawArrays(GlTriangleStrip, 0, new IntPtr(4));
        }
    }

    private static int CreateProgram(GlInterface gl)
    {
        bool gles = gl.ContextInfo.Version.Type == GlProfileType.OpenGLES;
        string prefix = gles ? "precision mediump float;\n" : "#version 130\n";
        string vertexSource = gles
            ? "attribute vec2 position; attribute vec2 texCoord; varying vec2 uv; void main(){ uv=texCoord; gl_Position=vec4(position,0.0,1.0); }"
            : "in vec2 position; in vec2 texCoord; out vec2 uv; void main(){ uv=texCoord; gl_Position=vec4(position,0.0,1.0); }";
        string fragmentSource = gles
            ? $"{prefix}varying vec2 uv; uniform sampler2D previewTexture; void main(){{ gl_FragColor=texture2D(previewTexture,uv); }}"
            : $"{prefix}in vec2 uv; uniform sampler2D previewTexture; out vec4 color; void main(){{ color=texture(previewTexture,uv); }}";

        int vertexShader = CompileShader(gl, GL_VERTEX_SHADER, prefix + vertexSource);
        int fragmentShader = CompileShader(gl, GL_FRAGMENT_SHADER, fragmentSource);
        try
        {
            int program = gl.CreateProgram();
            gl.AttachShader(program, vertexShader);
            gl.AttachShader(program, fragmentShader);
            gl.BindAttribLocationString(program, 0, "position");
            gl.BindAttribLocationString(program, 1, "texCoord");
            string? error = gl.LinkProgramAndGetError(program);
            if (error is not null)
            {
                gl.DeleteProgram(program);
                throw new InvalidOperationException(
                    $"DMA-BUF shader program could not be linked: {error}"
                );
            }
            return program;
        }
        finally
        {
            gl.DeleteShader(vertexShader);
            gl.DeleteShader(fragmentShader);
        }
    }

    private static int CompileShader(GlInterface gl, int shaderType, string source)
    {
        int shader = gl.CreateShader(shaderType);
        string? error = gl.CompileShaderAndGetError(shader, source);
        if (error is null)
            return shader;
        gl.DeleteShader(shader);
        throw new InvalidOperationException($"DMA-BUF shader could not be compiled: {error}");
    }

    private LinuxDmaBufPreviewFrame? TakePendingFrame()
    {
        lock (_frameSync)
        {
            LinuxDmaBufPreviewFrame? frame = _pendingFrame;
            _pendingFrame = null;
            return frame;
        }
    }
}
