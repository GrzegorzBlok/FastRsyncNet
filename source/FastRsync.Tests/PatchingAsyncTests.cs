using System;
using System.IO;
using System.Threading.Tasks;
using FastRsync.Core;
using FastRsync.Delta;
using FastRsync.Diagnostics;
using FastRsync.Hash;
using FastRsync.Signature;
using FastRsync.Tests.OctodiffLegacy;
using NSubstitute;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace FastRsync.Tests;

[TestFixture]
public class PatchingAsyncTests
{
    [Test]
    [TestCase(1378, 129, SignatureBuilder.MinimumChunkSize, "XXH64", "Adler32")]
    [TestCase(1378, 129, SignatureBuilder.DefaultChunkSize, "XXH64", "Adler32")]
    [TestCase(1378, 129, SignatureBuilder.MaximumChunkSize, "XXH64", "Adler32")]
    [TestCase(16974, 8452, SignatureBuilder.MinimumChunkSize, "XXH64", "Adler32")]
    [TestCase(16974, 8452, SignatureBuilder.DefaultChunkSize, "XXH64", "Adler32")]
    [TestCase(16974, 8452, SignatureBuilder.MaximumChunkSize, "XXH64", "Adler32")]
    [TestCase(6666, 6666, SignatureBuilder.MinimumChunkSize, "XXH64", "Adler32")]
    [TestCase(6666, 6666, SignatureBuilder.DefaultChunkSize, "XXH64", "Adler32")]
    [TestCase(6666, 6666, SignatureBuilder.MaximumChunkSize, "XXH64", "Adler32")]
    [TestCase(1378, 129, SignatureBuilder.MinimumChunkSize, "XXH3", "Adler32")]
    [TestCase(1378, 129, SignatureBuilder.DefaultChunkSize, "XXH3", "Adler32")]
    [TestCase(1378, 129, SignatureBuilder.MaximumChunkSize, "XXH3", "Adler32")]
    [TestCase(16974, 8452, SignatureBuilder.MinimumChunkSize, "XXH3", "Adler32")]
    [TestCase(16974, 8452, SignatureBuilder.DefaultChunkSize, "XXH3", "Adler32")]
    [TestCase(16974, 8452, SignatureBuilder.MaximumChunkSize, "XXH3", "Adler32")]
    [TestCase(6666, 6666, SignatureBuilder.MinimumChunkSize, "XXH3", "Adler32")]
    [TestCase(6666, 6666, SignatureBuilder.DefaultChunkSize, "XXH3", "Adler32")]
    [TestCase(6666, 6666, SignatureBuilder.MaximumChunkSize, "XXH3", "Adler32")]
    [TestCase(1378, 129, SignatureBuilder.MinimumChunkSize, "XXH64", "Adler32V2")]
    [TestCase(1378, 129, SignatureBuilder.DefaultChunkSize, "XXH64", "Adler32V2")]
    [TestCase(1378, 129, SignatureBuilder.MaximumChunkSize, "XXH64", "Adler32V2")]
    [TestCase(16974, 8452, SignatureBuilder.MinimumChunkSize, "XXH64", "Adler32V2")]
    [TestCase(16974, 8452, SignatureBuilder.DefaultChunkSize, "XXH64", "Adler32V2")]
    [TestCase(16974, 8452, SignatureBuilder.MaximumChunkSize, "XXH64", "Adler32V2")]
    [TestCase(6666, 6666, SignatureBuilder.MinimumChunkSize, "XXH64", "Adler32V2")]
    [TestCase(6666, 6666, SignatureBuilder.DefaultChunkSize, "XXH64", "Adler32V2")]
    [TestCase(6666, 6666, SignatureBuilder.MaximumChunkSize, "XXH64", "Adler32V2")]
    public async Task PatchingAsyncXXHash_ForNewData_PatchesFile(int baseNumberOfBytes, int newDataNumberOfBytes, short chunkSize, string signatureHashingAlgorithm, string rollingChecksumAlgorithm)
    {
        // Arrange
        var signatureHash = SupportedAlgorithms.Hashing.Create(signatureHashingAlgorithm);
        var rollingChecksum = SupportedAlgorithms.Checksum.Create(rollingChecksumAlgorithm);
        var (baseDataStream, baseSignatureStream, newData, newDataStream) = await PrepareTestDataAsync(baseNumberOfBytes, newDataNumberOfBytes, chunkSize, rollingChecksum, signatureHash).ConfigureAwait(false);

        var progressReporter = Substitute.For<IProgress<ProgressReport>>();

        // Act
        var deltaStream = new MemoryStream();
        var deltaBuilder = new DeltaBuilder();
        await deltaBuilder.BuildDeltaAsync(newDataStream, new SignatureReader(baseSignatureStream, null), new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream))).ConfigureAwait(false);
        deltaStream.Seek(0, SeekOrigin.Begin);

        var patchedDataStream = new MemoryStream();
        var deltaApplier = new DeltaApplier();
        await deltaApplier.ApplyAsync(baseDataStream, new BinaryDeltaReader(deltaStream, progressReporter), patchedDataStream).ConfigureAwait(false);

        // Assert
        CollectionAssert.AreEqual(newData, patchedDataStream.ToArray());
        progressReporter.Received().Report(Arg.Any<ProgressReport>());
    }

    [Test]
    [TestCase(1378, 129, SignatureBuilder.MinimumChunkSize)]
    [TestCase(1378, 129, SignatureBuilder.DefaultChunkSize)]
    [TestCase(1378, 129, SignatureBuilder.MaximumChunkSize)]
    [TestCase(16974, 8452, SignatureBuilder.MinimumChunkSize)]
    [TestCase(16974, 8452, SignatureBuilder.DefaultChunkSize)]
    [TestCase(16974, 8452, SignatureBuilder.MaximumChunkSize)]
    [TestCase(6666, 6666, SignatureBuilder.MinimumChunkSize)]
    [TestCase(6666, 6666, SignatureBuilder.DefaultChunkSize)]
    [TestCase(6666, 6666, SignatureBuilder.MaximumChunkSize)]
    public async Task PatchingAsyncXXHash_ForOctodiffSignature_PatchesFile(int baseNumberOfBytes, int newDataNumberOfBytes, short chunkSize)
    {
        // Arrange
        var (baseDataStream, baseSignatureStream, newData, newDataStream) = PrepareTestDataWithOctodiffSignature(baseNumberOfBytes, newDataNumberOfBytes, chunkSize);

        var progressReporter = Substitute.For<IProgress<ProgressReport>>();

        // Act
        var deltaStream = new MemoryStream();
        var deltaBuilder = new DeltaBuilder();
        await deltaBuilder.BuildDeltaAsync(newDataStream, new SignatureReader(baseSignatureStream, null), new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream))).ConfigureAwait(false);
        deltaStream.Seek(0, SeekOrigin.Begin);

        var patchedDataStream = new MemoryStream();
        var deltaApplier = new DeltaApplier();
        await deltaApplier.ApplyAsync(baseDataStream, new BinaryDeltaReader(deltaStream, progressReporter), patchedDataStream).ConfigureAwait(false);

        // Assert
        CollectionAssert.AreEqual(newData, patchedDataStream.ToArray());
        progressReporter.Received().Report(Arg.Any<ProgressReport>());
    }
        
    [Test]
    [TestCase(1378, 129, SignatureBuilder.MinimumChunkSize)]
    [TestCase(1378, 129, SignatureBuilder.DefaultChunkSize)]
    [TestCase(1378, 129, SignatureBuilder.MaximumChunkSize)]
    [TestCase(16974, 8452, SignatureBuilder.MinimumChunkSize)]
    [TestCase(16974, 8452, SignatureBuilder.DefaultChunkSize)]
    [TestCase(16974, 8452, SignatureBuilder.MaximumChunkSize)]
    [TestCase(6666, 6666, SignatureBuilder.MinimumChunkSize)]
    [TestCase(6666, 6666, SignatureBuilder.DefaultChunkSize)]
    [TestCase(6666, 6666, SignatureBuilder.MaximumChunkSize)]
    public async Task PatchingAsyncXXHash_ForOctodiffSignatureAndPatch_PatchesFile(int baseNumberOfBytes, int newDataNumberOfBytes, short chunkSize)
    {
        // Arrange
        var (baseDataStream, baseSignatureStream, newData, newDataStream) = PrepareTestDataWithOctodiffSignature(baseNumberOfBytes, newDataNumberOfBytes, chunkSize);

        var progressReporter = Substitute.For<IProgress<ProgressReport>>();

        var deltaStream = new MemoryStream();
        var deltaBuilder = new OctodiffDeltaBuilder();
        deltaBuilder.BuildDelta(newDataStream, new OctodiffSignatureReader(baseSignatureStream, null), new OctodiffAggregateCopyOperationsDecorator(new OctodiffBinaryDeltaWriter(deltaStream)));
        deltaStream.Seek(0, SeekOrigin.Begin);

        // Act
        var patchedDataStream = new MemoryStream();
        var deltaApplier = new DeltaApplier();
        await deltaApplier.ApplyAsync(baseDataStream, new BinaryDeltaReader(deltaStream, progressReporter), patchedDataStream).ConfigureAwait(false);

        // Assert
        CollectionAssert.AreEqual(newData, patchedDataStream.ToArray());
        progressReporter.Received().Report(Arg.Any<ProgressReport>());
    }
        
    [Test]
    [TestCase(8467, SignatureBuilder.DefaultChunkSize)]
    public async Task PatchingAsyncXXHash_ForTheSameData_PatchesFile(int numberOfBytes, short chunkSize)
    {
        // Arrange
        var baseData = new byte[numberOfBytes];
        new Random().NextBytes(baseData);
        var baseDataStream = new MemoryStream(baseData);
        var baseSignatureStream = new MemoryStream();

        var signatureBuilder = new SignatureBuilder
        {
            ChunkSize = chunkSize
        };
        signatureBuilder.Build(baseDataStream, new SignatureWriter(baseSignatureStream));
        baseSignatureStream.Seek(0, SeekOrigin.Begin);

        var newDataStream = new MemoryStream(baseData);

        var progressReporter = Substitute.For<IProgress<ProgressReport>>();

        // Act
        var deltaStream = new MemoryStream();
        var deltaBuilder = new DeltaBuilder();
        await deltaBuilder.BuildDeltaAsync(newDataStream, new SignatureReader(baseSignatureStream, null), new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream))).ConfigureAwait(false);
        deltaStream.Seek(0, SeekOrigin.Begin);

        var patchedDataStream = new MemoryStream();
        var deltaApplier = new DeltaApplier();
        await deltaApplier.ApplyAsync(baseDataStream, new BinaryDeltaReader(deltaStream, progressReporter), patchedDataStream).ConfigureAwait(false);

        // Assert
        CollectionAssert.AreEqual(baseData, patchedDataStream.ToArray());
        progressReporter.Received().Report(Arg.Any<ProgressReport>());
    }

    private static async Task<(MemoryStream baseDataStream, MemoryStream baseSignatureStream, byte[] newData, MemoryStream newDataStream)> PrepareTestDataAsync(int baseNumberOfBytes, int newDataNumberOfBytes,
        short chunkSize, Hash.IRollingChecksum rollingChecksumAlg, IHashAlgorithm signatureHashingAlg)
    {
        var baseData = new byte[baseNumberOfBytes];
        new Random().NextBytes(baseData);
        var baseDataStream = new MemoryStream(baseData);
        var baseSignatureStream = new MemoryStream();

        var signatureBuilder = new SignatureBuilder(signatureHashingAlg, rollingChecksumAlg)
        {
            ChunkSize = chunkSize
        };
        await signatureBuilder.BuildAsync(baseDataStream, new SignatureWriter(baseSignatureStream)).ConfigureAwait(false);
        baseSignatureStream.Seek(0, SeekOrigin.Begin);

        var newData = new byte[newDataNumberOfBytes];
        new Random().NextBytes(newData);
        var newDataStream = new MemoryStream(newData);
        return (baseDataStream, baseSignatureStream, newData, newDataStream);
    }        

    private static (MemoryStream baseDataStream, MemoryStream baseSignatureStream, byte[] newData, MemoryStream newDataStream) PrepareTestDataWithOctodiffSignature(int baseNumberOfBytes, int newDataNumberOfBytes,
        short chunkSize)
    {
        var baseData = new byte[baseNumberOfBytes];
        new Random().NextBytes(baseData);
        var baseDataStream = new MemoryStream(baseData);
        var baseSignatureStream = new MemoryStream();

        var signatureBuilder = new OctodiffSignatureBuilder
        {
            ChunkSize = chunkSize
        };
        signatureBuilder.Build(baseDataStream, new OctodiffSignatureWriter(baseSignatureStream));
        baseSignatureStream.Seek(0, SeekOrigin.Begin);

        var newData = new byte[newDataNumberOfBytes];
        new Random().NextBytes(newData);
        var newDataStream = new MemoryStream(newData);
        return (baseDataStream, baseSignatureStream, newData, newDataStream);
    }
}