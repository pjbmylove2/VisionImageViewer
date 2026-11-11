using System;

namespace VisionImageViewer
{
    /// <summary>입력 버퍼를 뷰어 보관 배치로 복사합니다. 변환이 필요 없는 배치는 메모리 복사만 합니다.</summary>
    internal static unsafe class PixelConverter
    {
        public static int GetBytesPerPixel(ViewerPixelFormat format)
        {
            switch (format)
            {
                case ViewerPixelFormat.Mono8: return 1;
                case ViewerPixelFormat.Mono10:
                case ViewerPixelFormat.Mono12:
                case ViewerPixelFormat.Mono16: return 2;
                case ViewerPixelFormat.Bgr24:
                case ViewerPixelFormat.Rgb24: return 3;
                case ViewerPixelFormat.Bgra32: return 4;
                default: throw new ArgumentOutOfRangeException(nameof(format));
            }
        }

        public static void CopyIn(byte* source, int width, int height, int stride,
            ViewerPixelFormat format, FrameBuffer target)
        {
            switch (format)
            {
                case ViewerPixelFormat.Mono8:
                    target.Prepare(width, height, StoredFormat.Gray8, 8);
                    CopyRows(source, stride, target, width);
                    break;
                case ViewerPixelFormat.Mono10:
                    target.Prepare(width, height, StoredFormat.Gray16, 10);
                    CopyRows(source, stride, target, width * 2);
                    break;
                case ViewerPixelFormat.Mono12:
                    target.Prepare(width, height, StoredFormat.Gray16, 12);
                    CopyRows(source, stride, target, width * 2);
                    break;
                case ViewerPixelFormat.Mono16:
                    target.Prepare(width, height, StoredFormat.Gray16, 16);
                    CopyRows(source, stride, target, width * 2);
                    break;
                case ViewerPixelFormat.Bgr24:
                    target.Prepare(width, height, StoredFormat.Bgr24, 8);
                    CopyRows(source, stride, target, width * 3);
                    break;
                case ViewerPixelFormat.Rgb24:
                    target.Prepare(width, height, StoredFormat.Bgr24, 8);
                    CopyRgbAsBgr(source, stride, target);
                    break;
                case ViewerPixelFormat.Bgra32:
                    target.Prepare(width, height, StoredFormat.Bgra32, 8);
                    CopyRows(source, stride, target, width * 4);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(format));
            }
        }

        /// <summary>R, G, B가 따로 떨어진 평면 3장(MIL·HALCON 컬러 기본 배치)을 BGR 한 장으로 합칩니다.</summary>
        public static void CopyPlanarIn(byte* red, byte* green, byte* blue, int width, int height, int stride,
            FrameBuffer target)
        {
            target.Prepare(width, height, StoredFormat.Bgr24, 8);
            for (int y = 0; y < height; y++)
            {
                byte* r = red + (long)y * stride;
                byte* g = green + (long)y * stride;
                byte* b = blue + (long)y * stride;
                byte* d = target.Row(y);
                for (int x = 0; x < width; x++)
                {
                    d[0] = b[x];
                    d[1] = g[x];
                    d[2] = r[x];
                    d += 3;
                }
            }
        }

        private static void CopyRows(byte* source, int sourceStride, FrameBuffer target, int rowBytes)
        {
            int height = target.Height;
            int targetStride = target.Stride;
            byte* destination = (byte*)target.Data;
            if (sourceStride == targetStride)
            {
                // 줄 간격이 같으면 한 번에 복사합니다. 마지막 줄은 원본 버퍼 끝을 넘지 않도록 유효 바이트만.
                long total = (long)targetStride * (height - 1) + rowBytes;
                Buffer.MemoryCopy(source, destination, total, total);
                return;
            }
            for (int y = 0; y < height; y++)
            {
                Buffer.MemoryCopy(source + (long)y * sourceStride, destination + (long)y * targetStride,
                    rowBytes, rowBytes);
            }
        }

        private static void CopyRgbAsBgr(byte* source, int sourceStride, FrameBuffer target)
        {
            int width = target.Width;
            int height = target.Height;
            for (int y = 0; y < height; y++)
            {
                byte* s = source + (long)y * sourceStride;
                byte* d = target.Row(y);
                for (int x = 0; x < width; x++)
                {
                    d[0] = s[2];
                    d[1] = s[1];
                    d[2] = s[0];
                    s += 3;
                    d += 3;
                }
            }
        }
    }
}
