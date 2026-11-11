using System;
using System.Collections.Generic;
using OpenCvSharp;

namespace VisionImageViewer
{
    /// <summary>
    /// OpenCvSharp Mat을 뷰어에 바로 넣는 확장 메서드입니다.
    /// <code>viewer.SetImage(mat);</code>
    /// 뷰어가 픽셀을 복사하므로 호출이 끝나면 Mat을 Dispose하거나 다음 프레임에 재사용해도 됩니다.
    /// </summary>
    public static class MatViewerExtensions
    {
        /// <summary>
        /// Mat을 표시합니다. CV_8UC1(흑백), CV_8UC3(BGR), CV_8UC4(BGRA), CV_16UC1(16bit 흑백)을 지원합니다.
        /// ROI로 잘라낸 Mat도 그대로 넣을 수 있습니다. 어느 스레드에서든 호출할 수 있습니다.
        /// </summary>
        /// <returns>표시 요청을 넣었으면 true. Mat이 null이거나 해제됐거나 비어 있으면 아무 일도 하지 않고 false.</returns>
        /// <exception cref="NotSupportedException">지원하지 않는 Mat 형식(예: CV_32F). 먼저 8bit나 16bit로 변환해서 넘기세요.</exception>
        public static bool SetImage(this ImageViewer viewer, Mat image, IReadOnlyList<OverlayItem> overlays = null)
        {
            if (viewer == null)
            {
                throw new ArgumentNullException(nameof(viewer));
            }
            if (image == null || image.IsDisposed || image.Empty())
            {
                return false;
            }
            return SetImage(viewer, image, GetFormat(image.Type()), overlays);
        }

        /// <summary>
        /// 픽셀 배치를 직접 지정해 Mat을 표시합니다. CV_16UC1에 담긴 10/12bit 영상(Mono10/Mono12)이나
        /// RGB 순서의 CV_8UC3(Rgb24)처럼 Mat 형식만으로는 알 수 없는 경우에 씁니다.
        /// </summary>
        public static bool SetImage(this ImageViewer viewer, Mat image, ViewerPixelFormat format,
            IReadOnlyList<OverlayItem> overlays = null)
        {
            if (viewer == null)
            {
                throw new ArgumentNullException(nameof(viewer));
            }
            if (image == null || image.IsDisposed || image.Empty())
            {
                return false;
            }
            viewer.SetImage(image.Data, image.Cols, image.Rows, (int)image.Step(), format, overlays);
            return true;
        }

        private static ViewerPixelFormat GetFormat(MatType type)
        {
            if (type == MatType.CV_8UC1)
            {
                return ViewerPixelFormat.Mono8;
            }
            if (type == MatType.CV_8UC3)
            {
                return ViewerPixelFormat.Bgr24;
            }
            if (type == MatType.CV_8UC4)
            {
                return ViewerPixelFormat.Bgra32;
            }
            if (type == MatType.CV_16UC1)
            {
                return ViewerPixelFormat.Mono16;
            }
            throw new NotSupportedException("지원하지 않는 Mat 형식입니다: " + type +
                " (CV_8UC1, CV_8UC3, CV_8UC4, CV_16UC1만 가능)");
        }
    }
}
