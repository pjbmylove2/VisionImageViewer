using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace VisionImageViewer
{
    /// <summary>
    /// 비전 프로젝트 공용 이미지 뷰어입니다.
    /// <para>
    /// - SetImage는 어느 스레드에서든 호출할 수 있고, 영상을 내부 버퍼로 복사한 뒤 바로 돌아옵니다.
    ///   호출이 끝나면 원본 버퍼는 해제하거나 다음 프레임에 재사용해도 됩니다.<br/>
    /// - 화면은 UI 스레드가 한가할 때(WM_PAINT) 가장 최근 프레임으로 그립니다. 입력이 빨라도
    ///   UI가 멈추지 않고, 마지막 프레임은 반드시 표시됩니다.<br/>
    /// - 확대/축소·보기 방식 등 화면 조작 속성과 메서드는 UI 스레드에서 호출합니다.
    /// </para>
    /// </summary>
    [DesignerCategory("Code")]
    [DefaultEvent("PixelHover")]
    public sealed partial class ImageViewer : ScrollableControl
    {
        private readonly object _gate = new object();
        private readonly FramePool _pool = new FramePool();
        private readonly Queue<FrameBuffer> _queue = new Queue<FrameBuffer>();
        private FrameBuffer _current;           // UI 스레드 전용: 지금 화면에 그려진 프레임
        private bool _disposed;                 // _gate
        private bool _clearRequested;           // _gate
        private IntPtr _hwnd;                   // 다른 스레드에서 다시 그리기를 요청할 때 쓰는 창 핸들

        private DisplayPolicy _displayPolicy;   // _gate
        private int _queueCapacity;             // _gate
        private int _maxDisplayFps;
        private long _nextPaintTicks;           // 이 시각 전에는 새 프레임 그리기를 요청하지 않음
        private int _deferArmed;
        private System.Threading.Timer _deferTimer;

        // 통계 (_gate)
        private long _received;
        private long _displayed;
        private long _skipped;
        private long _copyTicksLast;
        private long _copyTicksTotal;
        private long _paintTicksLast;
        private long _paintTicksTotal;
        private long _paintCount;

        // 보기 상태 (UI 스레드 전용)
        private ViewMode _viewMode;
        private double _zoom = 1.0;
        private double _minZoom;
        private double _maxZoom;
        private double _zoomStep;
        private bool _inspectionMode;
        private ZoomOutQuality _zoomOutQuality;
        private bool _showPixelGrid;
        private bool _showCrosshair;
        private ViewerStrings _strings;

        // 연속 확대 기준점 (UI 스레드 전용)
        private bool _anchorValid;
        private Point _anchorClient;
        private Point _anchorScroll;
        private Size _anchorImageSize;
        private double _anchorImageX;
        private double _anchorImageY;

        /// <summary>마우스가 가리키는 이미지 좌표와 원본 픽셀 값이 바뀔 때 발생합니다.</summary>
        public event EventHandler<PixelHoverEventArgs> PixelHover;
        /// <summary>배율이나 보기 방식이 바뀔 때 발생합니다.</summary>
        public event EventHandler ZoomChanged;
        /// <summary>새 프레임을 화면에 그린 직후 발생합니다(UI 스레드).</summary>
        public event EventHandler FrameDisplayed;

        /// <summary>
        /// 모든 뷰어가 공통으로 쓰는 외부 파일 디코더입니다. OpenCvSharp 어댑터 등이 프로그램 시작 시 한 번 등록합니다.
        /// 개별 뷰어의 <see cref="FileDecoder"/>가 있으면 그쪽이 우선합니다.
        /// </summary>
        public static ImageFileDecoder DefaultFileDecoder { get; set; }

        /// <summary>기본 설정(검정 배경, 화면 맞춤)으로 만듭니다. 디자이너가 사용합니다.</summary>
        public ImageViewer()
            : this(null)
        {
        }

        /// <summary>배경색 등 초기 설정을 지정해 만듭니다.</summary>
        public ImageViewer(ImageViewerOptions options)
        {
            ImageViewerOptions o = options ?? new ImageViewerOptions();
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            AutoScroll = true;
            TabStop = true;
            BackColor = o.BackColor;

            _viewMode = o.ViewMode;
            _minZoom = Math.Max(0.0001, o.MinZoom);
            _maxZoom = Math.Max(_minZoom, o.MaxZoom);
            _zoomStep = o.ZoomStep > 1.0 ? o.ZoomStep : 1.2;
            _zoomOutQuality = o.ZoomOutQuality;
            _displayPolicy = o.DisplayPolicy;
            _queueCapacity = Math.Max(1, o.QueueCapacity);
            _maxDisplayFps = Math.Max(0, o.MaxDisplayFps);
            _inspectionMode = o.InspectionMode;
            FocusOnMouseEnter = o.FocusOnMouseEnter;
            PanWithMouseDrag = o.PanWithMouseDrag;
            _showPixelGrid = o.ShowPixelGrid;
            _showCrosshair = o.ShowCrosshair;
            _strings = o.Strings ?? new ViewerStrings();
            UpdatePoolLimit();

            InitializeRenderResources();
            BuildMenu();
        }

        /// <inheritdoc/>
        protected override Size DefaultSize
        {
            get { return new Size(320, 240); }
        }

        // ------------------------------------------------------------------
        // 입력 (어느 스레드에서든 호출 가능)
        // ------------------------------------------------------------------

        /// <summary>
        /// 원시 버퍼의 영상을 표시합니다. 카메라 SDK나 영상 라이브러리의 버퍼 포인터를 그대로 넘기면 됩니다.
        /// </summary>
        /// <param name="data">첫 줄 첫 픽셀의 주소.</param>
        /// <param name="width">가로 픽셀 수.</param>
        /// <param name="height">세로 픽셀 수.</param>
        /// <param name="stride">한 줄의 바이트 수(패딩 포함). 아래에서 위로 저장된 버퍼는 음수.</param>
        /// <param name="format">픽셀 배치.</param>
        /// <param name="overlays">이 프레임과 함께 그릴 도형. 없으면 null.</param>
        public unsafe void SetImage(IntPtr data, int width, int height, int stride,
            ViewerPixelFormat format, IReadOnlyList<OverlayItem> overlays = null)
        {
            if (data == IntPtr.Zero)
            {
                throw new ArgumentNullException(nameof(data));
            }
            ValidateGeometry(width, height, stride, PixelConverter.GetBytesPerPixel(format));
            SubmitFrame((byte*)data, width, height, stride, format, overlays);
        }

        /// <summary>관리 배열(예: Basler Pylon의 PixelData)의 영상을 표시합니다.</summary>
        public unsafe void SetImage(byte[] data, int width, int height, int stride,
            ViewerPixelFormat format, IReadOnlyList<OverlayItem> overlays = null)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }
            int bytesPerPixel = PixelConverter.GetBytesPerPixel(format);
            ValidateGeometry(width, height, stride, bytesPerPixel);
            if (stride < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(stride));
            }
            long needed = (long)stride * (height - 1) + (long)width * bytesPerPixel;
            if (data.Length < needed)
            {
                throw new ArgumentException("배열 길이가 영상 크기보다 작습니다.", nameof(data));
            }
            fixed (byte* p = data)
            {
                SubmitFrame(p, width, height, stride, format, overlays);
            }
        }

        /// <summary>
        /// R, G, B가 각각 별도 평면인 8bit 컬러 영상을 표시합니다(MIL·HALCON의 기본 컬러 배치).
        /// </summary>
        public unsafe void SetImagePlanar(IntPtr red, IntPtr green, IntPtr blue, int width, int height, int stride,
            IReadOnlyList<OverlayItem> overlays = null)
        {
            if (red == IntPtr.Zero || green == IntPtr.Zero || blue == IntPtr.Zero)
            {
                throw new ArgumentNullException(nameof(red));
            }
            ValidateGeometry(width, height, stride, 1);
            if (IsViewerDisposed())
            {
                return;
            }
            long start = Stopwatch.GetTimestamp();
            FrameBuffer buffer = _pool.Rent();
            try
            {
                PixelConverter.CopyPlanarIn((byte*)red, (byte*)green, (byte*)blue, width, height, stride, buffer);
                buffer.Overlays = SnapshotOverlays(overlays);
            }
            catch
            {
                _pool.Return(buffer);
                throw;
            }
            Post(buffer, Stopwatch.GetTimestamp() - start);
        }

        /// <summary>현재 영상과 대기 중인 프레임을 모두 지웁니다. 어느 스레드에서든 호출할 수 있습니다.</summary>
        public void ClearImage()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }
                while (_queue.Count > 0)
                {
                    _pool.Return(_queue.Dequeue());
                }
                _clearRequested = true;
            }
            RequestPaint(true);
        }

        private static void ValidateGeometry(int width, int height, int stride, int bytesPerPixel)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }
            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }
            if (Math.Abs((long)stride) < (long)width * bytesPerPixel)
            {
                throw new ArgumentException("stride가 한 줄의 픽셀 바이트 수보다 작습니다.", nameof(stride));
            }
        }

        private unsafe void SubmitFrame(byte* data, int width, int height, int stride,
            ViewerPixelFormat format, IReadOnlyList<OverlayItem> overlays)
        {
            if (IsViewerDisposed())
            {
                return;
            }
            long start = Stopwatch.GetTimestamp();
            FrameBuffer buffer = _pool.Rent();
            try
            {
                PixelConverter.CopyIn(data, width, height, stride, format, buffer);
                buffer.Overlays = SnapshotOverlays(overlays);
            }
            catch
            {
                _pool.Return(buffer);
                throw;
            }
            Post(buffer, Stopwatch.GetTimestamp() - start);
        }

        /// <summary>호출한 쪽이 목록을 나중에 고쳐도 영향이 없도록 배열로 떠 둡니다.</summary>
        private static OverlayItem[] SnapshotOverlays(IReadOnlyList<OverlayItem> overlays)
        {
            if (overlays == null || overlays.Count == 0)
            {
                return null;
            }
            OverlayItem[] copy = new OverlayItem[overlays.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = overlays[i];
            }
            return copy;
        }

        /// <summary>복사를 마친 프레임을 대기열에 올리고 화면 갱신을 요청합니다.</summary>
        private void Post(FrameBuffer buffer, long copyTicks)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    buffer.Dispose();
                    return;
                }
                _received++;
                _copyTicksLast = copyTicks;
                _copyTicksTotal += copyTicks;
                int capacity = _displayPolicy == DisplayPolicy.Queue ? _queueCapacity : 1;
                while (_queue.Count >= capacity)
                {
                    // 아직 그리지 못한 오래된 프레임을 새 프레임에 양보합니다.
                    _pool.Return(_queue.Dequeue());
                    _skipped++;
                }
                _queue.Enqueue(buffer);
            }
            RequestPaint(false);
        }

        private bool IsViewerDisposed()
        {
            lock (_gate)
            {
                return _disposed;
            }
        }

        /// <summary>
        /// 다시 그리기를 요청합니다. WM_PAINT는 여러 번 요청해도 한 번으로 합쳐지고 사용자 입력보다
        /// 우선순위가 낮아, 프레임이 아무리 빨리 들어와도 키·마우스 입력이 밀리지 않습니다.
        /// 다만 WM_PAINT가 쉬지 않고 이어지면 그보다 우선순위가 낮은 타이머(WM_TIMER)가 굶으므로,
        /// 직전 그리기에 걸린 시간만큼은 쉬었다가 다음 그리기를 요청합니다(_nextPaintTicks).
        /// </summary>
        private void RequestPaint(bool immediate)
        {
            IntPtr hwnd = Volatile.Read(ref _hwnd);
            if (hwnd == IntPtr.Zero)
            {
                return;
            }
            if (!immediate)
            {
                long due = Interlocked.Read(ref _nextPaintTicks);
                long now = Stopwatch.GetTimestamp();
                if (now < due)
                {
                    // 아직 이르면 남은 시간 뒤에 한 번만 요청합니다. 그 사이 들어온 프레임은 최신 것으로 교체됩니다.
                    if (Interlocked.Exchange(ref _deferArmed, 1) == 0)
                    {
                        int delayMs = (int)((due - now) * 1000 / Stopwatch.Frequency) + 1;
                        ArmDeferTimer(delayMs);
                    }
                    return;
                }
            }
            NativeMethods.InvalidateRect(hwnd, IntPtr.Zero, false);
        }

        private void ArmDeferTimer(int delayMs)
        {
            try
            {
                lock (_gate)
                {
                    if (_disposed)
                    {
                        return;
                    }
                    if (_deferTimer == null)
                    {
                        _deferTimer = new System.Threading.Timer(OnDeferTimer, null, Timeout.Infinite, Timeout.Infinite);
                    }
                    _deferTimer.Change(delayMs, Timeout.Infinite);
                }
            }
            catch (ObjectDisposedException)
            {
                Interlocked.Exchange(ref _deferArmed, 0);
            }
        }

        private void OnDeferTimer(object state)
        {
            Interlocked.Exchange(ref _deferArmed, 0);
            IntPtr hwnd = Volatile.Read(ref _hwnd);
            if (hwnd != IntPtr.Zero)
            {
                NativeMethods.InvalidateRect(hwnd, IntPtr.Zero, false);
            }
        }

        /// <summary>대기 중인 프레임을 표시 프레임으로 넘겨받습니다. OnPaint에서만 호출합니다.</summary>
        private bool AcceptPendingFrame(out bool sizeChanged, out bool more)
        {
            FrameBuffer next = null;
            bool cleared = false;
            lock (_gate)
            {
                if (_clearRequested)
                {
                    _clearRequested = false;
                    cleared = true;
                }
                if (_queue.Count > 0)
                {
                    next = _queue.Dequeue();
                    _displayed++;
                }
                more = _queue.Count > 0;
            }

            sizeChanged = false;
            if (cleared && _current != null)
            {
                _pool.Return(_current);
                _current = null;
                sizeChanged = true;
            }
            if (next == null)
            {
                return false;
            }
            FrameBuffer previous = _current;
            if (previous == null || previous.Width != next.Width || previous.Height != next.Height)
            {
                sizeChanged = true;
            }
            _current = next;
            _pool.Return(previous);
            return true;
        }

        // ------------------------------------------------------------------
        // 상태 조회
        // ------------------------------------------------------------------

        /// <summary>표시 중인 영상이 있는지 여부.</summary>
        [Browsable(false)]
        public bool HasImage
        {
            get { return _current != null; }
        }

        /// <summary>표시 중인 영상의 크기(픽셀). 없으면 Size.Empty.</summary>
        [Browsable(false)]
        public Size ImageSize
        {
            get
            {
                FrameBuffer f = _current;
                return f == null ? Size.Empty : new Size(f.Width, f.Height);
            }
        }

        /// <summary>수신/표시/건너뜀 프레임 수와 복사·그리기 시간. 어느 스레드에서든 읽을 수 있습니다.</summary>
        [Browsable(false)]
        public ViewerStatistics Statistics
        {
            get
            {
                double toMs = 1000.0 / Stopwatch.Frequency;
                lock (_gate)
                {
                    ViewerStatistics s = new ViewerStatistics();
                    s.Received = _received;
                    s.Displayed = _displayed;
                    s.Skipped = _skipped;
                    s.LastCopyMs = _copyTicksLast * toMs;
                    s.AverageCopyMs = _received > 0 ? _copyTicksTotal * toMs / _received : 0.0;
                    s.LastPaintMs = _paintTicksLast * toMs;
                    s.AveragePaintMs = _paintCount > 0 ? _paintTicksTotal * toMs / _paintCount : 0.0;
                    return s;
                }
            }
        }

        /// <summary>통계를 0으로 되돌립니다.</summary>
        public void ResetStatistics()
        {
            lock (_gate)
            {
                _received = 0;
                _displayed = 0;
                _skipped = 0;
                _copyTicksLast = 0;
                _copyTicksTotal = 0;
                _paintTicksLast = 0;
                _paintTicksTotal = 0;
                _paintCount = 0;
            }
        }

        /// <summary>표시 중인 영상의 원본 픽셀 값을 읽습니다(UI 스레드). 좌표가 영상 밖이면 false.</summary>
        public unsafe bool TryGetPixel(int x, int y, out PixelValue value)
        {
            value = new PixelValue();
            FrameBuffer f = _current;
            if (f == null || x < 0 || y < 0 || x >= f.Width || y >= f.Height)
            {
                return false;
            }
            byte* p = f.Row(y) + (long)x * f.BytesPerPixel;
            switch (f.Format)
            {
                case StoredFormat.Gray8:
                    value.Channels = 1;
                    value.V0 = p[0];
                    break;
                case StoredFormat.Gray16:
                    value.Channels = 1;
                    value.V0 = *(ushort*)p;
                    break;
                case StoredFormat.Bgr24:
                    value.Channels = 3;
                    value.V0 = p[2];
                    value.V1 = p[1];
                    value.V2 = p[0];
                    break;
                default:
                    value.Channels = 4;
                    value.V0 = p[2];
                    value.V1 = p[1];
                    value.V2 = p[0];
                    value.V3 = p[3];
                    break;
            }
            return true;
        }

        /// <summary>이미지 좌표를 컨트롤 좌표로 바꿉니다. Paint 이벤트에서 직접 그릴 때 사용합니다.</summary>
        public PointF ImageToClient(PointF imagePoint)
        {
            FrameBuffer f = _current;
            return f == null ? PointF.Empty : GetTransform(f).ImageToDisplay(imagePoint.X, imagePoint.Y);
        }

        /// <summary>컨트롤 좌표를 이미지 좌표로 바꿉니다. 영상 밖이면 범위를 벗어난 값이 나옵니다.</summary>
        public PointF ClientToImage(Point clientPoint)
        {
            FrameBuffer f = _current;
            if (f == null)
            {
                return PointF.Empty;
            }
            double ix;
            double iy;
            GetTransform(f).DisplayToImage(clientPoint.X, clientPoint.Y, out ix, out iy);
            return new PointF((float)ix, (float)iy);
        }

        // ------------------------------------------------------------------
        // 보기 (UI 스레드)
        // ------------------------------------------------------------------

        /// <summary>화면 표시 방식.</summary>
        [Category("Viewer"), DefaultValue(ViewMode.Fit)]
        public ViewMode ViewMode
        {
            get { return _viewMode; }
            set
            {
                if (value == ViewMode.Custom)
                {
                    ZoomAt(Zoom, GetClientCenter());
                    return;
                }
                if (value == ViewMode.Actual)
                {
                    ZoomAt(1.0, GetClientCenter());
                    _viewMode = ViewMode.Actual;
                    return;
                }
                _viewMode = value;
                _anchorValid = false;
                UpdateScrollArea();
                Invalidate();
                OnZoomChanged();
            }
        }

        /// <summary>현재 배율(1.0 = 원본 크기). 값을 넣으면 화면 중앙을 기준으로 확대/축소합니다.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public double Zoom
        {
            get
            {
                FrameBuffer f = _current;
                if (f == null)
                {
                    return _viewMode == ViewMode.Actual ? 1.0 : _zoom;
                }
                return GetTransform(f).ScaleX;
            }
            set { ZoomAt(value, GetClientCenter()); }
        }

        /// <summary>최소 배율.</summary>
        [Category("Viewer"), DefaultValue(0.02)]
        public double MinZoom
        {
            get { return _minZoom; }
            set { _minZoom = Math.Max(0.0001, Math.Min(value, _maxZoom)); }
        }

        /// <summary>최대 배율.</summary>
        [Category("Viewer"), DefaultValue(64.0)]
        public double MaxZoom
        {
            get { return _maxZoom; }
            set { _maxZoom = Math.Max(value, _minZoom); }
        }

        /// <summary>휠 한 칸 또는 +/- 한 번에 곱해지는 배율(1보다 커야 함).</summary>
        [Category("Viewer"), DefaultValue(1.2)]
        public double ZoomStep
        {
            get { return _zoomStep; }
            set
            {
                if (value > 1.0)
                {
                    _zoomStep = value;
                }
            }
        }

        /// <summary>
        /// 검사 모드. true면 휠과 +/- 키로 배율이 바뀌지 않고, 파일 열기(메뉴·끌어놓기)도 막습니다.
        /// 우클릭 메뉴의 보기 전환은 그대로 쓸 수 있습니다.
        /// </summary>
        [Category("Viewer"), DefaultValue(false)]
        public bool InspectionMode
        {
            get { return _inspectionMode; }
            set { _inspectionMode = value; }
        }

        /// <summary>축소 표시 화질.</summary>
        [Category("Viewer"), DefaultValue(ZoomOutQuality.Fast)]
        public ZoomOutQuality ZoomOutQuality
        {
            get { return _zoomOutQuality; }
            set
            {
                _zoomOutQuality = value;
                Invalidate();
            }
        }

        /// <summary>입력이 화면 갱신보다 빠를 때의 처리 방식.</summary>
        [Category("Viewer"), DefaultValue(DisplayPolicy.LatestWins)]
        public DisplayPolicy DisplayPolicy
        {
            get { lock (_gate) { return _displayPolicy; } }
            set
            {
                lock (_gate)
                {
                    _displayPolicy = value;
                }
                UpdatePoolLimit();
            }
        }

        /// <summary>Queue 정책에서 쌓아 둘 최대 프레임 수. 프레임 크기 × 이 값만큼 메모리를 더 씁니다.</summary>
        [Category("Viewer"), DefaultValue(8)]
        public int QueueCapacity
        {
            get { lock (_gate) { return _queueCapacity; } }
            set
            {
                lock (_gate)
                {
                    _queueCapacity = Math.Max(1, value);
                }
                UpdatePoolLimit();
            }
        }

        /// <summary>
        /// 초당 최대 화면 갱신 횟수. 0이면 따로 제한하지 않고, UI 스레드가 절반 이상 쉴 수 있는 범위에서 가능한 만큼 그립니다.
        /// 값을 주면 그보다 빠른 입력은 건너뛰어 CPU를 아낍니다(마지막 프레임은 그래도 표시).
        /// </summary>
        [Category("Viewer"), DefaultValue(0)]
        public int MaxDisplayFps
        {
            get { return Volatile.Read(ref _maxDisplayFps); }
            set { Volatile.Write(ref _maxDisplayFps, Math.Max(0, value)); }
        }

        /// <summary>마우스가 올라오면 +/- 키를 받도록 포커스를 가져옵니다. 글자 입력 중인 컨트롤에서는 뺏지 않습니다.</summary>
        [Category("Viewer"), DefaultValue(true)]
        public bool FocusOnMouseEnter { get; set; }

        /// <summary>확대 상태에서 왼쪽 버튼 드래그로 화면을 이동합니다.</summary>
        [Category("Viewer"), DefaultValue(true)]
        public bool PanWithMouseDrag { get; set; }

        /// <summary>8배 이상 확대했을 때 픽셀 경계선을 표시합니다.</summary>
        [Category("Viewer"), DefaultValue(false)]
        public bool ShowPixelGrid
        {
            get { return _showPixelGrid; }
            set
            {
                _showPixelGrid = value;
                Invalidate();
            }
        }

        /// <summary>이미지 중심 십자선을 표시합니다.</summary>
        [Category("Viewer"), DefaultValue(false)]
        public bool ShowCrosshair
        {
            get { return _showCrosshair; }
            set
            {
                _showCrosshair = value;
                Invalidate();
            }
        }

        /// <summary>메뉴와 안내 문구. 값을 바꾼 객체를 넣으면 메뉴에 바로 반영됩니다.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ViewerStrings Strings
        {
            get { return _strings; }
            set
            {
                _strings = value ?? new ViewerStrings();
                ApplyStrings();
                Invalidate();
            }
        }

        /// <summary>마우스 위치(컨트롤 밖이면 화면 중앙)를 기준으로 한 단계 확대합니다.</summary>
        public void ZoomIn()
        {
            ZoomAt(Zoom * _zoomStep, GetZoomAnchor());
        }

        /// <summary>마우스 위치(컨트롤 밖이면 화면 중앙)를 기준으로 한 단계 축소합니다.</summary>
        public void ZoomOut()
        {
            ZoomAt(Zoom / _zoomStep, GetZoomAnchor());
        }

        /// <summary>비율을 유지하며 화면에 맞춥니다.</summary>
        public void ZoomToFit()
        {
            ViewMode = ViewMode.Fit;
        }

        /// <summary>원본 크기(1:1)로 표시합니다.</summary>
        public void ZoomToActual()
        {
            ViewMode = ViewMode.Actual;
        }

        /// <summary>
        /// 지정한 배율로 바꾸되, 컨트롤 좌표 clientAnchor 아래에 있던 이미지 지점이 그 자리에 그대로 있게 합니다.
        /// 확대된 영상이 컨트롤보다 커지면 스크롤바가 자동으로 나타납니다.
        /// </summary>
        public void ZoomAt(double zoom, Point clientAnchor)
        {
            double clamped = Math.Max(_minZoom, Math.Min(_maxZoom, zoom));
            FrameBuffer f = _current;
            if (f == null)
            {
                _zoom = clamped;
                _viewMode = ViewMode.Custom;
                OnZoomChanged();
                return;
            }

            double imageX;
            double imageY;
            Size imageSize = new Size(f.Width, f.Height);
            if (_anchorValid && clientAnchor == _anchorClient && AutoScrollPosition == _anchorScroll &&
                imageSize == _anchorImageSize)
            {
                // 같은 자리에서 휠을 연달아 굴리는 중: 처음 잡은 이미지 지점을 그대로 씁니다.
                // 매번 화면에서 다시 계산하면 스크롤 위치의 반올림 오차가 쌓여 기준점이 조금씩 밀립니다.
                imageX = _anchorImageX;
                imageY = _anchorImageY;
            }
            else
            {
                GetTransform(f).DisplayToImage(clientAnchor.X, clientAnchor.Y, out imageX, out imageY);
                imageX = Math.Max(0.0, Math.Min(f.Width, imageX));
                imageY = Math.Max(0.0, Math.Min(f.Height, imageY));
            }

            _zoom = clamped;
            _viewMode = ViewMode.Custom;
            UpdateScrollArea();     // 스크롤바가 생기거나 사라지면 ClientSize가 바뀌므로 그 뒤에 계산합니다.

            Size client = ClientSize;
            ViewTransform after = ViewTransform.Create(f.Width, f.Height, client.Width, client.Height,
                ViewMode.Custom, _zoom, 0, 0);
            AutoScrollPosition = after.ScrollForAnchor(imageX, imageY, clientAnchor, client.Width, client.Height);

            _anchorValid = true;
            _anchorClient = clientAnchor;
            _anchorImageX = imageX;
            _anchorImageY = imageY;
            _anchorImageSize = imageSize;
            _anchorScroll = AutoScrollPosition;

            Invalidate();
            OnZoomChanged();
        }

        private void OnZoomChanged()
        {
            EventHandler handler = ZoomChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private Point GetClientCenter()
        {
            Size client = ClientSize;
            return new Point(client.Width / 2, client.Height / 2);
        }

        private Point GetZoomAnchor()
        {
            if (IsHandleCreated)
            {
                Point p = PointToClient(Cursor.Position);
                if (ClientRectangle.Contains(p))
                {
                    return p;
                }
            }
            return GetClientCenter();
        }

        private ViewTransform GetTransform(FrameBuffer f)
        {
            Size client = ClientSize;
            Point scroll = AutoScrollPosition;      // 스크롤하면 음수로 나옵니다.
            return ViewTransform.Create(f.Width, f.Height, client.Width, client.Height,
                _viewMode, _zoom, -scroll.X, -scroll.Y);
        }

        /// <summary>확대된 영상 크기를 스크롤 범위로 알려 줍니다. 컨트롤보다 크면 스크롤바가 자동으로 생깁니다.</summary>
        private void UpdateScrollArea()
        {
            FrameBuffer f = _current;
            Size required = Size.Empty;
            if (f != null && (_viewMode == ViewMode.Actual || _viewMode == ViewMode.Custom))
            {
                double zoom = _viewMode == ViewMode.Actual ? 1.0 : _zoom;
                required = new Size(ViewTransform.ScaleSize(f.Width, zoom), ViewTransform.ScaleSize(f.Height, zoom));
            }
            if (AutoScrollMinSize != required)
            {
                AutoScrollMinSize = required;
            }
        }

        private void UpdatePoolLimit()
        {
            int capacity;
            lock (_gate)
            {
                capacity = _displayPolicy == DisplayPolicy.Queue ? _queueCapacity : 1;
            }
            _pool.MaxIdle = capacity + 2;
        }

        // ------------------------------------------------------------------
        // 수명 관리
        // ------------------------------------------------------------------

        /// <inheritdoc/>
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Volatile.Write(ref _hwnd, Handle);
            Invalidate();       // 핸들이 생기기 전에 받아 둔 프레임을 그립니다.
        }

        /// <inheritdoc/>
        protected override void OnHandleDestroyed(EventArgs e)
        {
            Volatile.Write(ref _hwnd, IntPtr.Zero);
            base.OnHandleDestroyed(e);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                FrameBuffer[] waiting;
                System.Threading.Timer timer;
                lock (_gate)
                {
                    _disposed = true;
                    waiting = _queue.ToArray();
                    _queue.Clear();
                    timer = _deferTimer;
                    _deferTimer = null;
                }
                Volatile.Write(ref _hwnd, IntPtr.Zero);
                if (timer != null)
                {
                    timer.Dispose();
                }
                foreach (FrameBuffer buffer in waiting)
                {
                    buffer.Dispose();
                }
                if (_current != null)
                {
                    _current.Dispose();
                    _current = null;
                }
                _pool.Dispose();
                DisposeMenu();
            }
            DisposeRenderResources(disposing);
            base.Dispose(disposing);
        }
    }
}
