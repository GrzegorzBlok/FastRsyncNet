using NUnit.Framework;
using System;
using System.IO;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using System.Data.HashFunction.xxHash;
using System.IO.Hashing;

namespace FastRsync.Tests;

[TestFixture]
public class HashTests
{
    [Test]
    [TestCase(120)]
    [TestCase(5 * 1024 * 1024)]
    [TestCase(6 * 1024 * 1024)]
    [TestCase(7 * 1023 * 1017)]
    [TestCase(100 * 333 * 333)]
    public void XxHash64StaticBackwardCompatibility(int dataLength)
    {
        // Arrange
        var data = new byte[dataLength];
        new Random().NextBytes(data);

        // Act
        var dataXxHash = xxHashFactory.Instance.Create(new xxHashConfig
        {
            HashSizeInBits = 64
        });

        var dataXxHash64Dest = dataXxHash.ComputeHash(data).Hash;
        var systemXxHash64Dest = XxHash64.Hash(data);
        Array.Reverse(systemXxHash64Dest);

        // Assert
        Assert.AreEqual(dataXxHash64Dest, systemXxHash64Dest);
    }

    [Test]
    [TestCase(120)]
    [TestCase(5 * 1024 * 1024)]
    [TestCase(6 * 1024 * 1024)]
    [TestCase(7 * 1023 * 1017)]
    [TestCase(100 * 333 * 333)]
    public void XxHash64BackwardCompatibility(int dataLength)
    {
        // Arrange
        var data = new byte[dataLength];
        new Random().NextBytes(data);

        // Act
        var dataXxHash = xxHashFactory.Instance.Create(new xxHashConfig
        {
            HashSizeInBits = 64
        });

        NonCryptographicHashAlgorithm systemXxHash = new XxHash64();

        var dataXxHash64Dest = dataXxHash.ComputeHash(data).Hash;

        systemXxHash.Append(data);
        var systemXxHash64Dest = systemXxHash.GetHashAndReset();
        Array.Reverse(systemXxHash64Dest);

        // Assert
        Assert.AreEqual(dataXxHash64Dest, systemXxHash64Dest);
    }

    [Test]
    [TestCase(120)]
    [TestCase(5 * 1024 * 1024)]
    [TestCase(100 * 333 * 333)]
    public async Task XxHash64StreamBackwardCompatibility(int dataLength)
    {
        // Arrange
        var data = new byte[dataLength];
        new Random().NextBytes(data);
        var streamdata = new MemoryStream(data);

        const string azureBlobStorageConnectionString = "PUT AZURE BLOB STORAGE CONNECTION STRING HERE";

        var azureBlobServiceClient = new BlobServiceClient(azureBlobStorageConnectionString);
        var azureBlobContainerClient = azureBlobServiceClient.GetBlobContainerClient("hashbenchmark");
        var azureBlob = azureBlobContainerClient.GetBlockBlobClient("XxHash64StreamBackwardCompatibility_" + dataLength);
        await azureBlob.UploadAsync(streamdata);

        await using var blobstreamDataXxHash = await azureBlob.OpenReadAsync();
        await using var blobstreamSystemXxHash = await azureBlob.OpenReadAsync();

        // Act
        var dataXxHash = xxHashFactory.Instance.Create(new xxHashConfig
        {
            HashSizeInBits = 64
        });

        NonCryptographicHashAlgorithm systemXxHash = new XxHash64();

        var dataXxHash64Dest = (await dataXxHash.ComputeHashAsync(blobstreamDataXxHash)).Hash;
        
        await systemXxHash.AppendAsync(blobstreamSystemXxHash);
        var systemXxHash64Dest = systemXxHash.GetHashAndReset();
        Array.Reverse(systemXxHash64Dest);

        // Assert
        Assert.AreEqual(dataXxHash64Dest, systemXxHash64Dest);
    }
}

