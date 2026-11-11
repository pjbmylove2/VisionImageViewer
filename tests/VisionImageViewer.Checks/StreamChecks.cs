using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace VisionImageViewer.Checks
{
    /// <summary>고속 갱신: 마지막 프레임 보장, 큐 순서, 여러 스레드 동시 입력, 메모리·핸들 누수, 성능 측정.</summary>
    internal static unsafe class StreamChecks
    {
        public static void LastFrame()
        {
            Harness.Section("마지막 프레임 표시 보장");
            ImageViewer viewer;
            using (Form form = Harness.CreateHost(320, 240, null, out viewer))
            {
                const int Width = 256;
                const int Height = 64;
                int failures = 0;
                long skippedTotal = 0;
                IntPtr data = Marshal.AllocHGlobal(Width * Height * 2);
                try
                {
                    for (int round = 0; round < 50; round++)
                    {
                        viewer.ResetStatistics();
                        int frames = 200 + round;
                        Thread producer = new Thread(() =>
                        {
                            for (int i = 0; i < frames; i++)
                            {
                                *(ushort*)data = (ushort)i;     // 프레임 번호를 첫 픽셀에 심습니다.
                                viewer.SetImage(data, Width, Height, Width * 2, ViewerPixelFormat.Mono16);
                            }
                        });
                        producer.Start();
                        while (!producer.Join(0))
                        {
                            Application.DoEvents();
                        }
                        bool settled = Harness.PumpUntil(() =>
                        {
                            ViewerStatistics s = viewer.Statistics;
                            return s.Displayed + s.Skipped == s.Received;
                        }, 2000);
                        PixelValue value;
                        bool ok = settled && viewer.TryGetPixel(0, 0, out value) && value.V0 == frames - 1;
                        if (!ok)
                        {
                            failures++;
                        }
                        skippedTotal += viewer.Statistics.Skipped;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(data);
                }
                Harness.Check(failures == 0, "버스트 50회: 항상 마지막 프레임이 화면에 남음 (실패 " + failures + ", 건너뛴 프레임 합계 " + skippedTotal + ")");
            }
        }

        public static void QueuePolicy()
        {
            Harness.Section("Queue 정책: 순서대로 모두 표시");
            ImageViewer viewer;
            ImageViewerOptions options = new ImageViewerOptions { DisplayPolicy = DisplayPolicy.Queue, QueueCapacity = 16 };
            using (Form form = Harness.CreateHost(200, 150, options, out viewer))
            {
                List<int> shown = new List<int>();
                viewer.FrameDisplayed += (s, e) =>
                {
                    PixelValue value;
                    if (viewer.TryGetPixel(0, 0, out value))
                    {
                        shown.Add(value.V0);
                    }
                };
                byte[] frame = new byte[32 * 32];
                for (int i = 0; i < 12; i++)
                {
                    frame[0] = (byte)(i + 1);
                    viewer.SetImage(frame, 32, 32, 32, ViewerPixelFormat.Mono8);    // 화면 갱신 없이 연달아 12장
                }
                Harness.PumpUntil(() => shown.Count >= 12, 2000);
                bool ordered = shown.Count == 12;
                for (int i = 0; ordered && i < 12; i++)
                {
                    ordered = shown[i] == i + 1;
                }
                ViewerStatistics stats = viewer.Statistics;
                Harness.Check(ordered && stats.Skipped == 0, "큐 16칸에 12장: 1~12 순서대로 모두 표시 (표시 " + shown.Count + ", 건너뜀 " + stats.Skipped + ")");

                shown.Clear();
                viewer.ResetStatistics();
                for (int i = 0; i < 40; i++)
                {
                    frame[0] = (byte)(i + 1);
                    viewer.SetImage(frame, 32, 32, 32, ViewerPixelFormat.Mono8);
                }
                Harness.PumpUntil(() => shown.Count >= 16, 2000);
                stats = viewer.Statistics;
                Harness.Check(shown.Count == 16 && shown[shown.Count - 1] == 40 && stats.Skipped == 24,
                    "큐 16칸에 40장: 오래된 24장을 버리고 최신 16장 표시, 마지막은 40번 (표시 " + shown.Count + ", 건너뜀 " + stats.Skipped + ")");
            }
        }

        public static void ThreadStress(int iterations)
        {
            Harness.Section("여러 스레드 동시 입력 + 실행 중 창 닫기 (" + iterations + "회)");
            int errors = 0;
            string firstError = null;
            byte[] source = new byte[64 * 48 * 3];
            Stopwatch watch = Stopwatch.StartNew();
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                ImageViewer viewer;
                Form form = Harness.CreateHost(120, 90, null, out viewer);
                bool stop = false;
                Thread[] producers = new Thread[4];
                for (int t = 0; t < producers.Length; t++)
                {
                    bool color = t % 2 == 0;
                    producers[t] = new Thread(() =>
                    {
                        try
                        {
                            while (!Volatile.Read(ref stop))
                            {
                                if (color)
                                {
                                    viewer.SetImage(source, 64, 48, 64 * 3, ViewerPixelFormat.Bgr24);
                                }
                                else
                                {
                                    viewer.SetImage(source, 48, 30, 48, ViewerPixelFormat.Mono8);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref errors);
                            Interlocked.CompareExchange(ref firstError, ex.ToString(), null);
                        }
                    });
                    producers[t].Start();
                }
                // 프레임이 쏟아지는 중에 몇 번 그리고 바로 닫습니다.
                for (int i = 0; i < 3; i++)
                {
                    Application.DoEvents();
                }
                form.Close();
                form.Dispose();
                Application.DoEvents();
                Volatile.Write(ref stop, true);
                foreach (Thread producer in producers)
                {
                    if (!producer.Join(5000))
                    {
                        Interlocked.Increment(ref errors);
                        Interlocked.CompareExchange(ref firstError, "스레드가 5초 안에 끝나지 않음(교착 의심)", null);
                    }
                }
            }
            Harness.Check(errors == 0, "예외·교착 0건 (" + watch.ElapsedMilliseconds + "ms, 오류 " + errors + ")");
            if (firstError != null)
            {
                Console.WriteLine(firstError);
            }
        }

        /// <summary>
        /// 프레임을 계속 넣으면서 관리 힙, 프로세스 메모리, GDI 개체 수가 늘어나는지 봅니다.
        /// 워밍업 뒤와 끝의 값을 비교합니다.
        /// </summary>
        public static void Memory(int frames)
        {
            Harness.Section("메모리·핸들 누수 (" + frames + " 프레임)");
            ImageViewer viewer;
            using (Form form = Harness.CreateHost(640, 480, null, out viewer))
            {
                const int Width = 1024;
                const int Height = 768;
                IntPtr data = Marshal.AllocHGlobal(Width * Height);
                OverlayItem[] overlays =
                {
                    OverlayItem.Rect(new RectangleF(100, 100, 200, 150), Color.Red),
                    OverlayItem.Label("NG", Color.Red)
                };
                try
                {
                    Process process = Process.GetCurrentProcess();
                    Action<int> run = count =>
                    {
                        Thread producer = new Thread(() =>
                        {
                            for (int i = 0; i < count; i++)
                            {
                                *(int*)data = i;
                                viewer.SetImage(data, Width, Height, Width, ViewerPixelFormat.Mono8, overlays);
                            }
                        });
                        producer.Start();
                        while (!producer.Join(0))
                        {
                            Application.DoEvents();
                        }
                        Harness.PumpUntil(() =>
                        {
                            ViewerStatistics s = viewer.Statistics;
                            return s.Displayed + s.Skipped == s.Received;
                        }, 2000);
                    };

                    run(Math.Max(2000, frames / 10));       // 워밍업: 버퍼 풀과 캐시가 자리 잡게 합니다.
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    process.Refresh();
                    long managedBefore = GC.GetTotalMemory(false);
                    long privateBefore = process.PrivateMemorySize64;
                    int gdiBefore = Harness.GetGuiResources(process.Handle, 0);
                    int userBefore = Harness.GetGuiResources(process.Handle, 1);
                    int gen2Before = GC.CollectionCount(2);

                    viewer.ResetStatistics();
                    Stopwatch watch = Stopwatch.StartNew();
                    run(frames);
                    watch.Stop();
                    int gen2During = GC.CollectionCount(2) - gen2Before;
                    ViewerStatistics stats = viewer.Statistics;

                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    process.Refresh();
                    long managedGrowth = GC.GetTotalMemory(false) - managedBefore;
                    long privateGrowth = process.PrivateMemorySize64 - privateBefore;
                    int gdiGrowth = Harness.GetGuiResources(process.Handle, 0) - gdiBefore;
                    int userGrowth = Harness.GetGuiResources(process.Handle, 1) - userBefore;

                    Console.WriteLine("  입력 " + (frames * 1000L / Math.Max(1, watch.ElapsedMilliseconds)) + " fps, 표시 " + stats.Displayed +
                                      ", 건너뜀 " + stats.Skipped + ", 복사 평균 " + stats.AverageCopyMs.ToString("0.000") +
                                      "ms, 그리기 평균 " + stats.AveragePaintMs.ToString("0.000") + "ms");
                    Harness.Check(managedGrowth < 1024 * 1024, "관리 힙 증가 < 1MB (" + (managedGrowth / 1024) + " KB)");
                    Harness.Check(privateGrowth < 16L * 1024 * 1024, "프로세스 메모리 증가 < 16MB (" + (privateGrowth / 1024) + " KB)");
                    Harness.Check(gdiGrowth <= 2 && userGrowth <= 2, "GDI/USER 개체 수 증가 없음 (GDI " + gdiGrowth + ", USER " + userGrowth + ")");
                    Harness.Check(gen2During <= 2, "실행 중 2세대 GC ≤ 2회 (" + gen2During + "회)");
                }
                finally
                {
                    Marshal.FreeHGlobal(data);
                }
            }
        }

        /// <summary>해상도·입력 속도별로 표시/건너뜀/복사·그리기 시간을 측정해 표로 출력합니다.</summary>
        public static void Benchmark()
        {
            Rectangle screen = Screen.PrimaryScreen.Bounds;
            Size viewport = new Size(Math.Min(1920, screen.Width), Math.Min(1080, screen.Height));
            Console.WriteLine("화면 " + screen.Width + "x" + screen.Height + ", 뷰어 " + viewport.Width + "x" + viewport.Height +
                              ", CPU 논리 코어 " + Environment.ProcessorCount);
            Console.WriteLine();
            Console.WriteLine("| 영상 | 보기 | 목표 fps | 실제 입력 fps | 표시 fps | 건너뜀 % | 복사 ms | 그리기 ms |");
            Console.WriteLine("|---|---|---|---|---|---|---|---|");

            int[][] sizes =
            {
                new[] { 2048, 2048 }, new[] { 4096, 3000 }, new[] { 8192, 4096 }, new[] { 16384, 4096 }
            };
            int[] rates = { 30, 60, 120, 500 };
            foreach (int[] size in sizes)
            {
                foreach (bool color in new[] { false, true })
                {
                    if (color && (long)size[0] * size[1] > 8192L * 4096)
                    {
                        continue;   // 16384x4096 컬러(201MB × 버퍼 여러 장)는 측정 PC 메모리 사정으로 제외
                    }
                    foreach (ViewMode mode in new[] { ViewMode.Fit, ViewMode.Actual })
                    {
                        foreach (int rate in rates)
                        {
                            BenchmarkCase(viewport, size[0], size[1], color, mode, rate);
                        }
                    }
                }
            }
        }

        private static void BenchmarkCase(Size viewport, int width, int height, bool color, ViewMode mode, int rate)
        {
            const int DurationMs = 2000;
            int bytesPerPixel = color ? 3 : 1;
            int stride = width * bytesPerPixel;
            long bytes = (long)stride * height;
            IntPtr[] sources = { Marshal.AllocHGlobal(new IntPtr(bytes)), Marshal.AllocHGlobal(new IntPtr(bytes)) };
            try
            {
                for (int s = 0; s < 2; s++)
                {
                    byte* p = (byte*)sources[s];
                    for (long i = 0; i < bytes; i++)
                    {
                        p[i] = (byte)((i >> (4 + s)) + i);
                    }
                }

                ImageViewer viewer;
                using (Form form = Harness.CreateHost(viewport.Width, viewport.Height, new ImageViewerOptions { ViewMode = mode }, out viewer))
                {
                    ViewerPixelFormat format = color ? ViewerPixelFormat.Bgr24 : ViewerPixelFormat.Mono8;
                    viewer.SetImage(sources[0], width, height, stride, format);
                    Harness.Settle(viewer);
                    viewer.ResetStatistics();

                    int sent = 0;
                    long interval = Stopwatch.Frequency / rate;
                    Stopwatch watch = new Stopwatch();
                    Thread producer = new Thread(() =>
                    {
                        long next = Stopwatch.GetTimestamp();
                        while (watch.ElapsedMilliseconds < DurationMs)
                        {
                            viewer.SetImage(sources[sent & 1], width, height, stride, format);
                            sent++;
                            next += interval;
                            long now = Stopwatch.GetTimestamp();
                            if (next < now)
                            {
                                next = now;     // 복사가 목표 주기보다 느리면 밀린 만큼 따라잡으려 하지 않습니다.
                            }
                            while (Stopwatch.GetTimestamp() < next)
                            {
                                Thread.SpinWait(50);
                            }
                        }
                    });

                    // 실제 프로그램과 같은 메시지 루프에서 측정합니다.
                    System.Windows.Forms.Timer finish = new System.Windows.Forms.Timer();
                    finish.Interval = 50;
                    finish.Tick += (s, e) =>
                    {
                        if (!producer.IsAlive)
                        {
                            finish.Stop();
                            form.Close();
                        }
                    };
                    form.Shown += (s, e) => { };
                    watch.Start();
                    producer.Start();
                    finish.Start();
                    ViewerStatistics stats = new ViewerStatistics();
                    form.FormClosing += (s, e) => stats = viewer.Statistics;
                    Application.Run(form);
                    finish.Dispose();
                    producer.Join();

                    double seconds = watch.ElapsedMilliseconds / 1000.0;
                    Console.WriteLine("| " + width + "x" + height + (color ? " BGR" : " Mono") + " | " + mode + " | " + rate +
                                      " | " + (stats.Received / seconds).ToString("0") +
                                      " | " + (stats.Displayed / seconds).ToString("0") +
                                      " | " + (stats.Received > 0 ? stats.Skipped * 100.0 / stats.Received : 0).ToString("0.0") +
                                      " | " + stats.AverageCopyMs.ToString("0.00") +
                                      " | " + stats.AveragePaintMs.ToString("0.00") + " |");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(sources[0]);
                Marshal.FreeHGlobal(sources[1]);
            }
        }
    }
}
