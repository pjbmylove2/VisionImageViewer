using System;
using System.Collections.Generic;
using System.Drawing;

namespace VisionImageViewer
{
    /// <summary>
    /// 영상 위에 겹쳐 그리는 도형입니다. 좌표는 이미지 픽셀 기준이며 (0,0)은 첫 픽셀의 왼쪽 위 모서리입니다.
    /// 한 번 만들면 값이 바뀌지 않으므로 여러 스레드에서 안전하게 넘길 수 있습니다.
    /// 프레임과 함께 SetImage에 넘기면 그 프레임이 그려질 때 같이 그려집니다.
    /// </summary>
    public abstract class OverlayItem
    {
        /// <summary>선/글자 색.</summary>
        public Color Color { get; }
        /// <summary>선 두께(화면 픽셀). 확대해도 두께는 변하지 않습니다.</summary>
        public float LineWidth { get; }

        internal OverlayItem(Color color, float lineWidth)
        {
            Color = color;
            LineWidth = lineWidth <= 0F ? 1F : lineWidth;
        }

        internal abstract void Draw(Graphics g, in ViewTransform t, Rectangle viewBounds, OverlayResources resources);

        /// <summary>사각형(불량 박스 등). fillAlpha가 0보다 크면 그 투명도로 안쪽을 채웁니다(0~255).</summary>
        public static OverlayItem Rect(RectangleF imageRect, Color color, float lineWidth = 2F, int fillAlpha = 0)
        {
            return new RectOverlay(imageRect, color, lineWidth, fillAlpha, false);
        }

        /// <summary>사각형에 내접하는 타원.</summary>
        public static OverlayItem Ellipse(RectangleF imageRect, Color color, float lineWidth = 2F, int fillAlpha = 0)
        {
            return new RectOverlay(imageRect, color, lineWidth, fillAlpha, true);
        }

        /// <summary>두 점을 잇는 선.</summary>
        public static OverlayItem Line(PointF from, PointF to, Color color, float lineWidth = 2F)
        {
            return new PolygonOverlay(new[] { from, to }, color, lineWidth, false);
        }

        /// <summary>십자 표시. size는 이미지 픽셀 기준 전체 길이입니다.</summary>
        public static OverlayItem Cross(PointF center, float size, Color color, float lineWidth = 2F)
        {
            return new CrossOverlay(center, size, color, lineWidth);
        }

        /// <summary>여러 점을 잇는 선. closed가 true면 마지막 점과 첫 점을 이어 닫습니다.</summary>
        public static OverlayItem Polygon(IEnumerable<PointF> points, Color color, float lineWidth = 2F, bool closed = true)
        {
            if (points == null)
            {
                throw new ArgumentNullException(nameof(points));
            }
            return new PolygonOverlay(new List<PointF>(points).ToArray(), color, lineWidth, closed);
        }

        /// <summary>이미지의 한 점에 붙는 글자. 글자 크기는 화면 픽셀 기준이라 확대해도 변하지 않습니다.</summary>
        public static OverlayItem Text(PointF imagePoint, string text, Color color, float fontSize = 14F)
        {
            return new TextOverlay(imagePoint, text, color, fontSize);
        }

        /// <summary>화면 모서리에 고정되는 글자(OK/NG 등). 확대·스크롤해도 같은 자리에 보입니다.</summary>
        public static OverlayItem Label(string text, Color color,
            ContentAlignment anchor = ContentAlignment.TopLeft, float fontSize = 28F)
        {
            return new LabelOverlay(text, color, anchor, fontSize);
        }

        private sealed class RectOverlay : OverlayItem
        {
            private readonly RectangleF _rect;
            private readonly int _fillAlpha;
            private readonly bool _ellipse;

            public RectOverlay(RectangleF rect, Color color, float lineWidth, int fillAlpha, bool ellipse)
                : base(color, lineWidth)
            {
                _rect = rect;
                _fillAlpha = Math.Max(0, Math.Min(255, fillAlpha));
                _ellipse = ellipse;
            }

            internal override void Draw(Graphics g, in ViewTransform t, Rectangle viewBounds, OverlayResources resources)
            {
                PointF a = t.ImageToDisplay(_rect.Left, _rect.Top);
                PointF b = t.ImageToDisplay(_rect.Right, _rect.Bottom);
                float w = b.X - a.X;
                float h = b.Y - a.Y;
                if (_fillAlpha > 0)
                {
                    SolidBrush fill = resources.GetBrush(Color.FromArgb(_fillAlpha, Color));
                    if (_ellipse)
                    {
                        g.FillEllipse(fill, a.X, a.Y, w, h);
                    }
                    else
                    {
                        g.FillRectangle(fill, a.X, a.Y, w, h);
                    }
                }
                Pen pen = resources.GetPen(Color, LineWidth);
                if (_ellipse)
                {
                    g.DrawEllipse(pen, a.X, a.Y, w, h);
                }
                else
                {
                    g.DrawRectangle(pen, a.X, a.Y, w, h);
                }
            }
        }

        private sealed class PolygonOverlay : OverlayItem
        {
            private readonly PointF[] _points;
            private readonly bool _closed;

            public PolygonOverlay(PointF[] points, Color color, float lineWidth, bool closed)
                : base(color, lineWidth)
            {
                _points = points;
                _closed = closed;
            }

            internal override void Draw(Graphics g, in ViewTransform t, Rectangle viewBounds, OverlayResources resources)
            {
                if (_points.Length < 2)
                {
                    return;
                }
                Pen pen = resources.GetPen(Color, LineWidth);
                PointF first = t.ImageToDisplay(_points[0].X, _points[0].Y);
                PointF previous = first;
                for (int i = 1; i < _points.Length; i++)
                {
                    PointF current = t.ImageToDisplay(_points[i].X, _points[i].Y);
                    g.DrawLine(pen, previous, current);
                    previous = current;
                }
                if (_closed && _points.Length > 2)
                {
                    g.DrawLine(pen, previous, first);
                }
            }
        }

        private sealed class CrossOverlay : OverlayItem
        {
            private readonly PointF _center;
            private readonly float _size;

            public CrossOverlay(PointF center, float size, Color color, float lineWidth)
                : base(color, lineWidth)
            {
                _center = center;
                _size = size;
            }

            internal override void Draw(Graphics g, in ViewTransform t, Rectangle viewBounds, OverlayResources resources)
            {
                float half = _size / 2F;
                Pen pen = resources.GetPen(Color, LineWidth);
                g.DrawLine(pen, t.ImageToDisplay(_center.X - half, _center.Y), t.ImageToDisplay(_center.X + half, _center.Y));
                g.DrawLine(pen, t.ImageToDisplay(_center.X, _center.Y - half), t.ImageToDisplay(_center.X, _center.Y + half));
            }
        }

        private sealed class TextOverlay : OverlayItem
        {
            private readonly PointF _point;
            private readonly string _text;
            private readonly float _fontSize;

            public TextOverlay(PointF point, string text, Color color, float fontSize)
                : base(color, 1F)
            {
                _point = point;
                _text = text ?? string.Empty;
                _fontSize = fontSize;
            }

            internal override void Draw(Graphics g, in ViewTransform t, Rectangle viewBounds, OverlayResources resources)
            {
                PointF p = t.ImageToDisplay(_point.X, _point.Y);
                resources.DrawText(g, _text, _fontSize, Color, p.X, p.Y);
            }
        }

        private sealed class LabelOverlay : OverlayItem
        {
            private const int Margin = 8;
            private readonly string _text;
            private readonly ContentAlignment _anchor;
            private readonly float _fontSize;

            public LabelOverlay(string text, Color color, ContentAlignment anchor, float fontSize)
                : base(color, 1F)
            {
                _text = text ?? string.Empty;
                _anchor = anchor;
                _fontSize = fontSize;
            }

            internal override void Draw(Graphics g, in ViewTransform t, Rectangle viewBounds, OverlayResources resources)
            {
                SizeF size = g.MeasureString(_text, resources.GetFont(_fontSize));
                float x;
                float y;
                switch (_anchor)
                {
                    case ContentAlignment.TopCenter:
                    case ContentAlignment.MiddleCenter:
                    case ContentAlignment.BottomCenter:
                        x = viewBounds.Left + (viewBounds.Width - size.Width) / 2F;
                        break;
                    case ContentAlignment.TopRight:
                    case ContentAlignment.MiddleRight:
                    case ContentAlignment.BottomRight:
                        x = viewBounds.Right - size.Width - Margin;
                        break;
                    default:
                        x = viewBounds.Left + Margin;
                        break;
                }
                switch (_anchor)
                {
                    case ContentAlignment.MiddleLeft:
                    case ContentAlignment.MiddleCenter:
                    case ContentAlignment.MiddleRight:
                        y = viewBounds.Top + (viewBounds.Height - size.Height) / 2F;
                        break;
                    case ContentAlignment.BottomLeft:
                    case ContentAlignment.BottomCenter:
                    case ContentAlignment.BottomRight:
                        y = viewBounds.Bottom - size.Height - Margin;
                        break;
                    default:
                        y = viewBounds.Top + Margin;
                        break;
                }
                resources.DrawText(g, _text, _fontSize, Color, x, y);
            }
        }
    }

    /// <summary>
    /// 오버레이를 그릴 때 쓰는 펜·브러시·글꼴을 색/크기별로 보관해 그릴 때마다 새로 만들지 않게 합니다.
    /// UI 스레드에서만 사용합니다.
    /// </summary>
    internal sealed class OverlayResources : IDisposable
    {
        private const int MaxCached = 64;
        private readonly Dictionary<long, Pen> _pens = new Dictionary<long, Pen>();
        private readonly Dictionary<int, SolidBrush> _brushes = new Dictionary<int, SolidBrush>();
        private readonly Dictionary<int, Font> _fonts = new Dictionary<int, Font>();

        public Pen GetPen(Color color, float width)
        {
            long key = ((long)color.ToArgb() << 32) | (uint)(int)(width * 16F);
            Pen pen;
            if (!_pens.TryGetValue(key, out pen))
            {
                if (_pens.Count >= MaxCached)
                {
                    DisposeAll(_pens);
                }
                pen = new Pen(color, width);
                _pens.Add(key, pen);
            }
            return pen;
        }

        public SolidBrush GetBrush(Color color)
        {
            int key = color.ToArgb();
            SolidBrush brush;
            if (!_brushes.TryGetValue(key, out brush))
            {
                if (_brushes.Count >= MaxCached)
                {
                    DisposeAll(_brushes);
                }
                brush = new SolidBrush(color);
                _brushes.Add(key, brush);
            }
            return brush;
        }

        public Font GetFont(float sizeInPixels)
        {
            int key = (int)(Math.Max(4F, sizeInPixels) * 4F);
            Font font;
            if (!_fonts.TryGetValue(key, out font))
            {
                if (_fonts.Count >= MaxCached)
                {
                    DisposeAll(_fonts);
                }
                font = new Font(SystemFonts.MessageBoxFont.FontFamily, key / 4F, FontStyle.Bold, GraphicsUnit.Pixel);
                _fonts.Add(key, font);
            }
            return font;
        }

        /// <summary>밝은 영상 위에서도 읽히도록 검은 그림자를 한 픽셀 밀어 먼저 그립니다.</summary>
        public void DrawText(Graphics g, string text, float fontSize, Color color, float x, float y)
        {
            Font font = GetFont(fontSize);
            g.DrawString(text, font, GetBrush(Color.Black), x + 1F, y + 1F);
            g.DrawString(text, font, GetBrush(color), x, y);
        }

        private static void DisposeAll<TKey, TValue>(Dictionary<TKey, TValue> items) where TValue : IDisposable
        {
            foreach (TValue item in items.Values)
            {
                item.Dispose();
            }
            items.Clear();
        }

        public void Dispose()
        {
            DisposeAll(_pens);
            DisposeAll(_brushes);
            DisposeAll(_fonts);
        }
    }
}
