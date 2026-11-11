using System;
using System.Drawing;

namespace VisionImageViewer
{
    /// <summary>입력 버퍼의 픽셀 배치입니다. 카메라·영상 라이브러리와 무관하게 메모리 배치만 지정합니다.</summary>
    public enum ViewerPixelFormat
    {
        /// <summary>8bit 흑백, 픽셀당 1바이트.</summary>
        Mono8,
        /// <summary>10bit 흑백, 픽셀당 2바이트(하위 비트 정렬).</summary>
        Mono10,
        /// <summary>12bit 흑백, 픽셀당 2바이트(하위 비트 정렬).</summary>
        Mono12,
        /// <summary>16bit 흑백, 픽셀당 2바이트.</summary>
        Mono16,
        /// <summary>B,G,R 순서 3바이트. OpenCV Mat(CV_8UC3)과 GDI의 기본 배치.</summary>
        Bgr24,
        /// <summary>R,G,B 순서 3바이트.</summary>
        Rgb24,
        /// <summary>B,G,R,A 순서 4바이트. 알파는 표시할 때 무시합니다.</summary>
        Bgra32
    }

    /// <summary>화면 표시 방식입니다.</summary>
    public enum ViewMode
    {
        /// <summary>원본 비율을 유지하며 컨트롤 안에 전체가 보이도록 맞춥니다.</summary>
        Fit,
        /// <summary>비율을 무시하고 컨트롤 전체에 꽉 채웁니다(표시 전용).</summary>
        Fill,
        /// <summary>원본 크기 1:1.</summary>
        Actual,
        /// <summary>사용자가 정한 배율.</summary>
        Custom
    }

    /// <summary>축소 표시 화질입니다. 확대할 때는 항상 픽셀 경계를 보존합니다.</summary>
    public enum ZoomOutQuality
    {
        /// <summary>픽셀을 건너뛰는 빠른 방식. 1픽셀 선·점은 축소 화면에서 사라질 수 있습니다.</summary>
        Fast,
        /// <summary>주변 픽셀을 평균하는 방식. 느리지만 가는 결함이 덜 사라집니다. 16bit 영상에는 적용되지 않습니다.</summary>
        Smooth
    }

    /// <summary>입력이 화면 갱신보다 빠를 때의 처리 방식입니다.</summary>
    public enum DisplayPolicy
    {
        /// <summary>항상 가장 최근 프레임만 그립니다. 마지막 프레임은 반드시 표시됩니다.</summary>
        LatestWins,
        /// <summary>크기 제한 큐에 쌓아 순서대로 모두 그립니다. 가득 차면 가장 오래된 프레임을 버립니다.</summary>
        Queue
    }

    /// <summary>한 픽셀의 원본 값입니다.</summary>
    public struct PixelValue
    {
        /// <summary>채널 수. 흑백 1, 컬러 3, 알파 포함 4.</summary>
        public int Channels;
        /// <summary>흑백 값 또는 R.</summary>
        public int V0;
        /// <summary>G.</summary>
        public int V1;
        /// <summary>B.</summary>
        public int V2;
        /// <summary>A.</summary>
        public int V3;

        /// <summary>"128" 또는 "R,G,B" 형태의 문자열.</summary>
        public override string ToString()
        {
            if (Channels <= 1)
            {
                return V0.ToString();
            }
            if (Channels == 3)
            {
                return V0 + "," + V1 + "," + V2;
            }
            return V0 + "," + V1 + "," + V2 + "," + V3;
        }
    }

    /// <summary>마우스가 가리키는 이미지 픽셀 정보입니다.</summary>
    public sealed class PixelHoverEventArgs : EventArgs
    {
        /// <summary>이미지 X 좌표(픽셀).</summary>
        public int X { get; }
        /// <summary>이미지 Y 좌표(픽셀).</summary>
        public int Y { get; }
        /// <summary>마우스가 이미지 영역 안에 있는지 여부.</summary>
        public bool IsInsideImage { get; }
        /// <summary>원본 픽셀 값. 이미지 밖이면 기본값.</summary>
        public PixelValue Value { get; }
        /// <summary>컨트롤 기준 마우스 위치.</summary>
        public Point Location { get; }

        internal PixelHoverEventArgs(int x, int y, bool inside, PixelValue value, Point location)
        {
            X = x;
            Y = y;
            IsInsideImage = inside;
            Value = value;
            Location = location;
        }
    }

    /// <summary>뷰어 동작 통계의 한 시점 값입니다.</summary>
    public struct ViewerStatistics
    {
        /// <summary>SetImage로 받은 프레임 수.</summary>
        public long Received;
        /// <summary>실제로 화면에 그린 프레임 수.</summary>
        public long Displayed;
        /// <summary>더 새로운 프레임에 밀려 그리지 않은 프레임 수.</summary>
        public long Skipped;
        /// <summary>마지막 SetImage의 복사 시간(ms).</summary>
        public double LastCopyMs;
        /// <summary>SetImage 복사 시간 평균(ms).</summary>
        public double AverageCopyMs;
        /// <summary>마지막 그리기 시간(ms).</summary>
        public double LastPaintMs;
        /// <summary>그리기 시간 평균(ms).</summary>
        public double AveragePaintMs;
    }

    /// <summary>
    /// 파일을 해석해 뷰어에 넣는 외부 디코더입니다. 해석에 성공하면 <paramref name="target"/>의 SetImage를
    /// 직접 호출하고 true를 반환합니다. false를 반환하면 뷰어가 기본(GDI+) 디코더로 다시 시도합니다.
    /// </summary>
    public delegate bool ImageFileDecoder(string path, byte[] fileBytes, ImageViewer target);
}
