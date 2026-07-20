using System.IO;
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
}
