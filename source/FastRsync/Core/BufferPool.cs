using System;
using System.Buffers;

namespace FastRsync.Core
{
    // Thin helper over ArrayPool that can be toggled off. Pooling avoids repeatedly allocating
    // the large (multi-megabyte) working buffers when many signature/delta operations run in the
    // same process (e.g. a server), at the cost of retaining buffers in the shared pool. When
    // disabled the behavior is identical to plain allocation.
    //
    // Rented buffers are larger than requested and contain arbitrary data, so callers must always
    // work with the requested size and their own byte counts, never buffer.Length.
    internal readonly struct PooledBuffer : IDisposable
    {
        private readonly bool pooled;

        public byte[] Array { get; }

        private PooledBuffer(byte[] array, bool pooled)
        {
            Array = array;
            this.pooled = pooled;
        }

        public static PooledBuffer Rent(int size, bool usePool)
        {
            return usePool
                ? new PooledBuffer(ArrayPool<byte>.Shared.Rent(size), true)
                : new PooledBuffer(new byte[size], false);
        }

        public void Dispose()
        {
            if (pooled && Array != null)
            {
                // clearArray: false - callers never rely on rented buffers being zeroed.
                ArrayPool<byte>.Shared.Return(Array);
            }
        }
    }
}
