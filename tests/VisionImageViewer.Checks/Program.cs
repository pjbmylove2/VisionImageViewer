using System;
using System.IO;
using System.Windows.Forms;

namespace VisionImageViewer.Checks
{
    /// <summary>
    /// 뷰어 검증 프로그램입니다. 하나라도 실패하면 종료 코드 1을 반환합니다.
    /// <para>
    /// 인자 없음: 기본 검증 전체<br/>
    /// soak [프레임 수]: 장시간 메모리·핸들 누수 확인 (기본 1,000,000)<br/>
    /// bench: 해상도·입력 속도별 성능 측정 표 출력
    /// </para>
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            string command = args.Length > 0 ? args[0].ToLowerInvariant() : "all";
            string artifacts = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "artifacts");
            Directory.CreateDirectory(artifacts);

            try
            {
                switch (command)
                {
                    case "bench":
                        StreamChecks.Benchmark();
                        return 0;
                    case "stress":
                        StreamChecks.ThreadStress(args.Length > 1 ? int.Parse(args[1]) : 1000);
                        break;
                    case "soak":
                        int frames = args.Length > 1 ? int.Parse(args[1]) : 1000000;
                        StreamChecks.Memory(frames);
                        break;
                    default:
                        ViewChecks.Transform();
                        ViewChecks.PixelAccuracy();
                        ViewChecks.Formats();
                        ViewChecks.Input();
                        FileChecks.Overlays(artifacts);
                        FileChecks.Files(artifacts);
                        AdapterChecks.Run(artifacts);
                        StreamChecks.LastFrame();
                        StreamChecks.QueuePolicy();
                        StreamChecks.ThreadStress(1000);
                        StreamChecks.Memory(20000);
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("[중단] 검증 도중 예외 발생:");
                Console.WriteLine(ex);
                return 2;
            }

            Harness.Summary();
            return Harness.Failed == 0 ? 0 : 1;
        }
    }
}
