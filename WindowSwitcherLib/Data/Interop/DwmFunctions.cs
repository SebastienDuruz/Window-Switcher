using System.Runtime.InteropServices;

namespace WindowSwitcherLib.Data.Interop;

public static class DwmFunctions
{
    public const int DWM_TNP_RECTDESTINATION = 0x00000001;
    public const int DWM_TNP_OPACITY = 0x00000004;
    public const int DWM_TNP_VISIBLE = 0x00000008;
    public const int DWM_TNP_SOURCECLIENTAREAONLY = 0x00000010;
    
    [DllImport( "dwmapi.dll" )]
    public static extern int DwmRegisterThumbnail( IntPtr dest, IntPtr src, out IntPtr thumb );

    [DllImport( "dwmapi.dll" )]
    public static extern int DwmUnregisterThumbnail( IntPtr thumb );

    [DllImport( "dwmapi.dll" )]
    public static extern int DwmQueryThumbnailSourceSize( IntPtr thumb, out PSIZE size );

    [DllImport( "dwmapi.dll" )]
    public static extern int DwmUpdateThumbnailProperties( IntPtr hThumb, ref DWM_THUMBNAIL_PROPERTIES props );
    
    [StructLayout( LayoutKind.Sequential )]
    public struct DWM_THUMBNAIL_PROPERTIES
    {
        public int dwFlags;
        public Rect rcDestination;
        public Rect rcSource;
        public byte opacity;
        public bool fVisible;
        public bool fSourceClientAreaOnly;
    }
    
    [StructLayout( LayoutKind.Sequential )]
    public struct PSIZE
    {
        public int x;
        public int y;
    }
    
    public struct Rect
    {
        public Rect( int left, int top, int right, int bottom )
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public List<Rect> AsList() => new List<Rect> { this };

        public Rect Scale( double percentage ) =>
            new Rect(
                (int)( Left * percentage ),
                (int)( Top * percentage ),
                (int)( Right * percentage ),
                (int)( Bottom * percentage ) );

        public Rect MakeSmaller( int size ) =>
            new Rect(
                Left + size,
                Top + size,
                Right - size,
                Bottom - size );
    }
}