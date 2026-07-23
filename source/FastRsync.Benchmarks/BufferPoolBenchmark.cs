using System;
using System.IO;
using BenchmarkDotNet.Attributes;
using FastRsync.Core;
using FastRsync.Delta;
using FastRsync.Signature;

namespace FastRsync.Benchmarks
{
    // Measures the allocation impact of DeltaBuilder/DeltaApplier buffer pooling, in-memory so
    // there is no network/SDK allocation noise. The wall-clock difference is expected to be
    // negligible; the Allocated column is the metric that matters.
    [MemoryDiagnoser]
    public class BufferPoolBenchmark
    {
        private const int BaseFileSize = 8 * 1024 * 1024;
        private const int NewFileTailSize = 256 * 1024;

        private byte[] baseData;
        private byte[] newData;
        private byte[] signature;
        private byte[] delta;

        [GlobalSetup]
        public void GlobalSetup()
        {
            var rnd = new Random(42);
            baseData = new byte[BaseFileSize];
            rnd.NextBytes(baseData);

            newData = new byte[BaseFileSize + NewFileTailSize];
            Array.Copy(baseData, newData, BaseFileSize);
            rnd.NextBytes(new Span<byte>(newData, BaseFileSize / 2, 256 * 1024));
            rnd.NextBytes(new Span<byte>(newData, BaseFileSize, NewFileTailSize));

            var signatureStream = new MemoryStream();
            new SignatureBuilder().Build(new MemoryStream(baseData), new SignatureWriter(signatureStream));
            signature = signatureStream.ToArray();

            var deltaStream = new MemoryStream();
            new DeltaBuilder().BuildDelta(new MemoryStream(newData), new SignatureReader(new MemoryStream(signature), null),
                new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream)));
            delta = deltaStream.ToArray();
        }

        [Benchmark]
        public long BuildDeltaPooled() => BuildDelta(useBufferPool: true);

        [Benchmark]
        public long BuildDeltaNotPooled() => BuildDelta(useBufferPool: false);

        [Benchmark]
        public long ApplyDeltaPooled() => ApplyDelta(useBufferPool: true);

        [Benchmark]
        public long ApplyDeltaNotPooled() => ApplyDelta(useBufferPool: false);

        private long BuildDelta(bool useBufferPool)
        {
            var deltaStream = new MemoryStream();
            new DeltaBuilder { UseBufferPool = useBufferPool }.BuildDelta(new MemoryStream(newData),
                new SignatureReader(new MemoryStream(signature), null),
                new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream)));
            return deltaStream.Length;
        }

        private long ApplyDelta(bool useBufferPool)
        {
            var output = new MemoryStream();
            new DeltaApplier { UseBufferPool = useBufferPool }.Apply(new MemoryStream(baseData),
                new BinaryDeltaReader(new MemoryStream(delta), null) { UseBufferPool = useBufferPool }, output);
            return output.Length;
        }
    }
}
