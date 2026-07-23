using System;
using System.IO;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using BenchmarkDotNet.Attributes;
using FastRsync.Core;
using FastRsync.Delta;
using FastRsync.Signature;

namespace FastRsync.Benchmarks
{
    // Benchmarks FastRsync operations reading directly from Azure Blob streams, the common
    // networked usage of the library. Targets local Azurite by default; set the
    // FASTRSYNC_BENCH_STORAGE environment variable to a connection string to run against a
    // real storage account.
    [MemoryDiagnoser]
    public class AzureBlobBenchmark
    {
        private const string ContainerName = "fastrsync-benchmark";
        private const int BaseFileSize = 8 * 1024 * 1024;
        private const int NewFileTailSize = 128 * 1024;

        // SDK-side download buffer of BlobOpenReadOptions; 4 MB is the SDK default.
        private const int BlobReadBufferSize = 4 * 1024 * 1024;

        private BlockBlobClient baseBlob;
        private BlockBlobClient signatureBlob;
        private BlockBlobClient deltaBlob;

        [GlobalSetup]
        public void GlobalSetup()
        {
            var connectionString = Environment.GetEnvironmentVariable("FASTRSYNC_BENCH_STORAGE") ?? "UseDevelopmentStorage=true";
            var container = new BlobServiceClient(connectionString).GetBlobContainerClient(ContainerName);
            container.CreateIfNotExists();

            var rnd = new Random(42);
            var baseData = new byte[BaseFileSize];
            rnd.NextBytes(baseData);

            // The new file shares most content with the base file: one modified region in the
            // middle and additional data at the end, so the delta contains both copy and data commands.
            var newFileData = new byte[BaseFileSize + NewFileTailSize];
            Array.Copy(baseData, newFileData, BaseFileSize);
            rnd.NextBytes(new Span<byte>(newFileData, BaseFileSize / 2, 256 * 1024));
            rnd.NextBytes(new Span<byte>(newFileData, BaseFileSize, NewFileTailSize));

            var signatureStream = new MemoryStream();
            new SignatureBuilder().Build(new MemoryStream(baseData), new SignatureWriter(signatureStream));

            var deltaStream = new MemoryStream();
            signatureStream.Seek(0, SeekOrigin.Begin);
            new DeltaBuilder().BuildDelta(new MemoryStream(newFileData), new SignatureReader(signatureStream, null),
                new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream)));

            baseBlob = container.GetBlockBlobClient("base.bin");
            baseBlob.Upload(new MemoryStream(baseData));

            signatureBlob = container.GetBlockBlobClient("base.sig");
            signatureStream.Seek(0, SeekOrigin.Begin);
            signatureBlob.Upload(signatureStream);

            deltaBlob = container.GetBlockBlobClient("new.delta");
            deltaStream.Seek(0, SeekOrigin.Begin);
            deltaBlob.Upload(deltaStream);
        }

        private Stream OpenRead(BlockBlobClient blob)
        {
            return blob.OpenRead(new BlobOpenReadOptions(false) { BufferSize = BlobReadBufferSize });
        }

        // Single-pass reads the base blob once (hash computed incrementally while chunking);
        // two-pass is the historical behavior that downloads the blob twice.
        [Benchmark]
        public long BuildSignatureFromBlobSinglePass()
        {
            using (var baseStream = OpenRead(baseBlob))
            {
                var output = new MemoryStream();
                new SignatureBuilder { SinglePassBuild = true }.Build(baseStream, new SignatureWriter(output));
                return output.Length;
            }
        }

        [Benchmark]
        public long BuildSignatureFromBlobTwoPass()
        {
            using (var baseStream = OpenRead(baseBlob))
            {
                var output = new MemoryStream();
                new SignatureBuilder { SinglePassBuild = false }.Build(baseStream, new SignatureWriter(output));
                return output.Length;
            }
        }

        [Benchmark]
        public int ReadSignatureFromBlob()
        {
            using (var signatureStream = OpenRead(signatureBlob))
            {
                return new SignatureReader(signatureStream, null).ReadSignature().Chunks.Count;
            }
        }

        [Benchmark]
        public long ApplyDeltaFromBlob()
        {
            using (var basisStream = OpenRead(baseBlob))
            using (var deltaStream = OpenRead(deltaBlob))
            {
                var output = new MemoryStream();
                new DeltaApplier().Apply(basisStream, new BinaryDeltaReader(deltaStream, null), output);
                return output.Length;
            }
        }

        [Benchmark]
        public long ApplyDeltaFromBlobPooled()
        {
            using (var basisStream = OpenRead(baseBlob))
            using (var deltaStream = OpenRead(deltaBlob))
            {
                var output = new MemoryStream();
                new DeltaApplier { UseBufferPool = true }
                    .Apply(basisStream, new BinaryDeltaReader(deltaStream, null) { UseBufferPool = true }, output);
                return output.Length;
            }
        }

        [Benchmark]
        public long ApplyDeltaFromBlobNotPooled()
        {
            using (var basisStream = OpenRead(baseBlob))
            using (var deltaStream = OpenRead(deltaBlob))
            {
                var output = new MemoryStream();
                new DeltaApplier { UseBufferPool = false }
                    .Apply(basisStream, new BinaryDeltaReader(deltaStream, null) { UseBufferPool = false }, output);
                return output.Length;
            }
        }
    }
}
