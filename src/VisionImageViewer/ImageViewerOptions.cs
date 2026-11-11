using System.Drawing;

namespace VisionImageViewer
{
    /// <summary>
    /// 뷰어를 코드에서 만들 때 넘기는 초기 설정입니다. 같은 값을 만든 뒤에 속성으로 바꿀 수도 있습니다.
    /// <code>var viewer = new ImageViewer(new ImageViewerOptions { BackColor = Color.DimGray });</code>
    /// </summary>
    public sealed class ImageViewerOptions
    {
        /// <summary>이미지가 없는 영역의 배경색. 기본 검정.</summary>
        public Color BackColor { get; set; } = Color.Black;
        /// <summary>처음 표시 방식. 기본 Fit.</summary>
        public ViewMode ViewMode { get; set; } = ViewMode.Fit;
        /// <summary>최소 배율. 기본 0.02(2%).</summary>
        public double MinZoom { get; set; } = 0.02;
        /// <summary>최대 배율. 기본 64(6400%).</summary>
        public double MaxZoom { get; set; } = 64.0;
        /// <summary>휠 한 칸 또는 +/- 한 번에 곱해지는 배율. 기본 1.2.</summary>
        public double ZoomStep { get; set; } = 1.2;
        /// <summary>축소 표시 화질. 기본 Fast.</summary>
        public ZoomOutQuality ZoomOutQuality { get; set; } = ZoomOutQuality.Fast;
        /// <summary>입력이 화면 갱신보다 빠를 때의 처리. 기본 LatestWins.</summary>
        public DisplayPolicy DisplayPolicy { get; set; } = DisplayPolicy.LatestWins;
        /// <summary>Queue 정책에서 쌓아 둘 최대 프레임 수. 기본 8.</summary>
        public int QueueCapacity { get; set; } = 8;
        /// <summary>초당 최대 화면 갱신 횟수. 0이면 제한 없음(가능한 한 모두 그림). 기본 0.</summary>
        public int MaxDisplayFps { get; set; }
        /// <summary>true면 휠과 +/- 키 확대를 잠급니다. 기본 false.</summary>
        public bool InspectionMode { get; set; }
        /// <summary>마우스가 올라오면 키 입력을 받도록 포커스를 가져옵니다. 기본 true.</summary>
        public bool FocusOnMouseEnter { get; set; } = true;
        /// <summary>확대 상태에서 왼쪽 버튼 드래그로 화면을 이동합니다. 기본 true.</summary>
        public bool PanWithMouseDrag { get; set; } = true;
        /// <summary>크게 확대했을 때 픽셀 경계선을 표시합니다. 기본 false.</summary>
        public bool ShowPixelGrid { get; set; }
        /// <summary>이미지 중심 십자선을 표시합니다. 기본 false.</summary>
        public bool ShowCrosshair { get; set; }
        /// <summary>메뉴 문구. null이면 한국어 기본값.</summary>
        public ViewerStrings Strings { get; set; }
    }
}
