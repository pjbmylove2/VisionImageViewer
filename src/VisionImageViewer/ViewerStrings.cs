namespace VisionImageViewer
{
    /// <summary>
    /// 뷰어가 화면에 보여 주는 문구입니다. 기본값은 한국어이며, 다국어 프로그램에서는
    /// 값을 바꾼 객체를 <see cref="ImageViewer.Strings"/>에 넣으면 메뉴가 그 문구로 바뀝니다.
    /// </summary>
    public sealed class ViewerStrings
    {
        /// <summary>메뉴: 화면 맞춤.</summary>
        public string Fit { get; set; } = "화면 맞춤 (비율 유지)";
        /// <summary>메뉴: 꽉 채움.</summary>
        public string Fill { get; set; } = "꽉 채움 (비율 무시)";
        /// <summary>메뉴: 원본 크기.</summary>
        public string Actual { get; set; } = "원본 크기 (1:1)";
        /// <summary>메뉴: 확대.</summary>
        public string ZoomIn { get; set; } = "확대 (+)";
        /// <summary>메뉴: 축소.</summary>
        public string ZoomOut { get; set; } = "축소 (-)";
        /// <summary>메뉴: 배율 선택.</summary>
        public string ZoomPreset { get; set; } = "배율";
        /// <summary>메뉴: 이미지 열기.</summary>
        public string Open { get; set; } = "이미지 열기...";
        /// <summary>메뉴: 이미지 저장.</summary>
        public string Save { get; set; } = "이미지 저장...";
        /// <summary>메뉴: 오버레이 포함 저장.</summary>
        public string SaveWithOverlays { get; set; } = "표시 내용 포함 저장...";
        /// <summary>메뉴: 픽셀 격자.</summary>
        public string PixelGrid { get; set; } = "픽셀 격자";
        /// <summary>메뉴: 십자선.</summary>
        public string Crosshair { get; set; } = "중심 십자선";
        /// <summary>이미지가 없을 때 가운데에 보이는 문구.</summary>
        public string Empty { get; set; } = "이미지 없음";
        /// <summary>파일 열기 실패 메시지 앞부분.</summary>
        public string OpenFailed { get; set; } = "이미지를 열 수 없습니다.";
        /// <summary>파일 저장 실패 메시지 앞부분.</summary>
        public string SaveFailed { get; set; } = "이미지를 저장할 수 없습니다.";
        /// <summary>열기 대화상자 필터의 "이미지 파일" 이름.</summary>
        public string ImageFiles { get; set; } = "이미지 파일";
        /// <summary>열기 대화상자 필터의 "모든 파일" 이름.</summary>
        public string AllFiles { get; set; } = "모든 파일";
    }
}
