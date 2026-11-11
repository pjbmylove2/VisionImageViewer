using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace VisionImageViewer
{
    public sealed partial class ImageViewer
    {
        private const double PixelGridMinScale = 8.0;

        private IntPtr _bitmapInfo;                                 // BITMAPINFOHEADER + 회색 팔레트 256개
        private readonly FrameBuffer _viewBuffer = new FrameBuffer(); // 보이는 픽셀만 담는 화면 크기 버퍼
        private int[] _xMap;                                        // 화면 X → 이미지 X 대응표
        private readonly OverlayResources _overlayResources = new OverlayResources();
        private SolidBrush _backBrush;
        private Point _paintedScroll;

        private unsafe void InitializeRenderResources()
        {
            _bitmapInfo = Marshal.AllocHGlobal(NativeMethods.BitmapInfoHeaderSize + 256 * 4);
            byte* palette = (byte*)_bitmapInfo + NativeMethods.BitmapInfoHeaderSize;
            for (int i = 0; i < 256; i++)
            {
                palette[i * 4] = (byte)i;       // B
                palette[i * 4 + 1] = (byte)i;   // G
                palette[i * 4 + 2] = (byte)i;   // R
                palette[i * 4 + 3] = 0;
            }
        }

        private void DisposeRenderResources(bool disposing)
        {
            if (disposing)
            {
                _viewBuffer.Dispose();
                _overlayResources.Dispose();
                if (_backBrush != null)
                {
                    _backBrush.Dispose();
                    _backBrush = null;
                }
            }
            IntPtr info = Interlocked.Exchange(ref _bitmapInfo, IntPtr.Zero);
            if (info != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(info);
            }
        }

        /// <inheritdoc/>
        protected override void OnBackColorChanged(EventArgs e)
        {
            base.OnBackColorChanged(e);
            if (_backBrush != null)
            {
                _backBrush.Dispose();
                _backBrush = null;
            }
            Invalidate();
        }

        /// <inheritdoc/>
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // 배경은 OnPaint에서 영상 바깥 부분만 칠합니다. 여기서 전체를 칠하면 같은 영역을 두 번 그리게 됩니다.
        }

        /// <inheritdoc/>
        protected override void OnPaint(PaintEventArgs e)
        {
            if (_bitmapInfo == IntPtr.Zero)
            {
                return;
            }
            long start = Stopwatch.GetTimestamp();
            bool sizeChanged;
            bool more;
            bool newFrame = AcceptPendingFrame(out sizeChanged, out more);

            Graphics g = e.Graphics;
            Rectangle client = ClientRectangle;
            Point scroll = AutoScrollPosition;
            if (scroll != _paintedScroll)
            {
                // 스크롤 위치를 밖에서 바꾸면 Windows는 기존 화면을 밀고 새로 드러난 띠만 다시 그리게 합니다.
                // 배율이 정수가 아닐 때 밀린 부분과 새로 그린 부분이 한 픽셀 어긋날 수 있어 전체를 한 번 더 그립니다.
                _paintedScroll = scroll;
                if (e.ClipRectangle != client)
                {
                    Invalidate();
                }
            }
            FrameBuffer frame = _current;
            if (frame == null)
            {
                PaintEmpty(g, client);
            }
            else
            {
                ViewTransform t = GetTransform(frame);
                PaintMargins(g, client, t);
                IntPtr hdc = g.GetHdc();
                try
                {
                    if (UseDirectBlit(frame, t))
                    {
                        BlitDirect(hdc, frame, t, client.Width, client.Height);
                    }
                    else
                    {
                        BlitSampled(hdc, frame, t, client.Width, client.Height);
                    }
                }
                finally
                {
                    g.ReleaseHdc(hdc);
                }
                PaintDecorations(g, frame, t, client);
            }

            base.OnPaint(e);    // Paint 이벤트: 호스트가 ImageToClient로 직접 더 그릴 수 있습니다.

            long elapsed = Stopwatch.GetTimestamp() - start;
            lock (_gate)
            {
                _paintTicksLast = elapsed;
                _paintTicksTotal += elapsed;
                _paintCount++;
            }

            if (newFrame)
            {
                // 다음 프레임은 "이번 그리기에 걸린 시간(최소 1ms)만큼 쉰 뒤"부터 그립니다.
                // UI 스레드가 그리기만 하느라 타이머나 다른 작업을 못 하는 일을 막습니다.
                long end = start + elapsed;
                long next = end + Math.Max(elapsed, Stopwatch.Frequency / 1000);
                int maxFps = Volatile.Read(ref _maxDisplayFps);
                if (maxFps > 0)
                {
                    next = Math.Max(next, start + Stopwatch.Frequency / maxFps);
                }
                Interlocked.Exchange(ref _nextPaintTicks, next);
                EventHandler handler = FrameDisplayed;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
            if (sizeChanged)
            {
                // 영상 크기가 바뀌면 스크롤 범위를 다시 잡고 한 번 더 그립니다(같은 크기가 이어지는 동안은 실행되지 않음).
                UpdateScrollArea();
                Invalidate();
            }
            if (more)
            {
                RequestPaint(false);    // Queue 정책: 남은 프레임을 같은 간격 규칙으로 이어서 그립니다.
            }
        }

        private void PaintEmpty(Graphics g, Rectangle client)
        {
            g.FillRectangle(GetBackBrush(), client);
            string text = DesignMode ? Name : _strings.Empty;
            if (!string.IsNullOrEmpty(text))
            {
                Color textColor = BackColor.GetBrightness() < 0.5F ? Color.Silver : Color.DimGray;
                TextRenderer.DrawText(g, text, Font, client, textColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }

        private SolidBrush GetBackBrush()
        {
            if (_backBrush == null)
            {
                _backBrush = new SolidBrush(BackColor);
            }
            return _backBrush;
        }

        /// <summary>영상이 덮지 않는 가장자리만 배경색으로 칠합니다.</summary>
        private void PaintMargins(Graphics g, Rectangle client, in ViewTransform t)
        {
            Rectangle image = Rectangle.Intersect(client,
                new Rectangle(t.OriginX, t.OriginY, t.DisplayWidth, t.DisplayHeight));
            SolidBrush brush = GetBackBrush();
            if (image.Width <= 0 || image.Height <= 0)
            {
                g.FillRectangle(brush, client);
                return;
            }
            if (image.Top > client.Top)
            {
                g.FillRectangle(brush, client.Left, client.Top, client.Width, image.Top - client.Top);
            }
            if (image.Bottom < client.Bottom)
            {
                g.FillRectangle(brush, client.Left, image.Bottom, client.Width, client.Bottom - image.Bottom);
            }
            if (image.Left > client.Left)
            {
                g.FillRectangle(brush, client.Left, image.Top, image.Left - client.Left, image.Height);
            }
            if (image.Right < client.Right)
            {
                g.FillRectangle(brush, image.Right, image.Top, client.Right - image.Right, image.Height);
            }
        }

        private unsafe void PrepareBitmapInfo(int width, int rows, int bitCount)
        {
            NativeMethods.BitmapInfoHeader* header = (NativeMethods.BitmapInfoHeader*)_bitmapInfo;
            header->Size = NativeMethods.BitmapInfoHeaderSize;
            header->Width = width;
            header->Height = -rows;         // 음수 = 위에서 아래로 저장된 영상
            header->Planes = 1;
            header->BitCount = (ushort)bitCount;
            header->Compression = 0;        // BI_RGB
            header->SizeImage = 0;
            header->XPelsPerMeter = 0;
            header->YPelsPerMeter = 0;
            header->ClrUsed = bitCount == 8 ? 256u : 0u;
            header->ClrImportant = 0;
        }

        /// <summary>
        /// 8bit 영상(흑백/BGR/BGRA)을 변환 없이 프레임 버퍼에서 화면으로 바로 그립니다.
        /// 보이는 줄부터 시작하는 주소와 보이는 줄 수만 GDI에 알려 주므로, 원본이 아무리 커도
        /// 화면에 보이는 부분만 처리합니다.
        /// </summary>
        private unsafe void BlitDirect(IntPtr hdc, FrameBuffer f, in ViewTransform t, int clientWidth, int clientHeight)
        {
            int left = Math.Max(t.OriginX, 0);
            int top = Math.Max(t.OriginY, 0);
            int right = Math.Min(t.OriginX + t.DisplayWidth, clientWidth);
            int bottom = Math.Min(t.OriginY + t.DisplayHeight, clientHeight);
            if (right <= left || bottom <= top)
            {
                return;
            }

            // 보이는 화면 범위를 덮는 이미지 픽셀 범위 [sx0, sx1) × [sy0, sy1)
            int sx0 = Clamp((int)Math.Floor((left - t.OriginX) / t.ScaleX), 0, f.Width - 1);
            int sx1 = Clamp((int)Math.Ceiling((right - t.OriginX) / t.ScaleX), sx0 + 1, f.Width);
            int sy0 = Clamp((int)Math.Floor((top - t.OriginY) / t.ScaleY), 0, f.Height - 1);
            int sy1 = Clamp((int)Math.Ceiling((bottom - t.OriginY) / t.ScaleY), sy0 + 1, f.Height);

            // 그 픽셀 범위가 화면에서 차지하는 자리(화면 밖으로 조금 나갈 수 있고, 나간 부분은 잘립니다)
            int dx0 = t.OriginX + (int)Math.Round(sx0 * t.ScaleX);
            int dx1 = Math.Max(dx0 + 1, t.OriginX + (int)Math.Round(sx1 * t.ScaleX));
            int dy0 = t.OriginY + (int)Math.Round(sy0 * t.ScaleY);
            int dy1 = Math.Max(dy0 + 1, t.OriginY + (int)Math.Round(sy1 * t.ScaleY));

            int rows = sy1 - sy0;
            PrepareBitmapInfo(f.Width, rows, f.BytesPerPixel * 8);
            IntPtr firstRow = new IntPtr(f.Row(sy0));

            bool shrinking = t.ScaleX < 1.0 || t.ScaleY < 1.0;
            if (shrinking && _zoomOutQuality == ZoomOutQuality.Smooth)
            {
                NativeMethods.SetStretchBltMode(hdc, NativeMethods.Halftone);
                NativeMethods.SetBrushOrgEx(hdc, 0, 0, IntPtr.Zero);
            }
            else
            {
                NativeMethods.SetStretchBltMode(hdc, NativeMethods.ColorOnColor);
            }
            NativeMethods.StretchDIBits(hdc, dx0, dy0, dx1 - dx0, dy1 - dy0,
                sx0, 0, sx1 - sx0, rows, firstRow, _bitmapInfo, NativeMethods.DibRgbColors, NativeMethods.SrcCopy);
        }

        /// <summary>
        /// 어느 경로로 그릴지 고릅니다(실측 기준, docs/Performance.ko.md).
        /// - 16bit: 화면용 8bit로 바꿔야 하므로 항상 샘플링.
        /// - 24bit 컬러: GDI의 24bit→화면 변환이 느려서 샘플링(보이는 픽셀만 32bit로 변환).
        /// - 축소 + Smooth: GDI의 평균 축소(HALFTONE)를 쓰기 위해 직접.
        /// - 그 외 8bit 흑백/32bit: 변환이 없으므로 직접.
        /// </summary>
        private bool UseDirectBlit(FrameBuffer f, in ViewTransform t)
        {
            if (f.Format == StoredFormat.Gray16)
            {
                return false;
            }
            bool shrinking = t.ScaleX < 1.0 || t.ScaleY < 1.0;
            if (shrinking)
            {
                return _zoomOutQuality == ZoomOutQuality.Smooth;
            }
            return f.Format != StoredFormat.Bgr24;
        }

        /// <summary>
        /// 화면에 보이는 픽셀만 골라 화면 크기 버퍼(흑백 8bit 또는 컬러 32bit)에 담은 뒤 1:1로 그립니다.
        /// 처리량이 원본 크기가 아니라 화면 크기에 비례합니다. 16bit 영상은 여기서 상위 8bit로 줄이며,
        /// 원본 값은 프레임 버퍼에 그대로 남아 픽셀 값 표시에 쓰입니다.
        /// </summary>
        private unsafe void BlitSampled(IntPtr hdc, FrameBuffer f, in ViewTransform t, int clientWidth, int clientHeight)
        {
            int left = Math.Max(t.OriginX, 0);
            int top = Math.Max(t.OriginY, 0);
            int right = Math.Min(t.OriginX + t.DisplayWidth, clientWidth);
            int bottom = Math.Min(t.OriginY + t.DisplayHeight, clientHeight);
            int viewWidth = right - left;
            int viewHeight = bottom - top;
            if (viewWidth <= 0 || viewHeight <= 0)
            {
                return;
            }

            bool colorOutput = f.Format == StoredFormat.Bgr24 || f.Format == StoredFormat.Bgra32;
            _viewBuffer.Prepare(viewWidth, viewHeight, colorOutput ? StoredFormat.Bgra32 : StoredFormat.Gray8, 8);
            if (_xMap == null || _xMap.Length < viewWidth)
            {
                _xMap = new int[viewWidth];
            }
            // 화면 X → 원본 줄 안에서의 바이트 위치
            int sourceBytesPerPixel = f.BytesPerPixel;
            for (int i = 0; i < viewWidth; i++)
            {
                int sx = Clamp((int)((left + i + 0.5 - t.OriginX) / t.ScaleX), 0, f.Width - 1);
                _xMap[i] = f.Format == StoredFormat.Gray16 ? sx : sx * sourceBytesPerPixel;
            }

            int shift = f.BitDepth - 8;
            int rowBytes = viewWidth * (colorOutput ? 4 : 1);
            int previousSourceRow = -1;
            fixed (int* map = _xMap)
            {
                for (int j = 0; j < viewHeight; j++)
                {
                    int sy = Clamp((int)((top + j + 0.5 - t.OriginY) / t.ScaleY), 0, f.Height - 1);
                    byte* destination = _viewBuffer.Row(j);
                    if (sy == previousSourceRow)
                    {
                        // 확대 중에는 같은 원본 줄이 여러 번 나옵니다. 방금 만든 줄을 그대로 복사합니다.
                        Buffer.MemoryCopy(_viewBuffer.Row(j - 1), destination, rowBytes, rowBytes);
                        continue;
                    }
                    previousSourceRow = sy;
                    byte* source = f.Row(sy);
                    switch (f.Format)
                    {
                        case StoredFormat.Gray8:
                            for (int i = 0; i < viewWidth; i++)
                            {
                                destination[i] = source[map[i]];
                            }
                            break;
                        case StoredFormat.Gray16:
                            ushort* source16 = (ushort*)source;
                            for (int i = 0; i < viewWidth; i++)
                            {
                                int value = source16[map[i]] >> shift;
                                destination[i] = (byte)(value > 255 ? 255 : value);
                            }
                            break;
                        case StoredFormat.Bgr24:
                            uint* color = (uint*)destination;
                            for (int i = 0; i < viewWidth; i++)
                            {
                                byte* p = source + map[i];
                                color[i] = (uint)(p[0] | (p[1] << 8) | (p[2] << 16));
                            }
                            break;
                        default:
                            uint* color32 = (uint*)destination;
                            for (int i = 0; i < viewWidth; i++)
                            {
                                color32[i] = *(uint*)(source + map[i]);
                            }
                            break;
                    }
                }
            }

            PrepareBitmapInfo(viewWidth, viewHeight, colorOutput ? 32 : 8);
            NativeMethods.SetStretchBltMode(hdc, NativeMethods.ColorOnColor);
            NativeMethods.StretchDIBits(hdc, left, top, viewWidth, viewHeight, 0, 0, viewWidth, viewHeight,
                _viewBuffer.Data, _bitmapInfo, NativeMethods.DibRgbColors, NativeMethods.SrcCopy);
        }

        /// <summary>영상 위에 격자·십자선·오버레이를 그립니다.</summary>
        private void PaintDecorations(Graphics g, FrameBuffer frame, in ViewTransform t, Rectangle client)
        {
            if (_showPixelGrid && t.ScaleX >= PixelGridMinScale && t.ScaleY >= PixelGridMinScale)
            {
                PaintPixelGrid(g, t, client);
            }
            if (_showCrosshair)
            {
                Pen pen = _overlayResources.GetPen(Color.FromArgb(180, Color.Lime), 1F);
                PointF center = t.ImageToDisplay(t.ImageWidth / 2.0, t.ImageHeight / 2.0);
                g.DrawLine(pen, t.OriginX, center.Y, t.OriginX + t.DisplayWidth, center.Y);
                g.DrawLine(pen, center.X, t.OriginY, center.X, t.OriginY + t.DisplayHeight);
            }
            OverlayItem[] overlays = frame.Overlays;
            if (overlays == null)
            {
                return;
            }
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            for (int i = 0; i < overlays.Length; i++)
            {
                OverlayItem item = overlays[i];
                if (item == null)
                {
                    continue;
                }
                try
                {
                    item.Draw(g, t, client, _overlayResources);
                }
                catch (OverflowException)
                {
                    // 크게 확대한 상태에서 화면 밖 아주 먼 좌표는 GDI+가 그리지 못합니다. 그 도형만 건너뜁니다.
                }
            }
        }

        private void PaintPixelGrid(Graphics g, in ViewTransform t, Rectangle client)
        {
            Pen pen = _overlayResources.GetPen(Color.FromArgb(90, 128, 128, 128), 1F);
            int left = Math.Max(t.OriginX, 0);
            int top = Math.Max(t.OriginY, 0);
            int right = Math.Min(t.OriginX + t.DisplayWidth, client.Width);
            int bottom = Math.Min(t.OriginY + t.DisplayHeight, client.Height);
            int firstColumn = Math.Max(0, (int)Math.Floor((left - t.OriginX) / t.ScaleX));
            int lastColumn = Math.Min(t.ImageWidth, (int)Math.Ceiling((right - t.OriginX) / t.ScaleX));
            for (int column = firstColumn; column <= lastColumn; column++)
            {
                int x = t.OriginX + (int)Math.Round(column * t.ScaleX);
                g.DrawLine(pen, x, top, x, bottom);
            }
            int firstRow = Math.Max(0, (int)Math.Floor((top - t.OriginY) / t.ScaleY));
            int lastRow = Math.Min(t.ImageHeight, (int)Math.Ceiling((bottom - t.OriginY) / t.ScaleY));
            for (int row = firstRow; row <= lastRow; row++)
            {
                int y = t.OriginY + (int)Math.Round(row * t.ScaleY);
                g.DrawLine(pen, left, y, right, y);
            }
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
