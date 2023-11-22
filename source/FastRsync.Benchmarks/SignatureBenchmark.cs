using System;
using System.IO;
using BenchmarkDotNet.Attributes;
using FastRsync.Core;
using FastRsync.Signature;
using FastRsync.Tests.OctodiffLegacy;

namespace FastRsync.Benchmarks
{
    public class SignatureBenchmark
    {
        [Params(128, 16974, 356879)]
        public int BaseFileSize { get; set; }

        [Params(SignatureBuilder.MinimumChunkSize, SignatureBuilder.DefaultChunkSize, SignatureBuilder.MaximumChunkSize)]
        public short ChunkSize { get; set; }

        private byte[] data;

        private readonly SignatureBuilder xxHashSignatureBuilder =
            new(SupportedAlgorithms.Hashing.XxHash(), SupportedAlgorithms.Checksum.Adler32Rolling());
        private readonly SignatureBuilder xxHash3SignatureBuilder =
            new(SupportedAlgorithms.Hashing.XxHash3(), SupportedAlgorithms.Checksum.Adler32Rolling());
        private readonly SignatureBuilder xxHashAdler32V2SignatureBuilder =
            new(SupportedAlgorithms.Hashing.XxHash(), SupportedAlgorithms.Checksum.Adler32RollingV2());
        private readonly SignatureBuilder sha1SignatureBuilder =
            new(SupportedAlgorithms.Hashing.Sha1(), SupportedAlgorithms.Checksum.Adler32Rolling());
        private readonly SignatureBuilder md5SignatureBuilder =
            new(SupportedAlgorithms.Hashing.Md5(), SupportedAlgorithms.Checksum.Adler32Rolling());
        private readonly OctodiffSignatureBuilder xxHashOctodiffSignatureBuilder =
            new(SupportedAlgorithms.Hashing.XxHash(), SupportedAlgorithms.Checksum.Adler32Rolling());

        private MemoryStream dataStream;

        [GlobalSetup]
        public void GlobalSetup()
        {
            data = new byte[BaseFileSize];
            new Random().NextBytes(data);

            xxHashSignatureBuilder.ChunkSize = ChunkSize;
            xxHashAdler32V2SignatureBuilder.ChunkSize = ChunkSize;
            sha1SignatureBuilder.ChunkSize = ChunkSize;
            md5SignatureBuilder.ChunkSize = ChunkSize;

            dataStream = new MemoryStream(data);
        }

        [Benchmark]
        public byte[] SignaturexxHash()
        {
            dataStream.Seek(0, SeekOrigin.Begin);
            var signatureStream = new MemoryStream();
            xxHashSignatureBuilder.Build(dataStream, new SignatureWriter(signatureStream));
            return signatureStream.ToArray();
        }

        [Benchmark]
        public byte[] SignaturexxHashAdler32V2()
        {
            dataStream.Seek(0, SeekOrigin.Begin);
            var signatureStream = new MemoryStream();
            xxHashAdler32V2SignatureBuilder.Build(dataStream, new SignatureWriter(signatureStream));
            return signatureStream.ToArray();
        }

        [Benchmark]
        public byte[] SignaturexxHash3()
        {
            dataStream.Seek(0, SeekOrigin.Begin);
            var signatureStream = new MemoryStream();
            xxHash3SignatureBuilder.Build(dataStream, new SignatureWriter(signatureStream));
            return signatureStream.ToArray();
        }

        [Benchmark]
        public byte[] SignatureSha1()
        {
            dataStream.Seek(0, SeekOrigin.Begin);
            var signatureStream = new MemoryStream();
            sha1SignatureBuilder.Build(dataStream, new SignatureWriter(signatureStream));
            return signatureStream.ToArray();
        }

        [Benchmark]
        public byte[] SignatureMd5()
        {
            dataStream.Seek(0, SeekOrigin.Begin);
            var signatureStream = new MemoryStream();
            md5SignatureBuilder.Build(dataStream, new SignatureWriter(signatureStream));
            return signatureStream.ToArray();
        }

        [Benchmark]
        public byte[] OctodiffSignaturexxHash()
        {
            dataStream.Seek(0, SeekOrigin.Begin);
            var signatureStream = new MemoryStream();
            xxHashOctodiffSignatureBuilder.Build(dataStream, new OctodiffSignatureWriter(signatureStream));
            return signatureStream.ToArray();
        }
    }
}