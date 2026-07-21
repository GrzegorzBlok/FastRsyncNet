using System;
using System.IO;
using System.Text;
using FastRsync.Core;
using FastRsync.Delta;
using FastRsync.Signature;
using FastRsync.Tests.FastRsyncLegacy;
using NUnit.Framework;

namespace FastRsync.Tests;

[TestFixture]
public class DeltaReaderTests
{
    /// <summary>
    /// Metadata without BaseFileHashAlgorithm and BaseFileHash fields
    /// </summary>
    private static readonly byte[] FastRsyncLegacyMetadataDelta =
    {
        0x46, 0x52, 0x53, 0x4e, 0x43, 0x44, 0x4c, 0x54, 0x41, 0x01, 0x69, 0x7b, 0x22, 0x68, 0x61, 0x73,
        0x68, 0x41, 0x6c, 0x67, 0x6f, 0x72, 0x69, 0x74, 0x68, 0x6d, 0x22, 0x3a, 0x22, 0x58, 0x58, 0x48,
        0x36, 0x34, 0x22, 0x2c, 0x22, 0x65, 0x78, 0x70, 0x65, 0x63, 0x74, 0x65, 0x64, 0x46, 0x69, 0x6c,
        0x65, 0x48, 0x61, 0x73, 0x68, 0x41, 0x6c, 0x67, 0x6f, 0x72, 0x69, 0x74, 0x68, 0x6d, 0x22, 0x3a,
        0x22, 0x4d, 0x44, 0x35, 0x22, 0x2c, 0x22, 0x65, 0x78, 0x70, 0x65, 0x63, 0x74, 0x65, 0x64, 0x46,
        0x69, 0x6c, 0x65, 0x48, 0x61, 0x73, 0x68, 0x22, 0x3a, 0x22, 0x4e, 0x33, 0x48, 0x65, 0x65, 0x51,
        0x62, 0x48, 0x52, 0x5a, 0x62, 0x65, 0x53, 0x47, 0x35, 0x4c, 0x4c, 0x50, 0x39, 0x46, 0x2f, 0x41,
        0x3d, 0x3d, 0x22, 0x7d, 0x80, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x61, 0x65, 0x63,
        0x64
    };

    private static readonly string FastRsyncLegacyDeltaExpectedFileHash = "N3HeeQbHRZbeSG5LLP9F/A==";

    [Test]
    public void BinaryDeltaReader_ReadsLegacyDelta()
    {
        // Arrange
        var deltaStream = new MemoryStream(FastRsyncLegacyMetadataDelta);

        // Act
        IDeltaReader target = new BinaryDeltaReader(deltaStream, null);

        // Assert
        var hashAlgorithm = SupportedAlgorithms.Hashing.Default();
        Assert.That(target.Type, Is.EqualTo(RsyncFormatType.FastRsync));
        Assert.That(target.HashAlgorithm.Name, Is.EqualTo(hashAlgorithm.Name));
        Assert.That(target.HashAlgorithm.HashLengthInBytes, Is.EqualTo(hashAlgorithm.HashLengthInBytes));
        Assert.That(target.Metadata.ExpectedFileHash, Is.EqualTo(FastRsyncLegacyDeltaExpectedFileHash));
        Assert.That(target.Metadata.ExpectedFileHashAlgorithm, Is.EqualTo("MD5"));
        Assert.That(target.Metadata.HashAlgorithm, Is.EqualTo(hashAlgorithm.Name));
        Assert.That(target.Metadata.BaseFileHash, Is.Null);
        Assert.That(target.Metadata.BaseFileHashAlgorithm, Is.Null);
        // Metadata written by older versions carries no file lengths
        Assert.That(target.Metadata.BaseFileLength, Is.Null);
        Assert.That(target.Metadata.TargetFileLength, Is.Null);
    }

    [Test]
    public void LegacyBinaryDeltaReader_ReadsDelta()
    {
        // Arrange
        var (_, baseSignatureStream, _, newDataStream) = Utils.PrepareTestData(16974, 8452, SignatureBuilder.DefaultChunkSize);

        var deltaStream = new MemoryStream();
        var deltaBuilder = new DeltaBuilder();
        deltaBuilder.BuildDelta(newDataStream, new SignatureReader(baseSignatureStream, null), new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream)));
        deltaStream.Seek(0, SeekOrigin.Begin);

        // Act
        var target = new BinaryDeltaReaderLegacy(deltaStream, null);

        // Assert
        var hashAlgorithm = SupportedAlgorithms.Hashing.Default();
        Assert.That(target.HashAlgorithm.Name, Is.EqualTo(hashAlgorithm.Name));
        Assert.That(target.HashAlgorithm.HashLengthInBytes, Is.EqualTo(hashAlgorithm.HashLengthInBytes));
        Assert.That(target.Type, Is.EqualTo(RsyncFormatType.FastRsync));
        Assert.That(target.Metadata.ExpectedFileHash, Is.Not.Null.And.Not.Empty);
        Assert.That(target.Metadata.ExpectedFileHashAlgorithm, Is.EqualTo("MD5"));
        Assert.That(target.Metadata.HashAlgorithm, Is.EqualTo(hashAlgorithm.Name));
    }

    [Test]
    public void BinaryDeltaReader_FastRsyncDeltaWithInvalidBase64Hash_ThrowsInvalidDataException()
    {
        // Arrange
        var deltaStream = CreateFastRsyncDeltaStream("!!!not-valid-base64!!!");
        var target = new BinaryDeltaReader(deltaStream, null);

        // Act & Assert
        Assert.Throws<InvalidDataException>(() => _ = target.Metadata);
    }

    [Test]
    public void BinaryDeltaReader_Apply_UnknownCommandByte_ThrowsInvalidDataException()
    {
        // Arrange - valid header followed by a command byte that is neither Copy nor Data.
        // Before validation was added, unknown commands were silently ignored, allowing
        // a corrupt or misaligned delta stream to be mis-parsed.
        var deltaStream = CreateFastRsyncDeltaStream(EmptyMd5Base64, bw => bw.Write((byte)0x42));
        var target = new BinaryDeltaReader(deltaStream, null);

        // Act & Assert
        Assert.Throws<InvalidDataException>(() => target.Apply(_ => { }, (_, _) => { }));
    }

    [Test]
    public void BinaryDeltaReader_Apply_CopyCommandWithNegativeValues_ThrowsInvalidDataException()
    {
        // Arrange - copy command with negative offset and length
        var deltaStream = CreateFastRsyncDeltaStream(EmptyMd5Base64, bw =>
        {
            bw.Write((byte)0x60); // BinaryFormat.CopyCommand
            bw.Write(-1L);
            bw.Write(-1L);
        });
        var target = new BinaryDeltaReader(deltaStream, null);

        // Act & Assert
        Assert.Throws<InvalidDataException>(() => target.Apply(_ => { }, (_, _) => { }));
    }

    [Test]
    public void BinaryDeltaReader_Apply_TruncatedDataCommand_ThrowsInvalidDataException()
    {
        // Arrange - data command declaring 100 bytes but providing only 5.
        // Before validation was added, this spun forever in the read loop.
        var deltaStream = CreateFastRsyncDeltaStream(EmptyMd5Base64, bw =>
        {
            bw.Write((byte)0x80); // BinaryFormat.DataCommand
            bw.Write(100L);
            bw.Write(new byte[] { 1, 2, 3, 4, 5 });
        });
        var target = new BinaryDeltaReader(deltaStream, null);

        // Act & Assert
        Assert.Throws<InvalidDataException>(() => target.Apply(_ => { }, (_, _) => { }));
    }

    [Test]
    public void BinaryDeltaReader_ApplyAsync_TruncatedDataCommand_ThrowsInvalidDataException()
    {
        // Arrange
        var deltaStream = CreateFastRsyncDeltaStream(EmptyMd5Base64, bw =>
        {
            bw.Write((byte)0x80); // BinaryFormat.DataCommand
            bw.Write(100L);
            bw.Write(new byte[] { 1, 2, 3, 4, 5 });
        });
        var target = new BinaryDeltaReader(deltaStream, null);

        // Act & Assert
        Assert.ThrowsAsync<InvalidDataException>(() =>
            target.ApplyAsync(_ => System.Threading.Tasks.Task.CompletedTask, (_, _) => System.Threading.Tasks.Task.CompletedTask));
    }

    private static readonly string EmptyMd5Base64 = Convert.ToBase64String(new byte[16]);

    private static MemoryStream CreateFastRsyncDeltaStream(string expectedFileHash, Action<BinaryWriter> writeBody = null)
    {
        var deltaStream = new MemoryStream();
        var writer = new BinaryWriter(deltaStream);
        writer.Write(Encoding.ASCII.GetBytes("FRSNCDLTA"));
        writer.Write((byte)0x01);
        writer.Write($"{{\"hashAlgorithm\":\"XXH64\",\"expectedFileHashAlgorithm\":\"MD5\",\"expectedFileHash\":\"{expectedFileHash}\"}}");
        writeBody?.Invoke(writer);
        writer.Flush();
        deltaStream.Seek(0, SeekOrigin.Begin);
        return deltaStream;
    }
}