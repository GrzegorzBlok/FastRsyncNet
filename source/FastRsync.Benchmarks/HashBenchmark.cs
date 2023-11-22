using System;
using System.Data.HashFunction.xxHash;
using System.IO;
using System.IO.Hashing;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using BenchmarkDotNet.Attributes;

namespace FastRsync.Benchmarks
{
    public class HashBenchmark
    {
        [Params(128, 16974, 356879)]
        public int N { get; set; }

        private byte[] data;
        private MemoryStream streamdata;

        private const string AzureBlobStorageConnectionString = "PUT AZURE BLOB STORAGE CONNECTION STRING HERE";

        private readonly BlobServiceClient azureBlobServiceClient = new(AzureBlobStorageConnectionString);
        private BlobContainerClient azureBlobContainerClient;
        
        private readonly IxxHash dataXxHash = xxHashFactory.Instance.Create(new xxHashConfig
        {
            HashSizeInBits = 64
        });

        private readonly NonCryptographicHashAlgorithm systemXxHash = new XxHash64();

        [GlobalSetup]
        public async Task GlobalSetup()
        {
            data = new byte[N];
            new Random().NextBytes(data);

            var sdata = new byte[N];
            new Random().NextBytes(sdata);
            streamdata = new MemoryStream(sdata);

            azureBlobContainerClient = azureBlobServiceClient.GetBlobContainerClient("hashbenchmark");
            BlockBlobClient azureBlob = azureBlobContainerClient.GetBlockBlobClient("hashdata");
            await azureBlob.UploadAsync(streamdata);
        }

        [Benchmark]
        public byte[] DataXxHash64()
        {
            return dataXxHash.ComputeHash(data).Hash;
        }

        [Benchmark]
        public byte[] DataXxHash64MemoryStream()
        {
            streamdata.Seek(0, SeekOrigin.Begin);
            return dataXxHash.ComputeHash(streamdata).Hash;
        }

        [Benchmark]
        public byte[] DataXxHash64AzureBlobStream()
        {
            var azureBlob = azureBlobContainerClient.GetBlockBlobClient("hashdata");
            using var blobstream = azureBlob.OpenRead();
            return dataXxHash.ComputeHash(blobstream).Hash;
        }

        [Benchmark]
        public byte[] SystemXxHash64()
        {
            return XxHash64.Hash(data);
        }

        [Benchmark]
        public byte[] SystemXxHash64MemoryStream()
        {
            streamdata.Seek(0, SeekOrigin.Begin);
            systemXxHash.Append(streamdata);
            return systemXxHash.GetHashAndReset();
        }

        [Benchmark]
        public byte[] SystemXxHash64AzureBlobStream()
        {
            var azureBlob = azureBlobContainerClient.GetBlockBlobClient("hashdata");
            using var blobstream = azureBlob.OpenRead();

            systemXxHash.Append(blobstream);
            return systemXxHash.GetHashAndReset();
        }

        [Benchmark]
        public byte[] SystemXxHash64Reverse()
        {
            var systemXxHash64Dest = XxHash64.Hash(data);
            Array.Reverse(systemXxHash64Dest);
            return systemXxHash64Dest;
        }

        [Benchmark]
        public byte[] SystemXxHash64AzureBlobStreamReverse()
        {
            var azureBlob = azureBlobContainerClient.GetBlockBlobClient("hashdata");
            using var blobstream = azureBlob.OpenRead();

            systemXxHash.Append(blobstream);
            var systemXxHash64Dest = systemXxHash.GetHashAndReset();
            Array.Reverse(systemXxHash64Dest);
            return systemXxHash64Dest;
        }
    }
}
