using System;
using System.Windows.Forms;

namespace VisionImageViewer.Demo
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            OpenCvFileDecoder.Register();   // 16bit tif, webp 등도 열 수 있게 OpenCV 디코더를 등록합니다.
            MainForm form = new MainForm();
            if (args.Length == 2 && args[0] == "--snapshot")
            {
                // 문서용 화면 캡처: 스트림을 잠깐 돌린 뒤 창을 PNG로 저장하고 끝냅니다.
                form.CaptureAfterStart(args[1]);
            }
            Application.Run(form);
        }
    }
}
