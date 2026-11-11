using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace VisionImageViewer.Checks
{
    /// <summary>검증 공통 도구: 결과 집계, 테스트용 창, 화면 캡처, 메시지 처리.</summary>
    internal static class Harness
    {
        public const int WmKeyDown = 0x0100;
        public const int WmMouseWheel = 0x020A;
        public const int VkAdd = 0x6B;
        public const int VkSubtract = 0x6D;

        private static int _passed;
        private static int _failed;

        public static int Failed
        {
            get { return _failed; }
        }

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern int GetGuiResources(IntPtr process, int flags);

        public static void Section(string name)
        {
            Console.WriteLine();
            Console.WriteLine("== " + name + " ==");
        }

        public static void Check(bool condition, string name)
        {
            if (condition)
            {
                _passed++;
                Console.WriteLine("  [통과] " + name);
            }
            else
            {
                _failed++;
                Console.WriteLine("  [실패] " + name);
            }
        }

        public static void Summary()
        {
            Console.WriteLine();
            Console.WriteLine("결과: 통과 " + _passed + ", 실패 " + _failed);
        }

        /// <summary>테두리 없는 창에 뷰어 하나를 꽉 채워 올립니다. 클라이언트 크기가 곧 뷰어 크기입니다.</summary>
        public static Form CreateHost(int width, int height, ImageViewerOptions options, out ImageViewer viewer)
        {
            Form form = new Form();
            form.FormBorderStyle = FormBorderStyle.None;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(0, 0);
            form.ShowInTaskbar = false;
            form.ClientSize = new Size(width, height);
            viewer = new ImageViewer(options);
            viewer.Dock = DockStyle.Fill;
            form.Controls.Add(viewer);
            form.Show();
            Application.DoEvents();
            return form;
        }

        /// <summary>대기 중인 프레임이 화면에 반영될 때까지 그리기를 강제합니다.</summary>
        public static void Settle(ImageViewer viewer)
        {
            for (int i = 0; i < 3; i++)
            {
                viewer.Refresh();
                Application.DoEvents();
            }
        }

        public static bool PumpUntil(Func<bool> condition, int timeoutMs)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                Application.DoEvents();
                if (condition())
                {
                    return true;
                }
                Thread.Sleep(1);
            }
            return condition();
        }

        /// <summary>컨트롤이 실제로 그리는 내용을 Bitmap으로 받습니다(창이 가려져 있어도 동작).</summary>
        public static Bitmap Snapshot(ImageViewer viewer)
        {
            Bitmap bitmap = new Bitmap(viewer.Width, viewer.Height, PixelFormat.Format32bppArgb);
            viewer.DrawToBitmap(bitmap, new Rectangle(0, 0, viewer.Width, viewer.Height));
            return bitmap;
        }

        public static void SendWheel(ImageViewer viewer, int delta, Point clientPoint)
        {
            Point screen = viewer.PointToScreen(clientPoint);
            IntPtr wParam = new IntPtr((long)delta << 16);
            IntPtr lParam = new IntPtr((long)(((screen.Y & 0xFFFF) << 16) | (screen.X & 0xFFFF)));
            SendMessage(viewer.Handle, WmMouseWheel, wParam, lParam);
        }

        public static void SendKey(ImageViewer viewer, int virtualKey)
        {
            SendMessage(viewer.Handle, WmKeyDown, new IntPtr(virtualKey), IntPtr.Zero);
        }
    }

    /// <summary>검증용 영상 버퍼(네이티브 메모리). 줄 간격에 패딩을 줄 수 있습니다.</summary>
    internal sealed unsafe class TestImage : IDisposable
    {
        public IntPtr Data { get; private set; }
        public int Width { get; }
        public int Height { get; }
        public int Stride { get; }
        public int BytesPerPixel { get; }

        public TestImage(int width, int height, int bytesPerPixel, int padding)
        {
            Width = width;
            Height = height;
            BytesPerPixel = bytesPerPixel;
            Stride = width * bytesPerPixel + padding;
            Data = Marshal.AllocHGlobal(new IntPtr((long)Stride * height));
            byte* p = (byte*)Data;
            long total = (long)Stride * height;
            for (long i = 0; i < total; i++)
            {
                p[i] = 0xEE;    // 패딩이 화면에 섞여 나오면 바로 드러나도록 눈에 띄는 값으로 채웁니다.
            }
        }

        public byte* Pixel(int x, int y)
        {
            return (byte*)Data + (long)y * Stride + (long)x * BytesPerPixel;
        }

        public static byte MonoValue(int x, int y)
        {
            return (byte)((x * 7 + y * 13) & 255);
        }

        public static TestImage Mono8(int width, int height, int padding)
        {
            TestImage image = new TestImage(width, height, 1, padding);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    *image.Pixel(x, y) = MonoValue(x, y);
                }
            }
            return image;
        }

        /// <summary>order: "BGR" 또는 "RGB". 색은 R=x, G=y, B=x+y (각 8bit).</summary>
        public static TestImage Color24(int width, int height, int padding, bool rgbOrder)
        {
            TestImage image = new TestImage(width, height, 3, padding);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    byte* p = image.Pixel(x, y);
                    byte r = (byte)x;
                    byte g = (byte)y;
                    byte b = (byte)(x + y);
                    if (rgbOrder)
                    {
                        p[0] = r; p[1] = g; p[2] = b;
                    }
                    else
                    {
                        p[0] = b; p[1] = g; p[2] = r;
                    }
                }
            }
            return image;
        }

        public void Dispose()
        {
            if (Data != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(Data);
                Data = IntPtr.Zero;
            }
        }
    }
}
