using System;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using WindowSwitcherLib.Data;
using WindowSwitcherLib.Data.FileAccess;
using WindowSwitcherLib.Data.WindowAccess;

namespace WindowSwitcher.Controls;

public sealed class X11WindowStreamControl : OpenGlControlBase
{
    public static readonly StyledProperty<string?> WindowIdProperty =
        AvaloniaProperty.Register<X11WindowStreamControl, string?>(nameof(WindowId));

    public string? WindowId
    {
        get => GetValue(WindowIdProperty);
        set => SetValue(WindowIdProperty, value);
    }

    private X11GlxTextureFromPixmap? _pixmapSource;
    private int _textureId;
    private bool _textureBound;

    private int _program;
    private int _vbo;
    private bool _glReady;
    public bool StreamingActive => _textureBound;
    private bool _reportedInitFailure;
    private bool _reportedBindFailure;

    private glGenTexturesDelegate? _glGenTextures;
    private glDeleteTexturesDelegate? _glDeleteTextures;
    private glBindTextureDelegate? _glBindTexture;
    private glTexParameteriDelegate? _glTexParameteri;
    private glActiveTextureDelegate? _glActiveTexture;
    private glBindFramebufferDelegate? _glBindFramebuffer;
    private glViewportDelegate? _glViewport;
    private glClearColorDelegate? _glClearColor;
    private glClearDelegate? _glClear;

    private glCreateShaderDelegate? _glCreateShader;
    private glShaderSourceDelegate? _glShaderSource;
    private glCompileShaderDelegate? _glCompileShader;
    private glGetShaderivDelegate? _glGetShaderiv;
    private glGetShaderInfoLogDelegate? _glGetShaderInfoLog;
    private glDeleteShaderDelegate? _glDeleteShader;

    private glCreateProgramDelegate? _glCreateProgram;
    private glAttachShaderDelegate? _glAttachShader;
    private glBindAttribLocationDelegate? _glBindAttribLocation;
    private glLinkProgramDelegate? _glLinkProgram;
    private glGetProgramivDelegate? _glGetProgramiv;
    private glGetProgramInfoLogDelegate? _glGetProgramInfoLog;
    private glUseProgramDelegate? _glUseProgram;
    private glGetUniformLocationDelegate? _glGetUniformLocation;
    private glUniform1iDelegate? _glUniform1i;
    private glDeleteProgramDelegate? _glDeleteProgram;

    private glGenBuffersDelegate? _glGenBuffers;
    private glBindBufferDelegate? _glBindBuffer;
    private glBufferDataDelegate? _glBufferData;
    private glDeleteBuffersDelegate? _glDeleteBuffers;

    private glEnableVertexAttribArrayDelegate? _glEnableVertexAttribArray;
    private glDisableVertexAttribArrayDelegate? _glDisableVertexAttribArray;
    private glVertexAttribPointerDelegate? _glVertexAttribPointer;
    private glDrawArraysDelegate? _glDrawArrays;

    static X11WindowStreamControl()
    {
        WindowIdProperty.Changed.AddClassHandler<X11WindowStreamControl>((control, _) => control.OnWindowIdChanged());
    }

    public void RequestFrame() => RequestNextFrameRendering();

    private void OnWindowIdChanged()
    {
        _textureBound = false;
        _pixmapSource?.Dispose();
        _pixmapSource = null;
        RequestNextFrameRendering();
    }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        _glReady = TryLoadGl(gl);
        if (!_glReady)
            return;

        _glGenTextures!.Invoke(1, out _textureId);
        _glBindTexture!.Invoke(GL_TEXTURE_2D, _textureId);
        _glTexParameteri!.Invoke(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
        _glTexParameteri!.Invoke(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
        _glTexParameteri!.Invoke(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
        _glTexParameteri!.Invoke(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
        _glBindTexture!.Invoke(GL_TEXTURE_2D, 0);

        _program = CreateProgram();
        _glGenBuffers!.Invoke(1, out _vbo);

        _glBindBuffer!.Invoke(GL_ARRAY_BUFFER, _vbo);

        // Fullscreen quad: position (x,y) + texcoord (u,v)
        // Triangle strip order (4 vertices).
        float[] vertices =
        [
            -1f, -1f, 0f, 1f,
            1f, -1f, 1f, 1f,
            -1f, 1f, 0f, 0f,
            1f, 1f, 1f, 0f
        ];

        var handle = GCHandle.Alloc(vertices, GCHandleType.Pinned);
        try
        {
            _glBufferData!.Invoke(
                GL_ARRAY_BUFFER,
                new IntPtr(vertices.Length * sizeof(float)),
                handle.AddrOfPinnedObject(),
                GL_STATIC_DRAW);
        }
        finally
        {
            handle.Free();
        }

        _glBindBuffer!.Invoke(GL_ARRAY_BUFFER, 0);
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        _pixmapSource?.Dispose();
        _pixmapSource = null;
        _textureBound = false;

        if (_vbo != 0)
            _glDeleteBuffers?.Invoke(1, ref _vbo);
        if (_program != 0)
            _glDeleteProgram?.Invoke(_program);
        if (_textureId != 0)
            _glDeleteTextures?.Invoke(1, ref _textureId);

        _vbo = 0;
        _program = 0;
        _textureId = 0;
        _glReady = false;
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (!_glReady)
            return;

        _glBindFramebuffer!.Invoke(GL_FRAMEBUFFER, fb);
        _glViewport!.Invoke(0, 0, (int)Math.Max(1, Bounds.Width), (int)Math.Max(1, Bounds.Height));
        _glClearColor!.Invoke(0f, 0f, 0f, 1f);
        _glClear!.Invoke(GL_COLOR_BUFFER_BIT);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;
        if (string.IsNullOrWhiteSpace(WindowId))
            return;

        _pixmapSource ??= new X11GlxTextureFromPixmap(WindowId);
        if (!_pixmapSource.TryInitializeForCurrentContext(out string? initError))
        {
            _textureBound = false;
            ReportOnce(ref _reportedInitFailure, "OpenGL window stream init failed", initError);
            return;
        }

        _glUseProgram!.Invoke(_program);
        _glActiveTexture!.Invoke(GL_TEXTURE0);
        _glBindTexture!.Invoke(GL_TEXTURE_2D, _textureId);

        // Refresh the binding each frame to get updated contents.
        _pixmapSource.Release();
        if (!_pixmapSource.TryBindToCurrentTexture(out string? bindError))
        {
            _textureBound = false;
            ReportOnce(ref _reportedBindFailure, "OpenGL window stream bind failed", bindError);
            return;
        }
        _textureBound = true;

        int samplerLoc = GetUniformLocation(_program, "uTex");
        if (samplerLoc >= 0 && _glUniform1i is not null)
            _glUniform1i(samplerLoc, 0);

        _glBindBuffer!.Invoke(GL_ARRAY_BUFFER, _vbo);
        const int stride = 4 * sizeof(float);
        _glEnableVertexAttribArray!.Invoke(0);
        _glVertexAttribPointer!.Invoke(0, 2, GL_FLOAT, 0, stride, IntPtr.Zero);
        _glEnableVertexAttribArray!.Invoke(1);
        _glVertexAttribPointer!.Invoke(1, 2, GL_FLOAT, 0, stride, new IntPtr(2 * sizeof(float)));

        _glDrawArrays!.Invoke(GL_TRIANGLE_STRIP, 0, 4);
        _glDisableVertexAttribArray!.Invoke(1);
        _glDisableVertexAttribArray!.Invoke(0);
        _glBindBuffer!.Invoke(GL_ARRAY_BUFFER, 0);

        _glBindTexture!.Invoke(GL_TEXTURE_2D, 0);
        _glUseProgram!.Invoke(0);
    }

    private int CreateProgram()
    {
        // Prefer modern GLSL first; fall back to legacy for compatibility profiles.
        const string vertex330 =
            "#version 330 core\n" +
            "layout(location = 0) in vec2 aPos;\n" +
            "layout(location = 1) in vec2 aTex;\n" +
            "out vec2 vTex;\n" +
            "void main() { vTex = aTex; gl_Position = vec4(aPos, 0.0, 1.0); }\n";

        const string fragment330 =
            "#version 330 core\n" +
            "uniform sampler2D uTex;\n" +
            "in vec2 vTex;\n" +
            "out vec4 FragColor;\n" +
            "void main() { FragColor = texture(uTex, vTex); }\n";

        int program = TryCreateProgram(vertex330, fragment330);
        if (program != 0)
            return program;

        const string vertex120 =
            "#version 120\n" +
            "attribute vec2 aPos;\n" +
            "attribute vec2 aTex;\n" +
            "varying vec2 vTex;\n" +
            "void main() { vTex = aTex; gl_Position = vec4(aPos, 0.0, 1.0); }\n";

        const string fragment120 =
            "#version 120\n" +
            "uniform sampler2D uTex;\n" +
            "varying vec2 vTex;\n" +
            "void main() { gl_FragColor = texture2D(uTex, vTex); }\n";

        return TryCreateProgram(vertex120, fragment120);
    }

    private int TryCreateProgram(string vertex, string fragment)
    {
        int vs = CompileShader(GL_VERTEX_SHADER, vertex);
        int fs = CompileShader(GL_FRAGMENT_SHADER, fragment);
        if (vs == 0 || fs == 0)
            return 0;

        int program = _glCreateProgram!.Invoke();
        _glAttachShader!.Invoke(program, vs);
        _glAttachShader!.Invoke(program, fs);

        BindAttribLocation(program, 0, "aPos");
        BindAttribLocation(program, 1, "aTex");

        _glLinkProgram!.Invoke(program);
        if (!CheckProgram(program))
        {
            _glDeleteShader!.Invoke(vs);
            _glDeleteShader!.Invoke(fs);
            _glDeleteProgram!.Invoke(program);
            return 0;
        }

        _glDeleteShader!.Invoke(vs);
        _glDeleteShader!.Invoke(fs);
        return program;
    }

    private int CompileShader(int type, string source)
    {
        int shader = _glCreateShader!.Invoke(type);
        if (shader == 0)
            return 0;
        SetShaderSource(shader, source);
        _glCompileShader!.Invoke(shader);
        if (!CheckShader(shader))
        {
            _glDeleteShader!.Invoke(shader);
            return 0;
        }
        return shader;
    }

    private bool TryLoadGl(GlInterface gl)
    {
        _glGenTextures = Load<glGenTexturesDelegate>(gl, "glGenTextures");
        _glDeleteTextures = Load<glDeleteTexturesDelegate>(gl, "glDeleteTextures");
        _glBindTexture = Load<glBindTextureDelegate>(gl, "glBindTexture");
        _glTexParameteri = Load<glTexParameteriDelegate>(gl, "glTexParameteri");
        _glActiveTexture = Load<glActiveTextureDelegate>(gl, "glActiveTexture");
        _glBindFramebuffer = Load<glBindFramebufferDelegate>(gl, "glBindFramebuffer");
        _glViewport = Load<glViewportDelegate>(gl, "glViewport");
        _glClearColor = Load<glClearColorDelegate>(gl, "glClearColor");
        _glClear = Load<glClearDelegate>(gl, "glClear");

        _glCreateShader = Load<glCreateShaderDelegate>(gl, "glCreateShader");
        _glShaderSource = Load<glShaderSourceDelegate>(gl, "glShaderSource");
        _glCompileShader = Load<glCompileShaderDelegate>(gl, "glCompileShader");
        _glGetShaderiv = Load<glGetShaderivDelegate>(gl, "glGetShaderiv");
        _glGetShaderInfoLog = Load<glGetShaderInfoLogDelegate>(gl, "glGetShaderInfoLog");
        _glDeleteShader = Load<glDeleteShaderDelegate>(gl, "glDeleteShader");

        _glCreateProgram = Load<glCreateProgramDelegate>(gl, "glCreateProgram");
        _glAttachShader = Load<glAttachShaderDelegate>(gl, "glAttachShader");
        _glBindAttribLocation = Load<glBindAttribLocationDelegate>(gl, "glBindAttribLocation");
        _glLinkProgram = Load<glLinkProgramDelegate>(gl, "glLinkProgram");
        _glGetProgramiv = Load<glGetProgramivDelegate>(gl, "glGetProgramiv");
        _glGetProgramInfoLog = Load<glGetProgramInfoLogDelegate>(gl, "glGetProgramInfoLog");
        _glUseProgram = Load<glUseProgramDelegate>(gl, "glUseProgram");
        _glGetUniformLocation = Load<glGetUniformLocationDelegate>(gl, "glGetUniformLocation");
        _glUniform1i = Load<glUniform1iDelegate>(gl, "glUniform1i");
        _glDeleteProgram = Load<glDeleteProgramDelegate>(gl, "glDeleteProgram");

        _glGenBuffers = Load<glGenBuffersDelegate>(gl, "glGenBuffers");
        _glBindBuffer = Load<glBindBufferDelegate>(gl, "glBindBuffer");
        _glBufferData = Load<glBufferDataDelegate>(gl, "glBufferData");
        _glDeleteBuffers = Load<glDeleteBuffersDelegate>(gl, "glDeleteBuffers");

        _glEnableVertexAttribArray = Load<glEnableVertexAttribArrayDelegate>(gl, "glEnableVertexAttribArray");
        _glDisableVertexAttribArray = Load<glDisableVertexAttribArrayDelegate>(gl, "glDisableVertexAttribArray");
        _glVertexAttribPointer = Load<glVertexAttribPointerDelegate>(gl, "glVertexAttribPointer");
        _glDrawArrays = Load<glDrawArraysDelegate>(gl, "glDrawArrays");

        return _glGenTextures is not null
               && _glDeleteTextures is not null
               && _glBindTexture is not null
               && _glTexParameteri is not null
               && _glActiveTexture is not null
               && _glBindFramebuffer is not null
               && _glViewport is not null
               && _glClearColor is not null
               && _glClear is not null
               && _glCreateShader is not null
               && _glShaderSource is not null
               && _glCompileShader is not null
               && _glGetShaderiv is not null
               && _glDeleteShader is not null
               && _glCreateProgram is not null
               && _glAttachShader is not null
               && _glBindAttribLocation is not null
               && _glLinkProgram is not null
               && _glGetProgramiv is not null
               && _glUseProgram is not null
               && _glGetUniformLocation is not null
               && _glUniform1i is not null
               && _glDeleteProgram is not null
               && _glGenBuffers is not null
               && _glBindBuffer is not null
               && _glBufferData is not null
               && _glDeleteBuffers is not null
               && _glEnableVertexAttribArray is not null
               && _glDisableVertexAttribArray is not null
               && _glVertexAttribPointer is not null
               && _glDrawArrays is not null;
    }

    private static T? Load<T>(GlInterface gl, string name) where T : class
    {
        IntPtr ptr = gl.GetProcAddress(name);
        return ptr == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer(ptr, typeof(T)) as T;
    }

    private void SetShaderSource(int shader, string source)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(source + '\0');
        IntPtr sourcePtr = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, sourcePtr, bytes.Length);
        IntPtr arrayPtr = Marshal.AllocHGlobal(IntPtr.Size);
        Marshal.WriteIntPtr(arrayPtr, sourcePtr);

        try
        {
            _glShaderSource!.Invoke(shader, 1, arrayPtr, IntPtr.Zero);
        }
        finally
        {
            Marshal.FreeHGlobal(arrayPtr);
            Marshal.FreeHGlobal(sourcePtr);
        }
    }

    private void BindAttribLocation(int program, int index, string name)
    {
        IntPtr namePtr = Marshal.StringToHGlobalAnsi(name);
        try
        {
            _glBindAttribLocation!.Invoke(program, index, namePtr);
        }
        finally
        {
            Marshal.FreeHGlobal(namePtr);
        }
    }

    private int GetUniformLocation(int program, string name)
    {
        IntPtr namePtr = Marshal.StringToHGlobalAnsi(name);
        try
        {
            return _glGetUniformLocation!.Invoke(program, namePtr);
        }
        finally
        {
            Marshal.FreeHGlobal(namePtr);
        }
    }

    private bool CheckShader(int shader)
    {
        _glGetShaderiv!.Invoke(shader, GL_COMPILE_STATUS, out int status);
        if (status != 0)
            return true;

        return false;
    }

    private bool CheckProgram(int program)
    {
        _glGetProgramiv!.Invoke(program, GL_LINK_STATUS, out int status);
        if (status != 0)
            return true;

        return false;
    }

    private const int GL_FRAMEBUFFER = 0x8D40;
    private const int GL_COLOR_BUFFER_BIT = 0x00004000;
    private const int GL_TEXTURE_2D = 0x0DE1;
    private const int GL_TEXTURE0 = 0x84C0;
    private const int GL_TEXTURE_MIN_FILTER = 0x2801;
    private const int GL_TEXTURE_MAG_FILTER = 0x2800;
    private const int GL_LINEAR = 0x2601;
    private const int GL_TEXTURE_WRAP_S = 0x2802;
    private const int GL_TEXTURE_WRAP_T = 0x2803;
    private const int GL_CLAMP_TO_EDGE = 0x812F;
    private const int GL_ARRAY_BUFFER = 0x8892;
    private const int GL_STATIC_DRAW = 0x88E4;
    private const int GL_FLOAT = 0x1406;
    private const int GL_TRIANGLE_STRIP = 0x0005;
    private const int GL_VERTEX_SHADER = 0x8B31;
    private const int GL_FRAGMENT_SHADER = 0x8B30;
    private const int GL_COMPILE_STATUS = 0x8B81;
    private const int GL_LINK_STATUS = 0x8B82;

    private static void ReportOnce(ref bool reported, string title, string? details)
    {
        if (reported)
            return;
        reported = true;

        bool logsEnabled = ConfigFileAccessor.GetInstance().ReadConfig(config => config.ActivateLogs);
        if (!logsEnabled)
            return;

        string message = details is null ? title : $"{title}: {details}";
        AppLogger.Log(message, StaticData.LogSeverity.WARN);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void glGenTexturesDelegate(int n, out int textures);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void glDeleteTexturesDelegate(int n, ref int textures);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void glBindTextureDelegate(int target, int texture);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void glTexParameteriDelegate(int target, int pname, int param);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void glActiveTextureDelegate(int texture);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void glBindFramebufferDelegate(int target, int framebuffer);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void glViewportDelegate(int x, int y, int width, int height);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void glClearColorDelegate(float red, float green, float blue, float alpha);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void glClearDelegate(int mask);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int glCreateShaderDelegate(int type);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void glShaderSourceDelegate(int shader, int count, IntPtr strings, IntPtr lengths);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void glCompileShaderDelegate(int shader);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void glGetShaderivDelegate(int shader, int pname, out int param);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void glGetShaderInfoLogDelegate(int shader, int bufSize, out int length, IntPtr infoLog);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void glDeleteShaderDelegate(int shader);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int glCreateProgramDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void glAttachShaderDelegate(int program, int shader);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void glBindAttribLocationDelegate(int program, int index, IntPtr name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void glLinkProgramDelegate(int program);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void glGetProgramivDelegate(int program, int pname, out int param);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void glGetProgramInfoLogDelegate(int program, int bufSize, out int length, IntPtr infoLog);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void glUseProgramDelegate(int program);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int glGetUniformLocationDelegate(int program, IntPtr name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void glUniform1iDelegate(int location, int v0);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void glDeleteProgramDelegate(int program);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void glGenBuffersDelegate(int n, out int buffers);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void glBindBufferDelegate(int target, int buffer);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void glBufferDataDelegate(int target, IntPtr size, IntPtr data, int usage);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void glDeleteBuffersDelegate(int n, ref int buffers);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void glEnableVertexAttribArrayDelegate(int index);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void glDisableVertexAttribArrayDelegate(int index);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void glVertexAttribPointerDelegate(int index, int size, int type, byte normalized, int stride, IntPtr pointer);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void glDrawArraysDelegate(int mode, int first, int count);
}
