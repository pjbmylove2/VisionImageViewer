using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace VisionImageViewer
{
    /// <summary>GDI+ Bitmap과 뷰어 프레임 사이의 변환, 기본 파일 열기/저장을 맡습니다.</summary>
    internal static unsafe class ImageFileCodec
    {
        /// <summary>Bitmap 픽셀을 뷰어에 넣습니다. 바로 쓸 수 없는 배치는 24bit로 바꿔서 넣습니다.</summary>
        public static void Submit(ImageViewer viewer, Bitmap bitmap, IReadOnlyList<OverlayItem> overlays)
        {
            ViewerPixelFormat format;
            Bitmap converted = null;
            Bitmap source = bitmap;
            switch (bitmap.PixelFormat)
            {
                case PixelFormat.Format24bppRgb:
                    format = ViewerPixelFormat.Bgr24;
                    break;
                case PixelFormat.Format32bppArgb:
                case PixelFormat.Format32bppRgb:
                case PixelFormat.Format32bppPArgb:
                    format = ViewerPixelFormat.Bgra32;
                    break;
                case PixelFormat.Format8bppIndexed:
                    if (HasGrayPalette(bitmap))
                    {
                        format = ViewerPixelFormat.Mono8;
                    }
                    else
                    {
                        converted = ConvertTo24(bitmap);
                        source = converted;
                        format = ViewerPixelFormat.Bgr24;
                    }
                    break;
                default:
                    converted = ConvertTo24(bitmap);
                    source = converted;
                    format = ViewerPixelFormat.Bgr24;
                    break;
            }

            try
            {
                Rectangle bounds = new Rectangle(0, 0, source.Width, source.Height);
                BitmapData data = source.LockBits(bounds, ImageLockMode.ReadOnly, source.PixelFormat);
                try
                {
                    viewer.SetImage(data.Scan0, data.Width, data.Height, data.Stride, format, overlays);
                }
                finally
                {
                    source.UnlockBits(data);
                }
            }
            finally
            {
                if (converted != null)
                {
                    converted.Dispose();
                }
            }
        }

        private static bool HasGrayPalette(Bitmap bitmap)
        {
            Color[] entries = bitmap.Palette.Entries;
            if (entries.Length < 256)
            {
                return false;
            }
            for (int i = 0; i < 256; i++)
            {
                Color c = entries[i];
                if (c.R != i || c.G != i || c.B != i)
                {
                    return false;
                }
            }
            return true;
        }

        private static Bitmap ConvertTo24(Bitmap bitmap)
        {
            Bitmap result = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                // 크기를 직접 지정해 파일의 DPI 값 때문에 늘어나거나 줄어들지 않게 합니다.
                g.DrawImage(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
            }
            return result;
        }

        /// <summary>GDI+가 읽을 수 있는 파일(bmp, jpg, png, gif, 8bit tif, ico)을 해석해 뷰어에 넣습니다.</summary>
        public static void DecodeWithGdiPlus(byte[] fileBytes, ImageViewer viewer)
        {
            using (MemoryStream stream = new MemoryStream(fileBytes))
            using (Image image = Image.FromStream(stream))
            {
                Bitmap bitmap = image as Bitmap;
                if (bitmap != null)
                {
                    Submit(viewer, bitmap, null);
                    return;
                }
                using (Bitmap rendered = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb))
                {
                    using (Graphics g = Graphics.FromImage(rendered))
                    {
                        g.Clear(Color.White);
                        g.DrawImage(image, new Rectangle(0, 0, rendered.Width, rendered.Height));
                    }
                    Submit(viewer, rendered, null);
                }
            }
        }

        /// <summary>프레임을 새 Bitmap으로 복사합니다. 16bit는 화면에 보이는 것과 같은 8bit로 줄입니다.</summary>
        public static Bitmap ToBitmap(FrameBuffer frame)
        {
            PixelFormat format;
            switch (frame.Format)
            {
                case StoredFormat.Bgr24:
                    format = PixelFormat.Format24bppRgb;
                    break;
                case StoredFormat.Bgra32:
                    format = PixelFormat.Format32bppRgb;
                    break;
                default:
                    format = PixelFormat.Format8bppIndexed;
                    break;
            }

            Bitmap bitmap = new Bitmap(frame.Width, frame.Height, format);
            try
            {
                if (format == PixelFormat.Format8bppIndexed)
                {
                    ColorPalette palette = bitmap.Palette;
                    for (int i = 0; i < 256; i++)
                    {
                        palette.Entries[i] = Color.FromArgb(i, i, i);
                    }
                    bitmap.Palette = palette;
                }

                Rectangle bounds = new Rectangle(0, 0, frame.Width, frame.Height);
                BitmapData data = bitmap.LockBits(bounds, ImageLockMode.WriteOnly, format);
                try
                {
                    CopyRows(frame, (byte*)data.Scan0, data.Stride);
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }
                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }

        private static void CopyRows(FrameBuffer frame, byte* destination, int destinationStride)
        {
            int width = frame.Width;
            if (frame.Format == StoredFormat.Gray16)
            {
                int shift = frame.BitDepth - 8;
                for (int y = 0; y < frame.Height; y++)
                {
                    ushort* s = (ushort*)frame.Row(y);
                    byte* d = destination + (long)y * destinationStride;
                    for (int x = 0; x < width; x++)
                    {
                        int value = s[x] >> shift;
                        d[x] = (byte)(value > 255 ? 255 : value);
                    }
                }
                return;
            }
            int rowBytes = width * frame.BytesPerPixel;
            for (int y = 0; y < frame.Height; y++)
            {
                Buffer.MemoryCopy(frame.Row(y), destination + (long)y * destinationStride, rowBytes, rowBytes);
            }
        }

        public static ImageFormat FormatFromPath(string path)
        {
            string extension = (Path.GetExtension(path) ?? string.Empty).ToLowerInvariant();
            switch (extension)
            {
                case ".bmp": return ImageFormat.Bmp;
                case ".jpg":
                case ".jpeg": return ImageFormat.Jpeg;
                case ".tif":
                case ".tiff": return ImageFormat.Tiff;
                case ".gif": return ImageFormat.Gif;
                default: return ImageFormat.Png;
            }
        }
    }
}
