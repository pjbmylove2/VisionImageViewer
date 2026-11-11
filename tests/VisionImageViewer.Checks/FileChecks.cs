using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace VisionImageViewer.Checks
{
    /// <summary>오버레이 그리기, 파일 열기/저장(한글 경로 포함) 검증.</summary>
    internal static class FileChecks
    {
        public static void Overlays(string artifactFolder)
        {
            Harness.Section("오버레이");
            ImageViewer viewer;
            using (Form form = Harness.CreateHost(400, 300, new ImageViewerOptions { ViewMode = ViewMode.Actual }, out viewer))
            using (TestImage image = TestImage.Mono8(200, 150, 0))
            {
                OverlayItem[] overlays =
                {
                    OverlayItem.Rect(new RectangleF(50, 40, 60, 30), Color.Red, 3F),
                    OverlayItem.Line(new PointF(0, 0), new PointF(200, 150), Color.Yellow, 1F),
                    OverlayItem.Cross(new PointF(150, 100), 20, Color.Cyan, 1F),
                    OverlayItem.Ellipse(new RectangleF(120, 20, 40, 40), Color.Lime, 2F, 60),
                    OverlayItem.Text(new PointF(52, 72), "불량 #1", Color.Red),
                    OverlayItem.Label("NG", Color.Red, ContentAlignment.TopRight)
                };
                viewer.SetImage(image.Data, image.Width, image.Height, image.Stride, ViewerPixelFormat.Mono8, overlays);
                Harness.Settle(viewer);
                using (Bitmap shot = Harness.Snapshot(viewer))
                {
                    // 사각형 윗변: 이미지 (80, 40) → 화면 (100+80, 75+40)
                    Color edge = shot.GetPixel(100 + 80, 75 + 40);
                    Harness.Check(edge.R > 200 && edge.G < 60 && edge.B < 60, "사각형 테두리가 이미지 좌표 위치에 그려짐");
                    shot.Save(Path.Combine(artifactFolder, "overlay-1x.png"));
                }

                // 확대해도 도형이 같은 이미지 위치를 따라가는지
                viewer.ZoomAt(3.0, new Point(200, 150));
                Harness.Settle(viewer);
                PointF corner = viewer.ImageToClient(new PointF(80, 40));
                using (Bitmap shot = Harness.Snapshot(viewer))
                {
                    int x = (int)Math.Round(corner.X);
                    int y = (int)Math.Round(corner.Y);
                    bool found = false;
                    if (x >= 0 && y >= 0 && x < shot.Width && y < shot.Height)
                    {
                        Color edge = shot.GetPixel(x, y);
                        found = edge.R > 200 && edge.G < 60 && edge.B < 60;
                    }
                    Harness.Check(found, "3배 확대 후에도 테두리가 같은 이미지 위치에 있음");
                    shot.Save(Path.Combine(artifactFolder, "overlay-3x.png"));
                }

                using (Bitmap withOverlays = viewer.CaptureBitmap(true))
                using (Bitmap plain = viewer.CaptureBitmap(false))
                {
                    Color drawn = withOverlays.GetPixel(80, 40);
                    Color original = plain.GetPixel(80, 40);
                    int expected = TestImage.MonoValue(80, 40);
                    Harness.Check(withOverlays.Width == 200 && drawn.R > 200 && drawn.G < 60 && original.R == expected && original.G == expected,
                        "CaptureBitmap: 표시 내용 포함/원본만 구분");
                }
            }
        }

        public static void Files(string artifactFolder)
        {
            Harness.Section("파일 열기/저장");
            string folder = Path.Combine(artifactFolder, "한글 경로");
            Directory.CreateDirectory(folder);
            ImageViewer viewer;
            using (Form form = Harness.CreateHost(200, 150, new ImageViewerOptions { ViewMode = ViewMode.Actual }, out viewer))
            {
                using (TestImage image = TestImage.Mono8(70, 50, 2))
                {
                    viewer.SetImage(image.Data, image.Width, image.Height, image.Stride, ViewerPixelFormat.Mono8);
                    Harness.Settle(viewer);
                }
                foreach (string name in new[] { "흑백.png", "흑백.bmp", "흑백.tif" })
                {
                    string path = Path.Combine(folder, name);
                    bool saved = viewer.SaveFile(path, false);
                    viewer.ClearImage();
                    Harness.Settle(viewer);
                    bool loaded = viewer.LoadFile(path);
                    Harness.Settle(viewer);
                    PixelValue value;
                    bool same = viewer.ImageSize == new Size(70, 50) && viewer.TryGetPixel(33, 21, out value) &&
                                value.V0 == TestImage.MonoValue(33, 21);
                    Harness.Check(saved && loaded && same, name + ": 저장 후 다시 열어 값 일치");
                }

                using (TestImage image = TestImage.Color24(64, 40, 0, false))
                {
                    viewer.SetImage(image.Data, image.Width, image.Height, image.Stride, ViewerPixelFormat.Bgr24);
                    Harness.Settle(viewer);
                }
                string colorPath = Path.Combine(folder, "컬러.png");
                bool colorOk = viewer.SaveFile(colorPath, false) && viewer.LoadFile(colorPath);
                Harness.Settle(viewer);
                PixelValue color;
                Harness.Check(colorOk && viewer.TryGetPixel(20, 10, out color) && color.V0 == 20 && color.V1 == 10 && color.V2 == 30,
                    "컬러.png: 저장 후 다시 열어 색 일치");

                Harness.Check(!viewer.LoadFile(Path.Combine(folder, "없는파일.png")) && viewer.LastFileError != null,
                    "없는 파일: false 반환 + 원인 문구");
                string broken = Path.Combine(folder, "깨진파일.png");
                File.WriteAllBytes(broken, new byte[] { 1, 2, 3, 4, 5 });
                Harness.Check(!viewer.LoadFile(broken) && viewer.LastFileError != null, "깨진 파일: false 반환 + 원인 문구");

                // 외부 디코더 훅
                bool called = false;
                viewer.FileDecoder = (path, bytes, target) =>
                {
                    called = true;
                    target.SetImage(new byte[] { 9, 9, 9, 9 }, 2, 2, 2, ViewerPixelFormat.Mono8);
                    return true;
                };
                bool hooked = viewer.LoadFile(broken);
                Harness.Settle(viewer);
                Harness.Check(hooked && called && viewer.ImageSize == new Size(2, 2), "외부 디코더를 꽂으면 그쪽이 먼저 처리");
            }
        }
    }
}
