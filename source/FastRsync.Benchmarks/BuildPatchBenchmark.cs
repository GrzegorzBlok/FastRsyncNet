using System;
using System.IO;
using BenchmarkDotNet.Attributes;
using FastRsync.Core;
using FastRsync.Delta;
using FastRsync.Signature;

namespace FastRsync.Benchmarks
{
    public class BuildPatchBenchmark
    {
        [Params(128, 16974)]
        public int BaseFileSize { get; set; }

        [Params(16974, 128)]
        public int NewFileSize { get; set; }

        private byte[] newFileData;

        private readonly DeltaBuilder deltaBuilder = new();

        private MemoryStream newDataStream;
        private MemoryStream baseSignatureSha1Stream;
        private MemoryStream baseSignatureXxHashStream;
        private MemoryStream baseSignatureXxHash3Stream;
        private MemoryStream baseSignatureMd5Stream;

        [GlobalSetup]
        public void GlobalSetup()
        {
            var baseFileBytes = new byte[BaseFileSize];
            var rnd = new Random();
            rnd.NextBytes(baseFileBytes);

            newFileData = new byte[NewFileSize];
            rnd.NextBytes(newFileData);

            var baseDataStream = new MemoryStream(baseFileBytes);

            newDataStream = new MemoryStream(newFileData);

            {
                var xxHashSignatureBuilder = new SignatureBuilder(SupportedAlgorithms.Hashing.XxHash(),
                    SupportedAlgorithms.Checksum.Adler32Rolling());
                baseSignatureXxHashStream = new MemoryStream();
                xxHashSignatureBuilder.Build(baseDataStream, new SignatureWriter(baseSignatureXxHashStream));
            }

            {
                var xxHash3SignatureBuilder = new SignatureBuilder(SupportedAlgorithms.Hashing.XxHash3(),
                    SupportedAlgorithms.Checksum.Adler32Rolling());
                baseSignatureXxHash3Stream = new MemoryStream();
                xxHash3SignatureBuilder.Build(baseDataStream, new SignatureWriter(baseSignatureXxHashStream));
            }

            {
                var sha1SignatureBuilder = new SignatureBuilder(SupportedAlgorithms.Hashing.Sha1(),
                    SupportedAlgorithms.Checksum.Adler32Rolling());
                baseDataStream.Seek(0, SeekOrigin.Begin);
                baseSignatureSha1Stream = new MemoryStream();
                sha1SignatureBuilder.Build(baseDataStream, new SignatureWriter(baseSignatureSha1Stream));
            }

            {
                var md5SignatureBuilder = new SignatureBuilder(SupportedAlgorithms.Hashing.Md5(),
                    SupportedAlgorithms.Checksum.Adler32Rolling());
                baseDataStream.Seek(0, SeekOrigin.Begin);
                baseSignatureMd5Stream = new MemoryStream();
                md5SignatureBuilder.Build(baseDataStream, new SignatureWriter(baseSignatureMd5Stream));
            }
        }

        [Benchmark]
        public byte[] BuildPatchXxHash()
        {
            newDataStream.Seek(0, SeekOrigin.Begin);
            baseSignatureXxHashStream.Seek(0, SeekOrigin.Begin);
            var deltaStream = new MemoryStream();
            
            deltaBuilder.BuildDelta(newDataStream, new SignatureReader(baseSignatureXxHashStream, null), new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream)));

            return deltaStream.ToArray();
        }

        [Benchmark]
        public byte[] BuildPatchXxHash3()
        {
            newDataStream.Seek(0, SeekOrigin.Begin);
            baseSignatureXxHash3Stream.Seek(0, SeekOrigin.Begin);
            var deltaStream = new MemoryStream();

            deltaBuilder.BuildDelta(newDataStream, new SignatureReader(baseSignatureXxHash3Stream, null), new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream)));

            return deltaStream.ToArray();
        }

        [Benchmark]
        public byte[] BuildPatchSha1()
        {
            newDataStream.Seek(0, SeekOrigin.Begin);
            baseSignatureSha1Stream.Seek(0, SeekOrigin.Begin);
            var deltaStream = new MemoryStream();

            deltaBuilder.BuildDelta(newDataStream, new SignatureReader(baseSignatureSha1Stream, null), new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream)));

            return deltaStream.ToArray();
        }

        [Benchmark]
        public byte[] BuildPatchMd5()
        {
            newDataStream.Seek(0, SeekOrigin.Begin);
            baseSignatureMd5Stream.Seek(0, SeekOrigin.Begin);
            var deltaStream = new MemoryStream();

            deltaBuilder.BuildDelta(newDataStream, new SignatureReader(baseSignatureMd5Stream, null), new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream)));

            return deltaStream.ToArray();
        }
    }
}