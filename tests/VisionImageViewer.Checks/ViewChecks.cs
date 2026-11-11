using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VisionImageViewer.Checks
{
    /// <summary>좌표 계산, 픽셀 정확도, 픽셀 포맷, 확대/메뉴 입력 검증.</summary>
    internal static unsafe class ViewChecks
    {
        public static void Transform()
        {
            Harness.Section("좌표 계산");
            Random random = new Random(1234);
            double worstRoundTrip = 0;
            double worstAnchor = 0;
            bool fitInside = true;
            for (int i = 0; i < 2000; i++)
            {
                int imageW = random.Next(1, 20000);
                int imageH = random.Next(1, 20000);
                int clientW = random.Next(50, 2500);
                int clientH = random.Next(50, 1500);

                ViewTransform fit = ViewTransform.Create(imageW, imageH, clientW, clientH, ViewMode.Fit, 1, 0, 0);
                if (fit.OriginX < 0 || fit.OriginY < 0 ||
                    fit.OriginX + fit.DisplayWidth > clientW || fit.OriginY + fit.DisplayHeight > clientH)
                {
                    fitInside = false;
                }

                double zoom = 0.05 + random.NextDouble() * 12;
                ViewTransform t = ViewTransform.Create(imageW, imageH, clientW, clientH, ViewMode.Custom, zoom,
                    random.Next(0, 5000), random.Next(0, 5000));
                double px = random.NextDouble() * imageW;
                double py = random.NextDouble() * imageH;
                PointF d = t.ImageToDisplay(px, py);
                double bx;
                double by;
                t.DisplayToImage(d.X, d.Y, out bx, out by);
                // 화면 좌표는 float이라 큰 값에서 정밀도가 떨어집니다. 화면 픽셀 단위 오차로 비교합니다.
                worstRoundTrip = Math.Max(worstRoundTrip, Math.Abs(bx - px) * t.ScaleX);
                worstRoundTrip = Math.Max(worstRoundTrip, Math.Abs(by - py) * t.ScaleY);

                // 기준점 확대: anchor 아래 이미지 점이 확대 후에도 anchor 아래에 있어야 합니다(스크롤이 끝에 걸리지 않는 경우).
                Point anchor = new Point(random.Next(0, clientW), random.Next(0, clientH));
                double ax;
                double ay;
                t.DisplayToImage(anchor.X, anchor.Y, out ax, out ay);
                if (ax < 0 || ay < 0 || ax > imageW || ay > imageH)
                {
                    continue;
                }
                double zoom2 = zoom * 1.2;
                ViewTransform probe = ViewTransform.Create(imageW, imageH, clientW, clientH, ViewMode.Custom, zoom2, 0, 0);
                Point scroll = probe.ScrollForAnchor(ax, ay, anchor, clientW, clientH);
                bool clampedX = probe.DisplayWidth <= clientW || scroll.X == 0 || scroll.X == probe.DisplayWidth - clientW;
                bool clampedY = probe.DisplayHeight <= clientH || scroll.Y == 0 || scroll.Y == probe.DisplayHeight - clientH;
                ViewTransform after = ViewTransform.Create(imageW, imageH, clientW, clientH, ViewMode.Custom, zoom2,
                    scroll.X, scroll.Y);
                PointF moved = after.ImageToDisplay(ax, ay);
                if (!clampedX)
                {
                    worstAnchor = Math.Max(worstAnchor, Math.Abs(moved.X - anchor.X));
                }
                if (!clampedY)
                {
                    worstAnchor = Math.Max(worstAnchor, Math.Abs(moved.Y - anchor.Y));
                }
            }
            Harness.Check(fitInside, "화면 맞춤: 영상이 항상 컨트롤 안에 들어감");
            Harness.Check(worstRoundTrip <= 1.0, "이미지↔화면 왕복 오차 ≤ 1px (최대 " + worstRoundTrip.ToString("0.000") + ")");
            Harness.Check(worstAnchor <= 1.0, "기준점 확대 후 같은 지점 유지 오차 ≤ 1px (최대 " + worstAnchor.ToString("0.000") + ")");

            ViewTransform fill = ViewTransform.Create(640, 480, 300, 500, ViewMode.Fill, 1, 0, 0);
            Harness.Check(fill.DisplayWidth == 300 && fill.DisplayHeight == 500 && fill.OriginX == 0 && fill.OriginY == 0,
                "꽉 채움: 컨트롤 전체를 덮음");
        }

        public static void PixelAccuracy()
        {
            Harness.Section("픽셀 정확도 (화면 캡처 비교)");
            Color back = Color.FromArgb(10, 20, 30);
            ImageViewer viewer;
            using (Form form = Harness.CreateHost(200, 150, new ImageViewerOptions { BackColor = back, ViewMode = ViewMode.Actual }, out viewer))
            {
                // 1) 1:1, 가운데 정렬, 너비가 4의 배수가 아닌 흑백 (줄 패딩 3바이트)
                using (TestImage image = TestImage.Mono8(61, 47, 3))
                {
                    viewer.SetImage(image.Data, image.Width, image.Height, image.Stride, ViewerPixelFormat.Mono8);
                    Harness.Settle(viewer);
                    using (Bitmap shot = Harness.Snapshot(viewer))
                    {
                        int ox = (200 - 61) / 2;
                        int oy = (150 - 47) / 2;
                        int wrong = 0;
                        for (int y = 0; y < 150; y++)
                        {
                            for (int x = 0; x < 200; x++)
                            {
                                Color actual = shot.GetPixel(x, y);
                                int ix = x - ox;
                                int iy = y - oy;
                                Color expected = back;
                                if (ix >= 0 && iy >= 0 && ix < 61 && iy < 47)
                                {
                                    int v = TestImage.MonoValue(ix, iy);
                                    expected = Color.FromArgb(v, v, v);
                                }
                                if (actual.R != expected.R || actual.G != expected.G || actual.B != expected.B)
                                {
                                    wrong++;
                                }
                            }
                        }
                        Harness.Check(wrong == 0, "흑백 1:1 + 배경색: 전 픽셀 일치 (불일치 " + wrong + ")");
                    }
                }

                // 2) 컬러 3배 확대 (BGR, 너비 41 → 줄 패딩 있음)
                using (TestImage image = TestImage.Color24(41, 31, 5, false))
                {
                    viewer.SetImage(image.Data, image.Width, image.Height, image.Stride, ViewerPixelFormat.Bgr24);
                    Harness.Settle(viewer);
                    viewer.Zoom = 3.0;
                    Harness.Settle(viewer);
                    using (Bitmap shot = Harness.Snapshot(viewer))
                    {
                        int ox = (200 - 123) / 2;
                        int oy = (150 - 93) / 2;
                        int wrong = 0;
                        for (int y = 0; y < 150; y++)
                        {
                            for (int x = 0; x < 200; x++)
                            {
                                Color actual = shot.GetPixel(x, y);
                                int dx = x - ox;
                                int dy = y - oy;
                                Color expected = back;
                                if (dx >= 0 && dy >= 0 && dx < 123 && dy < 93)
                                {
                                    int ix = dx / 3;
                                    int iy = dy / 3;
                                    expected = Color.FromArgb((byte)ix, (byte)iy, (byte)(ix + iy));
                                }
                                if (actual.R != expected.R || actual.G != expected.G || actual.B != expected.B)
                                {
                                    wrong++;
                                }
                            }
                        }
                        Harness.Check(wrong == 0, "컬러 3배 확대: 전 픽셀 일치 (불일치 " + wrong + ")");
                    }
                }

                // 3) 4배 확대 + 스크롤: 화면보다 큰 영상의 일부만 그릴 때 줄/열이 밀리지 않는지
                using (TestImage image = TestImage.Mono8(203, 157, 1))
                {
                    viewer.SetImage(image.Data, image.Width, image.Height, image.Stride, ViewerPixelFormat.Mono8);
                    Harness.Settle(viewer);
                    viewer.Zoom = 4.0;
                    Harness.Settle(viewer);
                    Size client = viewer.ClientSize;
                    Harness.Check(client.Width < viewer.Width && client.Height < viewer.Height,
                        "확대하면 가로/세로 스크롤바가 자동으로 생김");
                    foreach (Point scroll in new[] { new Point(0, 0), new Point(123, 77), new Point(401, 310), new Point(5000, 5000) })
                    {
                        viewer.AutoScrollPosition = scroll;
                        Harness.Settle(viewer);
                        Point actualScroll = viewer.AutoScrollPosition;
                        int sx = -actualScroll.X;
                        int sy = -actualScroll.Y;
                        using (Bitmap shot = Harness.Snapshot(viewer))
                        {
                            int wrong = 0;
                            for (int y = 0; y < client.Height; y++)
                            {
                                for (int x = 0; x < client.Width; x++)
                                {
                                    int v = TestImage.MonoValue((x + sx) / 4, (y + sy) / 4);
                                    if (shot.GetPixel(x, y).R != v)
                                    {
                                        wrong++;
                                    }
                                }
                            }
                            Harness.Check(wrong == 0, "4배 확대, 스크롤 (" + sx + "," + sy + "): 전 픽셀 일치 (불일치 " + wrong + ")");
                        }
                    }
                }

                // 4) 화면 맞춤(축소): 영상 영역과 배경 영역이 맞게 나뉘는지
                using (TestImage image = TestImage.Mono8(1600, 600, 0))
                {
                    viewer.SetImage(image.Data, image.Width, image.Height, image.Stride, ViewerPixelFormat.Mono8);
                    viewer.ViewMode = ViewMode.Fit;
                    Harness.Settle(viewer);
                    using (Bitmap shot = Harness.Snapshot(viewer))
                    {
                        // 1600x600 → 200x75, 세로 가운데 정렬 (위아래 배경)
                        Color top = shot.GetPixel(100, 5);
                        Color bottom = shot.GetPixel(100, 145);
                        Harness.Check(top.R == back.R && top.G == back.G && top.B == back.B &&
                                      bottom.R == back.R && bottom.B == back.B, "화면 맞춤: 영상 밖은 배경색");
                        Harness.Check(Math.Abs(viewer.Zoom - 0.125) < 0.001, "화면 맞춤 배율 0.125 (실제 " + viewer.Zoom.ToString("0.0000") + ")");
                    }
                }
            }
        }

        public static void Formats()
        {
            Harness.Section("픽셀 포맷");
            ImageViewer viewer;
            using (Form form = Harness.CreateHost(160, 120, new ImageViewerOptions { ViewMode = ViewMode.Actual }, out viewer))
            {
                PixelValue value;

                // RGB 순서 입력
                using (TestImage image = TestImage.Color24(33, 21, 2, true))
                {
                    viewer.SetImage(image.Data, image.Width, image.Height, image.Stride, ViewerPixelFormat.Rgb24);
                    Harness.Settle(viewer);
                    bool ok = viewer.TryGetPixel(20, 10, out value) && value.Channels == 3 &&
                              value.V0 == 20 && value.V1 == 10 && value.V2 == 30;
                    using (Bitmap shot = Harness.Snapshot(viewer))
                    {
                        Color c = shot.GetPixel((160 - 33) / 2 + 20, (120 - 21) / 2 + 10);
                        ok = ok && c.R == 20 && c.G == 10 && c.B == 30;
                    }
                    Harness.Check(ok, "RGB24: 값 읽기와 화면 색 일치");
                }

                // BGRA
                int w = 30;
                int h = 20;
                IntPtr bgra = Marshal.AllocHGlobal(w * h * 4);
                try
                {
                    byte* p = (byte*)bgra;
                    for (int i = 0; i < w * h; i++)
                    {
                        p[i * 4] = 200; p[i * 4 + 1] = 100; p[i * 4 + 2] = 50; p[i * 4 + 3] = 7;
                    }
                    viewer.SetImage(bgra, w, h, w * 4, ViewerPixelFormat.Bgra32);
                    Harness.Settle(viewer);
                    bool ok = viewer.TryGetPixel(3, 3, out value) && value.Channels == 4 &&
                              value.V0 == 50 && value.V1 == 100 && value.V2 == 200 && value.V3 == 7;
                    using (Bitmap shot = Harness.Snapshot(viewer))
                    {
                        Color c = shot.GetPixel(80, 60);
                        ok = ok && c.R == 50 && c.G == 100 && c.B == 200;
                    }
                    Harness.Check(ok, "BGRA32: 값 읽기와 화면 색 일치(알파 무시)");
                }
                finally
                {
                    Marshal.FreeHGlobal(bgra);
                }

                // 16bit 계열: 원본 값은 그대로 읽히고, 화면에는 상위 8bit가 보입니다.
                CheckMono16(viewer, ViewerPixelFormat.Mono16, 16);
                CheckMono16(viewer, ViewerPixelFormat.Mono12, 12);
                CheckMono16(viewer, ViewerPixelFormat.Mono10, 10);

                // 평면 RGB (MIL/HALCON 배치)
                int pw = 37;
                int ph = 19;
                int pstride = 40;
                IntPtr r = Marshal.AllocHGlobal(pstride * ph);
                IntPtr gPlane = Marshal.AllocHGlobal(pstride * ph);
                IntPtr b = Marshal.AllocHGlobal(pstride * ph);
                try
                {
                    for (int y = 0; y < ph; y++)
                    {
                        for (int x = 0; x < pw; x++)
                        {
                            ((byte*)r)[y * pstride + x] = (byte)(x * 3);
                            ((byte*)gPlane)[y * pstride + x] = (byte)(y * 5);
                            ((byte*)b)[y * pstride + x] = (byte)(x + y);
                        }
                    }
                    viewer.SetImagePlanar(r, gPlane, b, pw, ph, pstride);
                    Harness.Settle(viewer);
                    bool ok = viewer.TryGetPixel(30, 11, out value) && value.V0 == 90 && value.V1 == 55 && value.V2 == 41;
                    Harness.Check(ok, "평면 RGB: 한 장으로 합쳐진 값 일치");
                }
                finally
                {
                    Marshal.FreeHGlobal(r);
                    Marshal.FreeHGlobal(gPlane);
                    Marshal.FreeHGlobal(b);
                }

                // 관리 배열 입력 (Pylon PixelData 형태)
                byte[] managed = new byte[25 * 15];
                for (int i = 0; i < managed.Length; i++)
                {
                    managed[i] = (byte)(i % 251);
                }
                viewer.SetImage(managed, 25, 15, 25, ViewerPixelFormat.Mono8);
                Harness.Settle(viewer);
                Harness.Check(viewer.TryGetPixel(7, 9, out value) && value.V0 == (9 * 25 + 7) % 251, "byte[] 입력: 값 일치");

                // 아래에서 위로 저장된 버퍼 (음수 stride)
                using (TestImage image = TestImage.Mono8(40, 30, 0))
                {
                    IntPtr lastRow = new IntPtr((byte*)image.Data + (long)(image.Height - 1) * image.Stride);
                    viewer.SetImage(lastRow, image.Width, image.Height, -image.Stride, ViewerPixelFormat.Mono8);
                    Harness.Settle(viewer);
                    Harness.Check(viewer.TryGetPixel(5, 0, out value) && value.V0 == TestImage.MonoValue(5, 29),
                        "음수 stride(아래→위 저장): 위아래가 뒤집혀 들어옴");
                }

                // GDI+ Bitmap 입력
                using (Bitmap bitmap = new Bitmap(20, 10, PixelFormat.Format24bppRgb))
                {
                    bitmap.SetPixel(4, 6, Color.FromArgb(11, 22, 33));
                    viewer.SetImage(bitmap);
                    Harness.Settle(viewer);
                    Harness.Check(viewer.TryGetPixel(4, 6, out value) && value.V0 == 11 && value.V1 == 22 && value.V2 == 33,
                        "Bitmap(24bit) 입력: 값 일치");
                }

                // 잘못된 인자
                bool threw = false;
                try
                {
                    viewer.SetImage(new byte[10], 100, 100, 100, ViewerPixelFormat.Mono8);
                }
                catch (ArgumentException)
                {
                    threw = true;
                }
                Harness.Check(threw, "배열이 영상보다 작으면 예외로 알림");
            }
        }

        private static void CheckMono16(ImageViewer viewer, ViewerPixelFormat format, int bits)
        {
            int w = 64;
            int h = 16;
            int stride = w * 2 + 2;
            IntPtr data = Marshal.AllocHGlobal(stride * h);
            try
            {
                int max = (1 << bits) - 1;
                for (int y = 0; y < h; y++)
                {
                    ushort* row = (ushort*)((byte*)data + y * stride);
                    for (int x = 0; x < w; x++)
                    {
                        row[x] = (ushort)(max * x / (w - 1));
                    }
                }
                viewer.SetImage(data, w, h, stride, format);
                Harness.Settle(viewer);
                PixelValue value;
                int expectedRaw = max * 40 / (w - 1);
                bool ok = viewer.TryGetPixel(40, 5, out value) && value.Channels == 1 && value.V0 == expectedRaw;
                using (Bitmap shot = Harness.Snapshot(viewer))
                {
                    Color c = shot.GetPixel((160 - w) / 2 + 40, (120 - h) / 2 + 5);
                    ok = ok && c.R == (expectedRaw >> (bits - 8)) && c.R == c.G && c.G == c.B;
                    Color last = shot.GetPixel((160 - w) / 2 + w - 1, (120 - h) / 2 + 5);
                    ok = ok && last.R == 255;
                }
                Harness.Check(ok, format + ": 원본 값 보존, 화면은 상위 8bit");
            }
            finally
            {
                Marshal.FreeHGlobal(data);
            }
        }

        public static void Input()
        {
            Harness.Section("확대/축소 입력과 메뉴");
            ImageViewer viewer;
            using (Form form = Harness.CreateHost(300, 200, null, out viewer))
            using (TestImage image = TestImage.Mono8(600, 400, 0))
            {
                viewer.SetImage(image.Data, image.Width, image.Height, image.Stride, ViewerPixelFormat.Mono8);
                Harness.Settle(viewer);
                Harness.Check(viewer.ViewMode == ViewMode.Fit && Math.Abs(viewer.Zoom - 0.5) < 0.001, "처음은 화면 맞춤 (배율 0.5)");
                Harness.Check(viewer.ClientSize == viewer.Size, "화면 맞춤에서는 스크롤바 없음");

                // 휠 확대: 마우스 아래 지점 유지
                Point anchor = new Point(210, 60);
                PointF before = viewer.ClientToImage(anchor);
                Harness.SendWheel(viewer, 120, anchor);
                Harness.Settle(viewer);
                Harness.Check(Math.Abs(viewer.Zoom - 0.6) < 0.001, "휠 한 칸 위: 배율 ×1.2 (실제 " + viewer.Zoom.ToString("0.000") + ")");
                for (int i = 0; i < 9; i++)
                {
                    Harness.SendWheel(viewer, 120, anchor);
                }
                Harness.Settle(viewer);
                PointF after = viewer.ClientToImage(anchor);
                double drift = Math.Max(Math.Abs(after.X - before.X), Math.Abs(after.Y - before.Y)) * viewer.Zoom;
                Harness.Check(drift <= 1.5, "휠 확대 10칸 후 마우스 아래 지점 유지 (이동 " + drift.ToString("0.00") + "px)");
                Harness.Check(viewer.ClientSize.Width < viewer.Width && viewer.ClientSize.Height < viewer.Height,
                    "휠 확대로 영상이 커지면 스크롤바 자동 표시");

                double zoomed = viewer.Zoom;
                Harness.SendWheel(viewer, -120, anchor);
                Harness.Check(viewer.Zoom < zoomed, "휠 한 칸 아래: 축소");

                // 키보드 +/-
                zoomed = viewer.Zoom;
                Harness.SendKey(viewer, Harness.VkAdd);
                Harness.Check(Math.Abs(viewer.Zoom - zoomed * 1.2) < 0.01, "+ 키: 확대");
                Harness.SendKey(viewer, Harness.VkSubtract);
                Harness.Check(Math.Abs(viewer.Zoom - zoomed) < 0.01, "- 키: 축소");

                // 검사 모드: 휠·키 잠금
                viewer.InspectionMode = true;
                zoomed = viewer.Zoom;
                Harness.SendWheel(viewer, 120, anchor);
                Harness.SendKey(viewer, Harness.VkAdd);
                Harness.SendKey(viewer, Harness.VkSubtract);
                Harness.SendWheel(viewer, -120, anchor);
                Harness.Check(viewer.Zoom == zoomed, "검사 모드: 휠과 +/- 키로 배율이 바뀌지 않음");

                // 검사 모드에서도 우클릭 메뉴의 보기 전환은 동작
                ToolStripItemCollection items = viewer.ViewMenu.Items;
                Harness.Check(viewer.ContextMenuStrip == viewer.ViewMenu && items.Count >= 10, "우클릭 메뉴가 연결되어 있음");
                items[2].PerformClick();
                Harness.Check(viewer.ViewMode == ViewMode.Actual && Math.Abs(viewer.Zoom - 1.0) < 1e-9, "메뉴 '원본 크기': 1:1");
                items[1].PerformClick();
                Harness.Settle(viewer);
                Harness.Check(viewer.ViewMode == ViewMode.Fill && viewer.ClientSize == viewer.Size, "메뉴 '꽉 채움': 스크롤바 사라짐");
                items[0].PerformClick();
                Harness.Check(viewer.ViewMode == ViewMode.Fit, "메뉴 '화면 맞춤'");
                viewer.InspectionMode = false;

                // 배율 한계
                viewer.Zoom = 100000;
                Harness.Check(viewer.Zoom <= viewer.MaxZoom + 1e-9, "최대 배율 제한");
                viewer.Zoom = 0.0000001;
                Harness.Settle(viewer);
                Harness.Check(viewer.Zoom >= viewer.MinZoom * 0.5, "최소 배율 제한");

                // 픽셀 정보 이벤트
                viewer.ViewMode = ViewMode.Actual;
                Harness.Settle(viewer);
                viewer.AutoScrollPosition = new Point(0, 0);
                Harness.Settle(viewer);
                PixelHoverEventArgs hover = null;
                viewer.PixelHover += (s, e) => hover = e;
                Harness.SendMessage(viewer.Handle, 0x0200, IntPtr.Zero, new IntPtr((25 << 16) | 40));   // WM_MOUSEMOVE (40,25)
                Harness.Check(hover != null && hover.IsInsideImage && hover.X == 40 && hover.Y == 25 &&
                              hover.Value.V0 == TestImage.MonoValue(40, 25), "마우스 위치의 이미지 좌표와 픽셀 값 통지");

                // 영상 지우기
                viewer.ClearImage();
                Harness.Settle(viewer);
                Harness.Check(!viewer.HasImage && viewer.ClientSize == viewer.Size, "ClearImage: 영상과 스크롤바 제거");
            }
        }
    }
}
