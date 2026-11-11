using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VisionImageViewer
{
    public sealed partial class ImageViewer
    {
        private const string OpenPatterns = "*.bmp;*.jpg;*.jpeg;*.png;*.tif;*.tiff;*.gif;*.ico;*.webp;*.jp2;*.pgm;*.ppm;*.pbm";

        /// <summary>
        /// 이 뷰어에서만 쓰는 외부 파일 디코더. null이면 <see cref="DefaultFileDecoder"/>를 쓰고,
        /// 그것도 없으면 GDI+로 엽니다(bmp, jpg, png, gif, 8bit tif, ico).
        /// </summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ImageFileDecoder FileDecoder { get; set; }

        /// <summary>마지막 LoadFile/SaveFile이 실패했을 때의 원인 문구. 성공하면 null.</summary>
        [Browsable(false)]
        public string LastFileError { get; private set; }

        /// <summary>GDI+ Bitmap을 표시합니다. 픽셀을 복사하므로 호출 뒤 Bitmap을 해제해도 됩니다.</summary>
        public void SetImage(Bitmap bitmap, IReadOnlyList<OverlayItem> overlays = null)
        {
            if (bitmap == null)
            {
                throw new ArgumentNullException(nameof(bitmap));
            }
            ImageFileCodec.Submit(this, bitmap, overlays);
        }

        /// <summary>
        /// 이미지 파일을 열어 표시합니다. 한글 경로도 열 수 있습니다.
        /// 실패하면 false를 반환하고 원인은 <see cref="LastFileError"/>에 남습니다.
        /// </summary>
        public bool LoadFile(string path)
        {
            LastFileError = null;
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                ImageFileDecoder decoder = FileDecoder ?? DefaultFileDecoder;
                if (decoder != null && decoder(path, bytes, this))
                {
                    return true;
                }
                ImageFileCodec.DecodeWithGdiPlus(bytes, this);
                return true;
            }
            catch (Exception ex) when (IsFileFailure(ex))
            {
                LastFileError = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 화면에 표시 중인 영상을 파일로 저장합니다(확장자로 형식 결정: png, bmp, jpg, tif, gif).
        /// includeOverlays가 true면 도형과 글자를 함께 그려 저장합니다. 16bit 영상은 8bit로 저장됩니다.
        /// </summary>
        public bool SaveFile(string path, bool includeOverlays)
        {
            LastFileError = null;
            try
            {
                using (Bitmap bitmap = CaptureBitmap(includeOverlays))
                {
                    if (bitmap == null)
                    {
                        LastFileError = _strings.Empty;
                        return false;
                    }
                    bitmap.Save(path, ImageFileCodec.FormatFromPath(path));
                    return true;
                }
            }
            catch (Exception ex) when (IsFileFailure(ex))
            {
                LastFileError = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 화면에 표시 중인 영상을 원본 크기의 새 Bitmap으로 복사합니다(UI 스레드). 영상이 없으면 null.
        /// 받은 쪽에서 Dispose해야 합니다.
        /// </summary>
        public Bitmap CaptureBitmap(bool includeOverlays)
        {
            FrameBuffer frame = _current;
            if (frame == null)
            {
                return null;
            }
            Bitmap raw = ImageFileCodec.ToBitmap(frame);
            OverlayItem[] overlays = frame.Overlays;
            if (!includeOverlays || overlays == null)
            {
                return raw;
            }

            using (raw)
            {
                Rectangle bounds = new Rectangle(0, 0, frame.Width, frame.Height);
                Bitmap canvas = new Bitmap(frame.Width, frame.Height, PixelFormat.Format24bppRgb);
                try
                {
                    ViewTransform identity = ViewTransform.Create(frame.Width, frame.Height,
                        frame.Width, frame.Height, ViewMode.Actual, 1.0, 0, 0);
                    using (Graphics g = Graphics.FromImage(canvas))
                    {
                        g.DrawImage(raw, bounds);
                        foreach (OverlayItem item in overlays)
                        {
                            if (item != null)
                            {
                                item.Draw(g, identity, bounds, _overlayResources);
                            }
                        }
                    }
                    return canvas;
                }
                catch
                {
                    canvas.Dispose();
                    throw;
                }
            }
        }

        /// <summary>파일이 없거나, 권한이 없거나, 해석할 수 없는 형식일 때 나오는 예외만 실패로 처리합니다.</summary>
        private static bool IsFileFailure(Exception ex)
        {
            return ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException ||
                   ex is NotSupportedException || ex is ExternalException ||
                   ex is OutOfMemoryException;     // GDI+는 지원하지 않는 형식에도 이 예외를 던집니다.
        }

        private void OpenWithDialog()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = _strings.ImageFiles + "|" + OpenPatterns + "|" + _strings.AllFiles + "|*.*";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                if (!LoadFile(dialog.FileName))
                {
                    MessageBox.Show(this, _strings.OpenFailed + Environment.NewLine + LastFileError, Text,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void SaveWithDialog(bool includeOverlays)
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "PNG|*.png|BMP|*.bmp|JPEG|*.jpg|TIFF|*.tif";
                dialog.DefaultExt = "png";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                if (!SaveFile(dialog.FileName, includeOverlays))
                {
                    MessageBox.Show(this, _strings.SaveFailed + Environment.NewLine + LastFileError, Text,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }
    }
}
