using System;
using System.Collections.Generic;

namespace VisionImageViewer
{
    /// <summary>
    /// 프레임 버퍼를 돌려쓰는 풀입니다. 정상 상태에서는 "표시 중 / 대기 / 쓰는 중" 버퍼가
    /// 계속 순환하므로 프레임마다 메모리를 새로 잡지 않습니다.
    /// </summary>
    internal sealed class FramePool : IDisposable
    {
        private readonly Stack<FrameBuffer> _free = new Stack<FrameBuffer>();
        private bool _disposed;
        private int _maxIdle = 3;

        /// <summary>쉬는 버퍼를 최대 몇 개까지 보관할지. 넘치면 즉시 해제합니다.</summary>
        public int MaxIdle
        {
            get { lock (_free) { return _maxIdle; } }
            set { lock (_free) { _maxIdle = Math.Max(1, value); } }
        }

        public FrameBuffer Rent()
        {
            lock (_free)
            {
                if (_free.Count > 0)
                {
                    return _free.Pop();
                }
            }
            return new FrameBuffer();
        }

        public void Return(FrameBuffer buffer)
        {
            if (buffer == null)
            {
                return;
            }
            buffer.Overlays = null;
            lock (_free)
            {
                if (!_disposed && _free.Count < _maxIdle)
                {
                    _free.Push(buffer);
                    return;
                }
            }
            buffer.Dispose();
        }

        public void Dispose()
        {
            FrameBuffer[] items;
            lock (_free)
            {
                _disposed = true;
                items = _free.ToArray();
                _free.Clear();
            }
            foreach (FrameBuffer item in items)
            {
                item.Dispose();
            }
        }
    }
}
