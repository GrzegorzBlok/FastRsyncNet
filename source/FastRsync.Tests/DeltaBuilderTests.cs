using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FastRsync.Core;
using FastRsync.Delta;
using FastRsync.Signature;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace FastRsync.Tests;

[TestFixture]
public class DeltaBuilderTests
{
    [Test]
    [TestCase(1378, SignatureBuilder.MinimumChunkSize)]
    [TestCase(16974, SignatureBuilder.DefaultChunkSize)]
    [TestCase(6666, SignatureBuilder.MaximumChunkSize)]
    public void BuildDelta_SkipDeltaIfHashesMatch_UnchangedFile_SkipsScanAndProducesCopyOnlyDelta(int numberOfBytes, short chunkSize)
    {
        // Arrange - the "new" file is byte-identical to the basis file
        var (baseDataStream, baseSignatureStream, _, _) = Utils.PrepareTestData(numberOfBytes, 0, chunkSize);
        var newDataStream = new MemoryStream(baseDataStream.ToArray());

        // Act
        var deltaStream = new MemoryStream();
        var deltaBuilder = new DeltaBuilder { SkipDeltaIfHashesMatch = true };
        deltaBuilder.BuildDelta(newDataStream, new SignatureReader(baseSignatureStream, null), new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream)));

        // Assert - the new file stream was hashed (and rewound) but never scanned
        Assert.That(newDataStream.Position, Is.EqualTo(0));
        // the delta holds only metadata and a single copy command, no data commands
        Assert.That(deltaStream.Length, Is.LessThan(300));

        // the delta still patches correctly
        deltaStream.Seek(0, SeekOrigin.Begin);
        var patchedDataStream = new MemoryStream();
        new DeltaApplier().Apply(baseDataStream, new BinaryDeltaReader(deltaStream, null), patchedDataStream);
        CollectionAssert.AreEqual(baseDataStream.ToArray(), patchedDataStream.ToArray());
    }

    [Test]
    [TestCase(1378, SignatureBuilder.MinimumChunkSize)]
    [TestCase(16974, SignatureBuilder.DefaultChunkSize)]
    [TestCase(6666, SignatureBuilder.MaximumChunkSize)]
    public async Task BuildDeltaAsync_SkipDeltaIfHashesMatch_UnchangedFile_SkipsScanAndProducesCopyOnlyDelta(int numberOfBytes, short chunkSize)
    {
        // Arrange
        var (baseDataStream, baseSignatureStream, _, _) = Utils.PrepareTestData(numberOfBytes, 0, chunkSize);
        var newDataStream = new MemoryStream(baseDataStream.ToArray());

        // Act
        var deltaStream = new MemoryStream();
        var deltaBuilder = new DeltaBuilder { SkipDeltaIfHashesMatch = true };
        await deltaBuilder.BuildDeltaAsync(newDataStream, new SignatureReader(baseSignatureStream, null), new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream)));

        // Assert
        Assert.That(newDataStream.Position, Is.EqualTo(0));
        Assert.That(deltaStream.Length, Is.LessThan(300));

        deltaStream.Seek(0, SeekOrigin.Begin);
        var patchedDataStream = new MemoryStream();
        await new DeltaApplier().ApplyAsync(baseDataStream, new BinaryDeltaReader(deltaStream, null), patchedDataStream);
        CollectionAssert.AreEqual(baseDataStream.ToArray(), patchedDataStream.ToArray());
    }

    [Test]
    public void BuildDelta_SkipDeltaIfHashesMatch_ChangedFile_FallsBackToFullScan()
    {
        // Arrange - the "new" file differs from the basis file, so the hashes do not match
        var (baseDataStream, baseSignatureStream, newData, newDataStream) = Utils.PrepareTestData(16974, 8452, SignatureBuilder.DefaultChunkSize);

        // Act
        var deltaStream = new MemoryStream();
        var deltaBuilder = new DeltaBuilder { SkipDeltaIfHashesMatch = true };
        deltaBuilder.BuildDelta(newDataStream, new SignatureReader(baseSignatureStream, null), new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream)));
        deltaStream.Seek(0, SeekOrigin.Begin);

        // Assert - normal delta is produced and patches correctly
        var patchedDataStream = new MemoryStream();
        new DeltaApplier().Apply(baseDataStream, new BinaryDeltaReader(deltaStream, null), patchedDataStream);
        CollectionAssert.AreEqual(newData, patchedDataStream.ToArray());
    }

    [Test]
    public void BuildDelta_SkipDeltaIfHashesMatch_EmptyFile_ProducesValidEmptyDelta()
    {
        // Arrange - zero-length basis and new file
        var (baseDataStream, baseSignatureStream, _, _) = Utils.PrepareTestData(0, 0, SignatureBuilder.DefaultChunkSize);
        var newDataStream = new MemoryStream();

        // Act
        var deltaStream = new MemoryStream();
        var deltaBuilder = new DeltaBuilder { SkipDeltaIfHashesMatch = true };
        deltaBuilder.BuildDelta(newDataStream, new SignatureReader(baseSignatureStream, null), new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream)));
        deltaStream.Seek(0, SeekOrigin.Begin);

        // Assert
        var patchedDataStream = new MemoryStream();
        new DeltaApplier().Apply(baseDataStream, new BinaryDeltaReader(deltaStream, null), patchedDataStream);
        Assert.That(patchedDataStream.Length, Is.EqualTo(0));
    }

    [Test]
    public void DeltaBuilder_ReadBufferSmallerThanMaximumChunkSize_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DeltaBuilder(16 * 1024));
    }

    [Test]
    public async Task BuildDeltaAsync_WithReusedSignatureReaderAfterExternalSeek_BuildsValidDelta()
    {
        // Regression test mirroring a real-world handler: read the signature metadata for
        // validation, seek the signature stream back to the start, then hand the same reader
        // instance to BuildDeltaAsync.
        var (baseDataStream, baseSignatureStream, newData, newDataStream) = Utils.PrepareTestData(16974, 8452, SignatureBuilder.DefaultChunkSize);

        var signatureReader = new SignatureReader(baseSignatureStream, null);
        var metadata = signatureReader.ReadSignatureMetadata();
        Assert.That(metadata.Metadata.BaseFileHash, Is.Not.Null.And.Not.Empty);

        baseSignatureStream.Seek(0, SeekOrigin.Begin);

        var deltaStream = new MemoryStream();
        await new DeltaBuilder().BuildDeltaAsync(newDataStream, signatureReader, new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream)));
        deltaStream.Seek(0, SeekOrigin.Begin);

        var patchedDataStream = new MemoryStream();
        await new DeltaApplier().ApplyAsync(baseDataStream, new BinaryDeltaReader(deltaStream, null), patchedDataStream);
        CollectionAssert.AreEqual(newData, patchedDataStream.ToArray());
    }

    [Test]
    [TestCase(16974, 8452)]
    [TestCase(6666, 6666)]
    public void BuildDelta_WithAndWithoutBufferPool_ProduceIdenticalDelta(int baseNumberOfBytes, int newDataNumberOfBytes)
    {
        // Arrange - identical inputs, one with pooling on (default), one off.
        var (_, sigA, newData, newStreamA) = Utils.PrepareTestData(baseNumberOfBytes, newDataNumberOfBytes, SignatureBuilder.DefaultChunkSize);
        var newStreamB = new MemoryStream(newData);
        var sigB = new MemoryStream(sigA.ToArray());

        // Act
        var pooled = new MemoryStream();
        new DeltaBuilder { UseBufferPool = true }.BuildDelta(newStreamA, new SignatureReader(sigA, null), new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(pooled)));

        var notPooled = new MemoryStream();
        new DeltaBuilder { UseBufferPool = false }.BuildDelta(newStreamB, new SignatureReader(sigB, null), new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(notPooled)));

        // Assert - pooling is an allocation strategy only; the delta bytes must be identical.
        CollectionAssert.AreEqual(notPooled.ToArray(), pooled.ToArray());
    }

    [Test]
    public void BuildDelta_WritesBaseAndTargetFileLengthMetadata()
    {
        // Arrange
        const int baseNumberOfBytes = 16974;
        const int newDataNumberOfBytes = 8452;
        var (_, baseSignatureStream, _, newDataStream) = Utils.PrepareTestData(baseNumberOfBytes, newDataNumberOfBytes, SignatureBuilder.DefaultChunkSize);

        // Act
        var deltaStream = new MemoryStream();
        var deltaBuilder = new DeltaBuilder();
        deltaBuilder.BuildDelta(newDataStream, new SignatureReader(baseSignatureStream, null), new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream)));
        deltaStream.Seek(0, SeekOrigin.Begin);

        // Assert
        var reader = new BinaryDeltaReader(deltaStream, null);
        Assert.That(reader.Metadata.BaseFileLength, Is.EqualTo(baseNumberOfBytes));
        Assert.That(reader.Metadata.TargetFileLength, Is.EqualTo(newDataNumberOfBytes));
    }

    [Test]
    public void BuildDelta_SkipDeltaIfHashesMatchDisabled_UnchangedFile_ScansStream()
    {
        // Arrange - flag off (default): behavior is unchanged, the stream is scanned
        var (baseDataStream, baseSignatureStream, _, _) = Utils.PrepareTestData(16974, 0, SignatureBuilder.DefaultChunkSize);
        var newDataStream = new MemoryStream(baseDataStream.ToArray());

        // Act
        var deltaStream = new MemoryStream();
        var deltaBuilder = new DeltaBuilder();
        deltaBuilder.BuildDelta(newDataStream, new SignatureReader(baseSignatureStream, null), new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream)));
        deltaStream.Seek(0, SeekOrigin.Begin);

        // Assert - the stream was read by the scan loop
        Assert.That(newDataStream.Position, Is.GreaterThan(0));

        var patchedDataStream = new MemoryStream();
        new DeltaApplier().Apply(baseDataStream, new BinaryDeltaReader(deltaStream, null), patchedDataStream);
        CollectionAssert.AreEqual(baseDataStream.ToArray(), patchedDataStream.ToArray());
    }

    [Test]
    [TestCase(1000)]
    [TestCase(4096)]
    [TestCase(20000)]
    public void BuildDelta_NewFileStreamReturnsShortReads_ProducesSameDeltaAsFullReads(int maxBytesPerRead)
    {
        // Arrange - the read buffer is much smaller than the file, so it is refilled several
        // times, and every read of the new file returns less than the buffer size.
        var (baseDataStream, baseSignatureStream, newData) = PrepareSlightlyChangedFile();

        var expectedDeltaStream = new MemoryStream();
        new DeltaBuilder(ShortReadsReadBufferSize).BuildDelta(new MemoryStream(newData), new SignatureReader(new MemoryStream(baseSignatureStream.ToArray()), null), new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(expectedDeltaStream)));

        // Act
        var deltaStream = new MemoryStream();
        new DeltaBuilder(ShortReadsReadBufferSize).BuildDelta(new ShortReadStream(newData, maxBytesPerRead), new SignatureReader(baseSignatureStream, null), new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream)));

        // Assert - a short read must not be taken for the end of the file
        CollectionAssert.AreEqual(expectedDeltaStream.ToArray(), deltaStream.ToArray());
        Assert.That(deltaStream.Length, Is.LessThan(newData.Length / 2));

        deltaStream.Seek(0, SeekOrigin.Begin);
        var patchedDataStream = new MemoryStream();
        new DeltaApplier().Apply(baseDataStream, new BinaryDeltaReader(deltaStream, null), patchedDataStream);
        CollectionAssert.AreEqual(newData, patchedDataStream.ToArray());
    }

    [Test]
    [TestCase(1000)]
    [TestCase(4096)]
    [TestCase(20000)]
    public async Task BuildDeltaAsync_NewFileStreamReturnsShortReads_ProducesSameDeltaAsFullReads(int maxBytesPerRead)
    {
        // Arrange
        var (baseDataStream, baseSignatureStream, newData) = PrepareSlightlyChangedFile();

        var expectedDeltaStream = new MemoryStream();
        await new DeltaBuilder(ShortReadsReadBufferSize).BuildDeltaAsync(new MemoryStream(newData), new SignatureReader(new MemoryStream(baseSignatureStream.ToArray()), null), new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(expectedDeltaStream)));

        // Act
        var deltaStream = new MemoryStream();
        await new DeltaBuilder(ShortReadsReadBufferSize).BuildDeltaAsync(new ShortReadStream(newData, maxBytesPerRead), new SignatureReader(baseSignatureStream, null), new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream)));

        // Assert
        CollectionAssert.AreEqual(expectedDeltaStream.ToArray(), deltaStream.ToArray());
        Assert.That(deltaStream.Length, Is.LessThan(newData.Length / 2));

        deltaStream.Seek(0, SeekOrigin.Begin);
        var patchedDataStream = new MemoryStream();
        await new DeltaApplier().ApplyAsync(baseDataStream, new BinaryDeltaReader(deltaStream, null), patchedDataStream);
        CollectionAssert.AreEqual(newData, patchedDataStream.ToArray());
    }

    private const int ShortReadsReadBufferSize = 32 * 1024;

    // The new file is the basis file with a small block in the middle replaced, so a correct
    // delta consists almost entirely of copy commands.
    private static (MemoryStream baseDataStream, MemoryStream baseSignatureStream, byte[] newData) PrepareSlightlyChangedFile()
    {
        var (baseDataStream, baseSignatureStream, _, _) = Utils.PrepareTestData(200000, 0, SignatureBuilder.DefaultChunkSize);

        var newData = baseDataStream.ToArray();
        var changedBlock = new byte[500];
        new Random().NextBytes(changedBlock);
        Array.Copy(changedBlock, 0, newData, newData.Length / 2, changedBlock.Length);

        return (baseDataStream, baseSignatureStream, newData);
    }

    // Seekable stream that never returns more than maxBytesPerRead bytes from a single read,
    // as network-backed streams commonly do.
    private class ShortReadStream : MemoryStream
    {
        private readonly int maxBytesPerRead;

        public ShortReadStream(byte[] data, int maxBytesPerRead) : base(data)
        {
            this.maxBytesPerRead = maxBytesPerRead;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            base.Read(buffer, offset, Math.Min(count, maxBytesPerRead));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            base.ReadAsync(buffer, offset, Math.Min(count, maxBytesPerRead), cancellationToken);
    }
}
