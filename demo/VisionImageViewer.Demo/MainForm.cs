using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using OpenCvSharp;
using CvSize = OpenCvSharp.Size;
using Point = System.Drawing.Point;
using Size = System.Drawing.Size;

namespace VisionImageViewer.Demo
{
    /// <summary>
    /// 대표 사용 흐름을 보여 주는 데모입니다.
    /// 카메라 스레드(모의) → OpenCvSharp 전처리 → 라이브 뷰어, 검사(모의) → 결과 뷰어(불량 박스 + OK/NG).
    /// </summary>
    [DesignerCategory("Code")]
    internal sealed class MainForm : Form
    {
        // 배경색은 만들 때 옵션으로 지정합니다.
        private readonly ImageViewer _live = new ImageViewer(new ImageViewerOptions { BackColor = Color.FromArgb(28, 28, 28) });
        private readonly ImageViewer _result = new ImageViewer(new ImageViewerOptions { BackColor = Color.Black });

        private readonly ComboBox _resolution = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        private readonly ComboBox _format = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        private readonly NumericUpDown _fps = new NumericUpDown { Minimum = 1, Maximum = 1000, Value = 60, Width = 220 };
        private readonly CheckBox _preprocess = new CheckBox { Text = "전처리 (가우시안 3x3)", AutoSize = true };
        private readonly Button _startStop = new Button { Text = "스트림 시작", Width = 220, Height = 32 };
        private readonly CheckBox _inspection = new CheckBox { Text = "검사 모드 (휠·+/- 잠금)", AutoSize = true };
        private readonly ComboBox _policy = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        private readonly ComboBox _quality = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        private readonly Label _stats = new Label { AutoSize = true, Font = new Font("Consolas", 9F) };
        private readonly Label _pixel = new Label { AutoSize = true, Font = new Font("Consolas", 9F), Text = "픽셀: -" };
        private readonly System.Windows.Forms.Timer _statsTimer = new System.Windows.Forms.Timer { Interval = 500 };

        private Thread _camera;
        private volatile bool _running;

        [DllImport("user32.dll")]
        private static extern int GetGuiResources(IntPtr process, int flags);

        public MainForm()
        {
            Text = "VisionImageViewer 데모";
            ClientSize = new Size(1500, 800);
            StartPosition = FormStartPosition.CenterScreen;

            TableLayoutPanel viewers = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            viewers.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            viewers.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            viewers.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            viewers.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            viewers.Controls.Add(new Label { Text = "라이브 (전처리 후)", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            viewers.Controls.Add(new Label { Text = "검사 결과", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 1, 0);
            _live.Dock = DockStyle.Fill;
            _result.Dock = DockStyle.Fill;
            _live.AllowDrop = true;         // 파일을 끌어다 놓으면 열립니다.
            viewers.Controls.Add(_live, 0, 1);
            viewers.Controls.Add(_result, 1, 1);

            FlowLayoutPanel side = new FlowLayoutPanel
            {
                Dock = DockStyle.Right, Width = 250, FlowDirection = FlowDirection.TopDown,
                WrapContents = false, Padding = new Padding(10), AutoScroll = true
            };
            _resolution.Items.AddRange(new object[] { "2048 x 2048", "4096 x 3000", "8192 x 4096", "16384 x 4096" });
            _resolution.SelectedIndex = 1;
            _format.Items.AddRange(new object[] { "흑백 8bit", "컬러 BGR" });
            _format.SelectedIndex = 0;
            _policy.Items.AddRange(new object[] { "최신 프레임 우선", "큐 (순서대로 모두)" });
            _policy.SelectedIndex = 0;
            _quality.Items.AddRange(new object[] { "축소 화질: 빠름", "축소 화질: 부드럽게" });
            _quality.SelectedIndex = 0;
            side.Controls.AddRange(new Control[]
            {
                new Label { Text = "해상도", AutoSize = true }, _resolution,
                new Label { Text = "형식", AutoSize = true }, _format,
                new Label { Text = "입력 속도 (fps)", AutoSize = true }, _fps,
                _preprocess, _startStop,
                new Label { Text = " ", AutoSize = true },
                _inspection, _policy, _quality,
                new Label { Text = " ", AutoSize = true },
                _pixel, _stats
            });

            Controls.Add(viewers);
            Controls.Add(side);

            _startStop.Click += (s, e) => ToggleStream();
            _inspection.CheckedChanged += (s, e) =>
            {
                _live.InspectionMode = _inspection.Checked;
                _result.InspectionMode = _inspection.Checked;
            };
            _policy.SelectedIndexChanged += (s, e) =>
            {
                DisplayPolicy policy = _policy.SelectedIndex == 1 ? DisplayPolicy.Queue : DisplayPolicy.LatestWins;
                _live.DisplayPolicy = policy;
                _result.DisplayPolicy = policy;
            };
            _quality.SelectedIndexChanged += (s, e) =>
            {
                ZoomOutQuality quality = _quality.SelectedIndex == 1 ? ZoomOutQuality.Smooth : ZoomOutQuality.Fast;
                _live.ZoomOutQuality = quality;
                _result.ZoomOutQuality = quality;
            };
            EventHandler<PixelHoverEventArgs> showPixel = (s, e) =>
            {
                _pixel.Text = e.IsInsideImage ? "픽셀: (" + e.X + ", " + e.Y + ") = " + e.Value : "픽셀: -";
            };
            _live.PixelHover += showPixel;
            _result.PixelHover += showPixel;
            _statsTimer.Tick += (s, e) => UpdateStats();
            _statsTimer.Start();
        }

        /// <summary>창이 뜨면 스트림을 시작하고 2초 뒤 창 전체를 PNG로 저장한 다음 닫습니다.</summary>
        public void CaptureAfterStart(string path)
        {
            Shown += (s, e) =>
            {
                ToggleStream();
                System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 2000 };
                timer.Tick += (s2, e2) =>
                {
                    timer.Dispose();
                    UpdateStats();
                    using (Bitmap bitmap = new Bitmap(Width, Height))
                    {
                        DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
                        bitmap.Save(path);
                    }
                    Close();
                };
                timer.Start();
            };
        }

        private void ToggleStream()
        {
            if (_running)
            {
                StopStream();
                return;
            }
            string[] parts = ((string)_resolution.SelectedItem).Split('x');
            int width = int.Parse(parts[0].Trim());
            int height = int.Parse(parts[1].Trim());
            bool color = _format.SelectedIndex == 1;
            int fps = (int)_fps.Value;
            bool preprocess = _preprocess.Checked;

            _live.ResetStatistics();
            _result.ResetStatistics();
            _running = true;
            _camera = new Thread(() => CameraLoop(width, height, color, fps, preprocess));
            _camera.IsBackground = true;
            _camera.Name = "모의 카메라";
            _camera.Start();
            _startStop.Text = "스트림 정지";
        }

        private void StopStream()
        {
            _running = false;
            if (_camera != null)
            {
                _camera.Join(3000);
                _camera = null;
            }
            _startStop.Text = "스트림 시작";
        }

        /// <summary>
        /// 카메라 취득 스레드 역할입니다. 실제 프로그램에서는 이 자리에 카메라 SDK 콜백이 옵니다.
        /// 뷰어의 SetImage는 이 스레드에서 바로 호출합니다(Invoke 불필요).
        /// </summary>
        private void CameraLoop(int width, int height, bool color, int fps, bool preprocess)
        {
            Mat[] sources = CreateSources(width, height, color);
            Mat processed = new Mat();
            try
            {
                long interval = Stopwatch.Frequency / fps;
                long next = Stopwatch.GetTimestamp();
                int frame = 0;
                while (_running)
                {
                    Mat raw = sources[frame & 3];                   // 카메라에서 받은 원본
                    Mat image = raw;
                    if (preprocess)
                    {
                        Cv2.GaussianBlur(raw, processed, new CvSize(3, 3), 0);   // OpenCvSharp 전처리
                        image = processed;
                    }

                    _live.SetImage(image);                          // 라이브 뷰어로 전달

                    List<OverlayItem> overlays = Inspect(frame, width, height);  // 검사(모의)
                    _result.SetImage(image, overlays);              // 결과 뷰어: 영상 + 불량 표시를 한 묶음으로

                    frame++;
                    next += interval;
                    long now = Stopwatch.GetTimestamp();
                    if (next < now)
                    {
                        next = now;
                    }
                    while (_running && Stopwatch.GetTimestamp() < next)
                    {
                        Thread.SpinWait(50);
                    }
                }
            }
            finally
            {
                processed.Dispose();
                foreach (Mat source in sources)
                {
                    source.Dispose();
                }
            }
        }

        /// <summary>움직이는 줄무늬 영상 4장을 미리 만듭니다(카메라 버퍼 역할).</summary>
        private static Mat[] CreateSources(int width, int height, bool color)
        {
            int channels = color ? 3 : 1;
            byte[] pattern = new byte[(width + 1024) * channels];
            for (int x = 0; x < width + 1024; x++)
            {
                byte value = (byte)(x & 255);
                for (int c = 0; c < channels; c++)
                {
                    pattern[x * channels + c] = (byte)(c == 0 ? value : c == 1 ? 255 - value : value / 2);
                }
            }
            Mat[] sources = new Mat[4];
            for (int i = 0; i < sources.Length; i++)
            {
                Mat mat = new Mat(height, width, color ? MatType.CV_8UC3 : MatType.CV_8UC1);
                for (int y = 0; y < height; y++)
                {
                    int offset = ((y / 4) + i * 64) & 255;
                    Marshal.Copy(pattern, offset * channels, mat.Ptr(y), width * channels);
                }
                sources[i] = mat;
            }
            return sources;
        }

        /// <summary>모의 검사: 프레임 번호로 정해지는 위치에 불량 0~3개를 만들고 표시할 도형 목록을 돌려줍니다.</summary>
        private static List<OverlayItem> Inspect(int frame, int width, int height)
        {
            Random random = new Random(frame / 20);     // 20프레임 동안 같은 결과가 유지되어 눈으로 볼 수 있습니다.
            int defects = random.Next(0, 4);
            List<OverlayItem> overlays = new List<OverlayItem>();
            for (int i = 0; i < defects; i++)
            {
                float w = width * (0.02F + (float)random.NextDouble() * 0.05F);
                float h = height * (0.02F + (float)random.NextDouble() * 0.05F);
                float x = (float)random.NextDouble() * (width - w);
                float y = (float)random.NextDouble() * (height - h);
                overlays.Add(OverlayItem.Rect(new RectangleF(x, y, w, h), Color.Red, 2F, 40));
                overlays.Add(OverlayItem.Text(new PointF(x, y + h), "불량 " + (i + 1), Color.Yellow));
            }
            overlays.Add(defects == 0
                ? OverlayItem.Label("OK", Color.Lime)
                : OverlayItem.Label("NG (" + defects + ")", Color.Red));
            overlays.Add(OverlayItem.Label("#" + frame, Color.White, ContentAlignment.BottomRight, 16F));
            return overlays;
        }

        private void UpdateStats()
        {
            Process process = Process.GetCurrentProcess();
            process.Refresh();
            _stats.Text =
                Describe("라이브", _live) + Environment.NewLine + Environment.NewLine +
                Describe("결과", _result) + Environment.NewLine + Environment.NewLine +
                "메모리 " + (process.PrivateMemorySize64 / (1024 * 1024)) + " MB" + Environment.NewLine +
                "GDI 개체 " + GetGuiResources(process.Handle, 0);
        }

        private static string Describe(string name, ImageViewer viewer)
        {
            ViewerStatistics s = viewer.Statistics;
            return "[" + name + "] " + viewer.ImageSize.Width + "x" + viewer.ImageSize.Height + Environment.NewLine +
                   "배율   " + (viewer.Zoom * 100).ToString("0.#") + "%" + Environment.NewLine +
                   "수신   " + s.Received + Environment.NewLine +
                   "표시   " + s.Displayed + Environment.NewLine +
                   "건너뜀 " + s.Skipped + Environment.NewLine +
                   "복사   " + s.AverageCopyMs.ToString("0.00") + " ms" + Environment.NewLine +
                   "그리기 " + s.AveragePaintMs.ToString("0.00") + " ms";
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            StopStream();
            _statsTimer.Dispose();
            base.OnFormClosing(e);
        }
    }
}
