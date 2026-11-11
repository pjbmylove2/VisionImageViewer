using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using OpenCvSharp;

namespace VisionImageViewer.Checks
{
    /// <summary>OpenCvSharp 어댑터: Mat 입력과 OpenCV 파일 디코더 검증.</summary>
    internal static class AdapterChecks
    {
        public static void Run(string artifactFolder)
        {
            Harness.Section("OpenCvSharp 어댑터");
            ImageViewer viewer;
            using (Form form = Harness.CreateHost(200, 150, new ImageViewerOptions { ViewMode = ViewMode.Actual }, out viewer))
            {
                PixelValue value;

                using (Mat gray = new Mat(40, 61, MatType.CV_8UC1))
                {
                    for (int y = 0; y < gray.Rows; y++)
                    {
                        for (int x = 0; x < gray.Cols; x++)
                        {
                            gray.Set(y, x, TestImage.MonoValue(x, y));
                        }
                    }
                    bool accepted = viewer.SetImage(gray);
                    Harness.Settle(viewer);
                    Harness.Check(accepted && viewer.ImageSize == new System.Drawing.Size(61, 40) &&
                                  viewer.TryGetPixel(33, 21, out value) && value.V0 == TestImage.MonoValue(33, 21),
                        "Mat CV_8UC1: 값 일치");

                    // ROI로 잘라낸 Mat(메모리가 이어져 있지 않음)
                    using (Mat roi = new Mat(gray, new Rect(10, 5, 30, 20)))
                    {
                        viewer.SetImage(roi);
                        Harness.Settle(viewer);
                        Harness.Check(viewer.ImageSize == new System.Drawing.Size(30, 20) &&
                                      viewer.TryGetPixel(7, 9, out value) && value.V0 == TestImage.MonoValue(17, 14),
                            "Mat ROI(부분 영역): 잘라낸 부분만 정확히 들어옴");
                    }
                }

                using (Mat bgr = new Mat(30, 45, MatType.CV_8UC3, new Scalar(200, 100, 50)))
                {
                    viewer.SetImage(bgr);
                    Harness.Settle(viewer);
                    Harness.Check(viewer.TryGetPixel(5, 5, out value) && value.Channels == 3 &&
                                  value.V0 == 50 && value.V1 == 100 && value.V2 == 200, "Mat CV_8UC3(BGR): 색 일치");
                }

                using (Mat deep = new Mat(20, 20, MatType.CV_16UC1, new Scalar(51234)))
                {
                    viewer.SetImage(deep);
                    Harness.Settle(viewer);
                    Harness.Check(viewer.TryGetPixel(3, 3, out value) && value.V0 == 51234, "Mat CV_16UC1: 16bit 원본 값 보존");

                    // 12bit 영상이 16bit Mat에 담긴 경우: 배치를 직접 지정
                    using (Mat twelve = new Mat(20, 20, MatType.CV_16UC1, new Scalar(4095)))
                    {
                        viewer.SetImage(twelve, ViewerPixelFormat.Mono12);
                        Harness.Settle(viewer);
                        using (Bitmap shot = Harness.Snapshot(viewer))
                        {
                            Harness.Check(shot.GetPixel(100, 75).R == 255, "Mat + Mono12 지정: 4095가 화면에서 흰색");
                        }
                    }
                }

                using (Mat empty = new Mat())
                {
                    Harness.Check(!viewer.SetImage(empty) && !viewer.SetImage((Mat)null), "빈 Mat/null: 무시하고 false");
                }
                bool threw = false;
                using (Mat floating = new Mat(4, 4, MatType.CV_32FC1, new Scalar(0.5)))
                {
                    try
                    {
                        viewer.SetImage(floating);
                    }
                    catch (NotSupportedException)
                    {
                        threw = true;
                    }
                }
                Harness.Check(threw, "지원하지 않는 Mat 형식(CV_32F): 예외로 알림");

                // 파일 디코더: 한글 경로의 16bit tif
                string folder = Path.Combine(artifactFolder, "한글 경로");
                Directory.CreateDirectory(folder);
                string tif = Path.Combine(folder, "16비트.tif");
                using (Mat deep = new Mat(32, 48, MatType.CV_16UC1))
                {
                    for (int y = 0; y < deep.Rows; y++)
                    {
                        for (int x = 0; x < deep.Cols; x++)
                        {
                            deep.Set(y, x, (ushort)(x * 1000 + y));
                        }
                    }
                    byte[] encoded;
                    Cv2.ImEncode(".tif", deep, out encoded);
                    File.WriteAllBytes(tif, encoded);
                }
                viewer.FileDecoder = OpenCvFileDecoder.Decode;
                bool loaded = viewer.LoadFile(tif);
                Harness.Settle(viewer);
                Harness.Check(loaded && viewer.ImageSize == new System.Drawing.Size(48, 32) &&
                              viewer.TryGetPixel(40, 7, out value) && value.V0 == 40007,
                    "16bit tif (한글 경로): 열기 + 16bit 값 보존");

                string pgm = Path.Combine(folder, "흑백.pgm");
                using (Mat gray = new Mat(10, 10, MatType.CV_8UC1, new Scalar(77)))
                {
                    byte[] encoded;
                    Cv2.ImEncode(".pgm", gray, out encoded);
                    File.WriteAllBytes(pgm, encoded);
                }
                Harness.Check(viewer.LoadFile(pgm), "pgm: OpenCV 디코더로 열림");
                Harness.Settle(viewer);

                // OpenCV가 못 읽는 파일은 false → 기본 디코더로 넘어가고, 그것도 실패하면 LoadFile이 false
                string broken = Path.Combine(folder, "깨진파일2.png");
                File.WriteAllBytes(broken, new byte[] { 9, 8, 7 });
                Harness.Check(!viewer.LoadFile(broken) && viewer.LastFileError != null, "깨진 파일: 예외 없이 false");
            }
        }
    }
}
