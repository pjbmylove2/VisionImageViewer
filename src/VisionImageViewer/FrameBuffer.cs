using System;
using System.Runtime.InteropServices;

namespace VisionImageViewer
{
    /// <summary>뷰어 내부 보관 배치. 화면 출력(DIB)에 바로 쓸 수 있는 형태만 둡니다.</summary>
    internal enum StoredFormat
    {
        Gray8,
        Gray16,
        Bgr24,
        Bgra32
    }

    /// <summary>
    /// 뷰어가 소유하는 프레임 한 장입니다. 관리 힙이 아닌 네이티브 메모리를 쓰고,
    /// 크기가 같으면 다시 할당하지 않으므로 고속 갱신에서 GC 부담이 없습니다.
    /// 줄 간격(Stride)은 GDI DIB 규칙(4바이트 배수)에 맞춥니다.
    /// </summary>
    internal sealed unsafe class FrameBuffer : IDisposable
    {
        private IntPtr _memory;
        private long _capacity;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Stride { get; private set; }
        public StoredFormat Format { get; private set; }
        /// <summary>Gray16일 때 유효 비트 수(10/12/16). 그 외에는 8.</summary>
        public int BitDepth { get; private set; }
        public OverlayItem[] Overlays { get; set; }

        public IntPtr Data
        {
            get { return _memory; }
        }

        public int BytesPerPixel
        {
            get { return GetBytesPerPixel(Format); }
        }

        public byte* Row(int y)
        {
            return (byte*)_memory + (long)y * Stride;
        }

        public static int GetBytesPerPixel(StoredFormat format)
        {
            switch (format)
            {
                case StoredFormat.Gray8: return 1;
                case StoredFormat.Gray16: return 2;
                case StoredFormat.Bgr24: return 3;
                default: return 4;
            }
        }

        public static int ComputeStride(int width, StoredFormat format)
        {
            long bits = (long)width * GetBytesPerPixel(format) * 8;
            long stride = ((bits + 31) / 32) * 4;
            if (stride > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }
            return (int)stride;
        }

        /// <summary>지정한 크기를 담을 수 있게 준비합니다. 용량이 충분하면 메모리를 그대로 재사용합니다.</summary>
        public void Prepare(int width, int height, StoredFormat format, int bitDepth)
        {
            int stride = ComputeStride(width, format);
            long required = (long)stride * height;
            if (required > _capacity)
            {
                Free();
                _memory = Marshal.AllocHGlobal(new IntPtr(required));
                _capacity = required;
                GC.AddMemoryPressure(required);
            }
            Width = width;
            Height = height;
            Stride = stride;
            Format = format;
            BitDepth = bitDepth;
        }

        private void Free()
        {
            if (_memory != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_memory);
                GC.RemoveMemoryPressure(_capacity);
                _memory = IntPtr.Zero;
                _capacity = 0;
            }
        }

        public void Dispose()
        {
            Free();
            Overlays = null;
            GC.SuppressFinalize(this);
        }

        ~FrameBuffer()
        {
            Free();
        }
    }
}
