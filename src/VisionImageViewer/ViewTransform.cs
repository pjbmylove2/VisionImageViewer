using System;
using System.Drawing;

namespace VisionImageViewer
{
    /// <summary>
    /// 이미지 좌표와 화면(컨트롤 클라이언트) 좌표 사이의 변환입니다. UI와 무관한 순수 계산이라
    /// 단독으로 검증할 수 있습니다. 이미지 좌표 (0,0)은 첫 픽셀의 왼쪽 위 모서리입니다.
    /// </summary>
    internal readonly struct ViewTransform
    {
        /// <summary>확대된 이미지의 가로/세로 한계. 스크롤 범위(int)를 넘지 않게 합니다.</summary>
        public const int MaxDisplaySize = 100000000;

        public readonly int ImageWidth;
        public readonly int ImageHeight;
        /// <summary>이미지 (0,0)이 그려지는 화면 X. 스크롤하면 음수가 됩니다.</summary>
        public readonly int OriginX;
        public readonly int OriginY;
        /// <summary>화면에 그려지는 이미지 전체 크기(보이지 않는 부분 포함).</summary>
        public readonly int DisplayWidth;
        public readonly int DisplayHeight;
        /// <summary>이미지 1픽셀이 화면에서 차지하는 픽셀 수.</summary>
        public readonly double ScaleX;
        public readonly double ScaleY;

        private ViewTransform(int imageWidth, int imageHeight, int originX, int originY,
            int displayWidth, int displayHeight)
        {
            ImageWidth = imageWidth;
            ImageHeight = imageHeight;
            OriginX = originX;
            OriginY = originY;
            DisplayWidth = displayWidth;
            DisplayHeight = displayHeight;
            ScaleX = displayWidth / (double)imageWidth;
            ScaleY = displayHeight / (double)imageHeight;
        }

        public static ViewTransform Create(int imageWidth, int imageHeight, int clientWidth, int clientHeight,
            ViewMode mode, double zoom, int scrollX, int scrollY)
        {
            int cw = Math.Max(1, clientWidth);
            int ch = Math.Max(1, clientHeight);
            int dw;
            int dh;
            switch (mode)
            {
                case ViewMode.Fill:
                    dw = cw;
                    dh = ch;
                    break;
                case ViewMode.Fit:
                    double fit = GetFitZoom(imageWidth, imageHeight, cw, ch);
                    dw = Math.Min(cw, ScaleSize(imageWidth, fit));
                    dh = Math.Min(ch, ScaleSize(imageHeight, fit));
                    break;
                case ViewMode.Actual:
                    dw = imageWidth;
                    dh = imageHeight;
                    break;
                default:
                    dw = ScaleSize(imageWidth, zoom);
                    dh = ScaleSize(imageHeight, zoom);
                    break;
            }

            // 화면보다 작으면 가운데 정렬, 크면 스크롤 위치만큼 왼쪽/위로 밀어 그립니다.
            int ox = dw <= cw ? (cw - dw) / 2 : -Clamp(scrollX, 0, dw - cw);
            int oy = dh <= ch ? (ch - dh) / 2 : -Clamp(scrollY, 0, dh - ch);
            return new ViewTransform(imageWidth, imageHeight, ox, oy, dw, dh);
        }

        /// <summary>비율을 유지하며 화면 안에 전체가 들어가는 배율.</summary>
        public static double GetFitZoom(int imageWidth, int imageHeight, int clientWidth, int clientHeight)
        {
            double zx = Math.Max(1, clientWidth) / (double)imageWidth;
            double zy = Math.Max(1, clientHeight) / (double)imageHeight;
            return Math.Min(zx, zy);
        }

        public static int ScaleSize(int size, double zoom)
        {
            double value = Math.Round(size * zoom);
            if (value < 1)
            {
                return 1;
            }
            if (value > MaxDisplaySize)
            {
                return MaxDisplaySize;
            }
            return (int)value;
        }

        public void DisplayToImage(double displayX, double displayY, out double imageX, out double imageY)
        {
            imageX = (displayX - OriginX) / ScaleX;
            imageY = (displayY - OriginY) / ScaleY;
        }

        public PointF ImageToDisplay(double imageX, double imageY)
        {
            return new PointF((float)(OriginX + imageX * ScaleX), (float)(OriginY + imageY * ScaleY));
        }

        /// <summary>
        /// 이 변환(새 배율)에서 이미지 점 (imageX, imageY)가 화면의 anchor 위치에 오도록 하는 스크롤 위치.
        /// 마우스 위치를 기준으로 확대/축소할 때 씁니다.
        /// </summary>
        public Point ScrollForAnchor(double imageX, double imageY, Point anchor, int clientWidth, int clientHeight)
        {
            int maxX = Math.Max(0, DisplayWidth - Math.Max(1, clientWidth));
            int maxY = Math.Max(0, DisplayHeight - Math.Max(1, clientHeight));
            int x = Clamp((int)Math.Round(imageX * ScaleX - anchor.X), 0, maxX);
            int y = Clamp((int)Math.Round(imageY * ScaleY - anchor.Y), 0, maxY);
            return new Point(x, y);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }
            return value > max ? max : value;
        }
    }
}
