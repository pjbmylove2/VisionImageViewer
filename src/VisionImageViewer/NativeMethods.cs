using System;
using System.Runtime.InteropServices;

namespace VisionImageViewer
{
    internal static class NativeMethods
    {
        public const uint SrcCopy = 0x00CC0020;
        public const uint DibRgbColors = 0;
        public const int ColorOnColor = 3;
        public const int Halftone = 4;
        public const int BitmapInfoHeaderSize = 40;

        [StructLayout(LayoutKind.Sequential)]
        public struct BitmapInfoHeader
        {
            public uint Size;
            public int Width;
            public int Height;
            public ushort Planes;
            public ushort BitCount;
            public uint Compression;
            public uint SizeImage;
            public int XPelsPerMeter;
            public int YPelsPerMeter;
            public uint ClrUsed;
            public uint ClrImportant;
        }

        [DllImport("gdi32.dll")]
        public static extern int StretchDIBits(IntPtr hdc, int xDest, int yDest, int destWidth, int destHeight,
            int xSrc, int ySrc, int srcWidth, int srcHeight, IntPtr bits, IntPtr bitmapInfo, uint usage, uint rop);

        [DllImport("gdi32.dll")]
        public static extern int SetStretchBltMode(IntPtr hdc, int mode);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetBrushOrgEx(IntPtr hdc, int x, int y, IntPtr previous);

        /// <summary>어느 스레드에서든 호출할 수 있습니다. 다시 그리기 요청만 남기고 즉시 반환합니다.</summary>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, [MarshalAs(UnmanagedType.Bool)] bool erase);
    }
}
