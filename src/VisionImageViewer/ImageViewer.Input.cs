using System;
using System.Drawing;
using System.Windows.Forms;

namespace VisionImageViewer
{
    public sealed partial class ImageViewer
    {
        private static readonly int[] ZoomPresets = { 25, 50, 100, 200, 400, 800 };

        private ContextMenuStrip _menu;
        private ToolStripMenuItem _menuFit;
        private ToolStripMenuItem _menuFill;
        private ToolStripMenuItem _menuActual;
        private ToolStripMenuItem _menuZoomIn;
        private ToolStripMenuItem _menuZoomOut;
        private ToolStripMenuItem _menuPreset;
        private ToolStripMenuItem _menuOpen;
        private ToolStripMenuItem _menuSave;
        private ToolStripMenuItem _menuSaveOverlays;
        private ToolStripMenuItem _menuGrid;
        private ToolStripMenuItem _menuCrosshair;

        private bool _panning;
        private Point _panStartMouse;
        private Point _panStartScroll;

        /// <summary>우클릭 메뉴입니다. 호스트가 Items에 자기 항목을 추가할 수 있습니다.</summary>
        [System.ComponentModel.Browsable(false)]
        public ContextMenuStrip ViewMenu
        {
            get { return _menu; }
        }

        private void BuildMenu()
        {
            _menu = new ContextMenuStrip();
            _menuFit = new ToolStripMenuItem(string.Empty, null, (s, e) => ViewMode = ViewMode.Fit);
            _menuFill = new ToolStripMenuItem(string.Empty, null, (s, e) => ViewMode = ViewMode.Fill);
            _menuActual = new ToolStripMenuItem(string.Empty, null, (s, e) => ViewMode = ViewMode.Actual);
            _menuZoomIn = new ToolStripMenuItem(string.Empty, null, (s, e) => ZoomAt(Zoom * _zoomStep, GetClientCenter()));
            _menuZoomOut = new ToolStripMenuItem(string.Empty, null, (s, e) => ZoomAt(Zoom / _zoomStep, GetClientCenter()));
            _menuPreset = new ToolStripMenuItem();
            foreach (int percent in ZoomPresets)
            {
                double zoom = percent / 100.0;
                _menuPreset.DropDownItems.Add(percent + "%", null, (s, e) => ZoomAt(zoom, GetClientCenter()));
            }
            _menuOpen = new ToolStripMenuItem(string.Empty, null, (s, e) => OpenWithDialog());
            _menuSave = new ToolStripMenuItem(string.Empty, null, (s, e) => SaveWithDialog(false));
            _menuSaveOverlays = new ToolStripMenuItem(string.Empty, null, (s, e) => SaveWithDialog(true));
            _menuGrid = new ToolStripMenuItem(string.Empty, null, (s, e) => ShowPixelGrid = !ShowPixelGrid);
            _menuCrosshair = new ToolStripMenuItem(string.Empty, null, (s, e) => ShowCrosshair = !ShowCrosshair);

            _menu.Items.AddRange(new ToolStripItem[]
            {
                _menuFit, _menuFill, _menuActual, new ToolStripSeparator(),
                _menuZoomIn, _menuZoomOut, _menuPreset, new ToolStripSeparator(),
                _menuOpen, _menuSave, _menuSaveOverlays, new ToolStripSeparator(),
                _menuGrid, _menuCrosshair
            });
            _menu.Opening += (s, e) => UpdateMenuState();
            ApplyStrings();
            ContextMenuStrip = _menu;
        }

        private void ApplyStrings()
        {
            if (_menu == null)
            {
                return;
            }
            _menuFit.Text = _strings.Fit;
            _menuFill.Text = _strings.Fill;
            _menuActual.Text = _strings.Actual;
            _menuZoomIn.Text = _strings.ZoomIn;
            _menuZoomOut.Text = _strings.ZoomOut;
            _menuPreset.Text = _strings.ZoomPreset;
            _menuOpen.Text = _strings.Open;
            _menuSave.Text = _strings.Save;
            _menuSaveOverlays.Text = _strings.SaveWithOverlays;
            _menuGrid.Text = _strings.PixelGrid;
            _menuCrosshair.Text = _strings.Crosshair;
        }

        private void UpdateMenuState()
        {
            bool hasImage = _current != null;
            _menuFit.Checked = _viewMode == ViewMode.Fit;
            _menuFill.Checked = _viewMode == ViewMode.Fill;
            _menuActual.Checked = _viewMode == ViewMode.Actual;
            _menuZoomIn.Enabled = hasImage;
            _menuZoomOut.Enabled = hasImage;
            _menuPreset.Enabled = hasImage;
            _menuOpen.Enabled = !_inspectionMode;
            _menuSave.Enabled = hasImage;
            _menuSaveOverlays.Enabled = hasImage;
            _menuGrid.Checked = _showPixelGrid;
            _menuCrosshair.Checked = _showCrosshair;
        }

        private void DisposeMenu()
        {
            if (_menu != null)
            {
                _menu.Dispose();
                _menu = null;
            }
        }

        // ------------------------------------------------------------------
        // 휠 / 키보드 확대
        // ------------------------------------------------------------------

        /// <inheritdoc/>
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            // 기본 동작(세로 스크롤)을 쓰지 않으므로 base를 부르지 않습니다.
            if (_inspectionMode || _current == null)
            {
                return;
            }
            HandledMouseEventArgs handled = e as HandledMouseEventArgs;
            if (handled != null)
            {
                handled.Handled = true;     // 부모 컨트롤까지 스크롤되지 않게 합니다.
            }
            double factor = Math.Pow(_zoomStep, e.Delta / 120.0);
            ZoomAt(Zoom * factor, e.Location);
        }

        /// <inheritdoc/>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (!_inspectionMode && _current != null)
            {
                switch (e.KeyCode)
                {
                    case Keys.Add:
                    case Keys.Oemplus:
                        ZoomIn();
                        e.Handled = true;
                        e.SuppressKeyPress = true;
                        break;
                    case Keys.Subtract:
                    case Keys.OemMinus:
                        ZoomOut();
                        e.Handled = true;
                        e.SuppressKeyPress = true;
                        break;
                }
            }
            base.OnKeyDown(e);
        }

        /// <inheritdoc/>
        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            // +/- 키는 포커스가 있어야 받을 수 있어서 마우스가 올라오면 포커스를 가져옵니다.
            if (FocusOnMouseEnter && !Focused && CanFocus && !IsTypingElsewhere())
            {
                Focus();
            }
        }

        /// <summary>다른 창이 활성 상태이거나 사용자가 글자를 입력 중이면 포커스를 뺏지 않습니다.</summary>
        private bool IsTypingElsewhere()
        {
            Form form = FindForm();
            if (form == null || Form.ActiveForm != form)
            {
                return true;
            }
            Control active = form.ActiveControl;
            ContainerControl container = active as ContainerControl;
            while (container != null && container.ActiveControl != null)
            {
                active = container.ActiveControl;
                container = active as ContainerControl;
            }
            return active is TextBoxBase || active is ComboBox || active is UpDownBase || active is DataGridView;
        }

        // ------------------------------------------------------------------
        // 마우스: 드래그 이동, 픽셀 정보
        // ------------------------------------------------------------------

        /// <inheritdoc/>
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Focused && CanFocus)
            {
                Focus();
            }
            if (e.Button == MouseButtons.Left && PanWithMouseDrag && (HScroll || VScroll))
            {
                _panning = true;
                _panStartMouse = e.Location;
                Point scroll = AutoScrollPosition;
                _panStartScroll = new Point(-scroll.X, -scroll.Y);
                Capture = true;
                Cursor = Cursors.SizeAll;
            }
        }

        /// <inheritdoc/>
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_panning)
            {
                AutoScrollPosition = new Point(
                    _panStartScroll.X - (e.X - _panStartMouse.X),
                    _panStartScroll.Y - (e.Y - _panStartMouse.Y));
                Invalidate();
            }
            RaisePixelHover(e.Location);
        }

        /// <inheritdoc/>
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            EndPan();
        }

        /// <inheritdoc/>
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture)
            {
                EndPan();
            }
        }

        private void EndPan()
        {
            if (_panning)
            {
                _panning = false;
                Capture = false;
                Cursor = Cursors.Default;
            }
        }

        /// <inheritdoc/>
        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            Invalidate();   // 스크롤로 밀린 화면을 재사용하지 않고 전체를 다시 그려 경계가 어긋나지 않게 합니다.
        }

        private void RaisePixelHover(Point location)
        {
            EventHandler<PixelHoverEventArgs> handler = PixelHover;
            FrameBuffer f = _current;
            if (handler == null || f == null)
            {
                return;
            }
            double imageX;
            double imageY;
            GetTransform(f).DisplayToImage(location.X, location.Y, out imageX, out imageY);
            int x = (int)Math.Floor(imageX);
            int y = (int)Math.Floor(imageY);
            PixelValue value;
            bool inside = TryGetPixel(x, y, out value);
            handler(this, new PixelHoverEventArgs(x, y, inside, value, location));
        }

        // ------------------------------------------------------------------
        // 파일 끌어놓기 (AllowDrop = true일 때)
        // ------------------------------------------------------------------

        /// <inheritdoc/>
        protected override void OnDragEnter(DragEventArgs drgevent)
        {
            base.OnDragEnter(drgevent);
            if (!_inspectionMode && drgevent.Data.GetDataPresent(DataFormats.FileDrop))
            {
                drgevent.Effect = DragDropEffects.Copy;
            }
        }

        /// <inheritdoc/>
        protected override void OnDragDrop(DragEventArgs drgevent)
        {
            base.OnDragDrop(drgevent);
            if (_inspectionMode)
            {
                return;
            }
            string[] files = drgevent.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Length > 0)
            {
                LoadFile(files[0]);
            }
        }
    }
}
