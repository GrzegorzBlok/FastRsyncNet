using System;
using System.IO;
using System.Threading.Tasks;
using FastRsync.Core;
using FastRsync.Delta;
using FastRsync.Diagnostics;
using FastRsync.Hash;
using FastRsync.Signature;
using NSubstitute;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace FastRsync.Tests;

[TestFixture]
public class SignatureBuilderTests
{
    private const int RandomSeed = 123;

    // The exact expected signature for the 1037-byte seeded test data: FRSNCSG header with
    // version 0x01, the length-prefixed metadata JSON, and one chunk record (Int16 length 1037,
    // UInt32 Adler32 checksum, 8-byte xxHash64). Guards the on-disk format against accidental
    // changes; update deliberately when the format legitimately evolves.
    private const string Xxhash1037TestSignatureMetadataJson =
        "{\"chunkHashAlgorithm\":\"XXH64\",\"rollingChecksumAlgorithm\":\"Adler32\",\"baseFileHashAlgorithm\":\"MD5\",\"baseFileHash\":\"A37EyejNnKolbhd4hsoNoQ==\",\"baseFileLength\":1037}";

    private static byte[] BuildExpectedXxhash1037TestSignature()
    {
        var ms = new MemoryStream();
        var writer = new BinaryWriter(ms);
        writer.Write(new byte[] { 0x46, 0x52, 0x53, 0x4E, 0x43, 0x53, 0x47, 0x01 }); // "FRSNCSG" + version
        writer.Write(Xxhash1037TestSignatureMetadataJson); // BinaryWriter length-prefixed string
        writer.Write(new byte[] { 0x0D, 0x04, 0x2F, 0xFC, 0xF4, 0x6C, 0x7B, 0x52, 0x06, 0x17, 0x0A, 0x90, 0x3D, 0x70 });
        writer.Flush();
        return ms.ToArray();
    }

    [Test]
    public void SignatureBuilderXXHash_BuildsSignature()
    {
        // Arrange
        const int dataLength = 1037;
        var data = new byte[dataLength];
        new Random(RandomSeed).NextBytes(data);
        var dataStream = new MemoryStream(data);
        var signatureStream = new MemoryStream();

        var progressReporter = Substitute.For<IProgress<ProgressReport>>();

        // Act
        var target = new SignatureBuilder
        {
            ProgressReport = progressReporter
        };
        target.Build(dataStream, new SignatureWriter(signatureStream));

        // Assert
        CollectionAssert.AreEqual(BuildExpectedXxhash1037TestSignature(), signatureStream.ToArray());

        CommonAsserts.ValidateSignature(signatureStream, SupportedAlgorithms.Hashing.XxHash(), Utils.GetMd5(data), new Adler32RollingChecksum());

        progressReporter.Received().Report(Arg.Any<ProgressReport>());
    }

    [Test]
    public async Task SignatureBuilderAsyncXXHash_BuildsSignature()
    {
        // Arrange
        const int dataLength = 1037;
        var data = new byte[dataLength];
        new Random(RandomSeed).NextBytes(data);
        var dataStream = new MemoryStream(data);
        var signatureStream = new MemoryStream();

        var progressReporter = Substitute.For<IProgress<ProgressReport>>();

        // Act
        var target = new SignatureBuilder
        {
            ProgressReport = progressReporter
        };
        await target.BuildAsync(dataStream, new SignatureWriter(signatureStream)).ConfigureAwait(false);

        // Assert
        CollectionAssert.AreEqual(BuildExpectedXxhash1037TestSignature(), signatureStream.ToArray());

        CommonAsserts.ValidateSignature(signatureStream, SupportedAlgorithms.Hashing.XxHash(), Utils.GetMd5(data), new Adler32RollingChecksum());

        progressReporter.Received().Report(Arg.Any<ProgressReport>());
    }

    [Test]
    [TestCase(0)]
    [TestCase(1037)]
    [TestCase(16974)]
    public void SignatureBuilder_SinglePassAndTwoPass_ProduceIdenticalSignature(int dataLength)
    {
        // Arrange
        var data = new byte[dataLength];
        new Random(RandomSeed).NextBytes(data);

        // Act
        var singlePassStream = new MemoryStream();
        new SignatureBuilder { SinglePassBuild = true }.Build(new MemoryStream(data), new SignatureWriter(singlePassStream));

        var twoPassStream = new MemoryStream();
        new SignatureBuilder { SinglePassBuild = false }.Build(new MemoryStream(data), new SignatureWriter(twoPassStream));

        // Assert
        CollectionAssert.AreEqual(twoPassStream.ToArray(), singlePassStream.ToArray());
    }

    [Test]
    [TestCase(true)]
    [TestCase(false)]
    public void SignatureBuilder_WritesBaseFileLengthMetadata(bool singlePassBuild)
    {
        // Arrange
        const int dataLength = 16974;
        var data = new byte[dataLength];
        new Random(RandomSeed).NextBytes(data);
        var signatureStream = new MemoryStream();

        // Act
        var target = new SignatureBuilder { SinglePassBuild = singlePassBuild };
        target.Build(new MemoryStream(data), new SignatureWriter(signatureStream));

        // Assert
        signatureStream.Seek(0, SeekOrigin.Begin);
        var signature = new SignatureReader(signatureStream, null).ReadSignature();
        Assert.That(signature.Metadata.BaseFileLength, Is.EqualTo(dataLength));
    }

    [Test]
    public void SignatureBuilder_TwoPassBuild_ForNewData_PatchesFile()
    {
        // Arrange - the default is single-pass; keep the two-pass mode covered end to end
        var baseData = new byte[16974];
        new Random(RandomSeed).NextBytes(baseData);
        var baseDataStream = new MemoryStream(baseData);
        var signatureStream = new MemoryStream();
        new SignatureBuilder { SinglePassBuild = false }.Build(baseDataStream, new SignatureWriter(signatureStream));
        signatureStream.Seek(0, SeekOrigin.Begin);

        var newData = new byte[8452];
        new Random(RandomSeed + 1).NextBytes(newData);
        var newDataStream = new MemoryStream(newData);

        // Act
        var deltaStream = new MemoryStream();
        new DeltaBuilder().BuildDelta(newDataStream, new SignatureReader(signatureStream, null), new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream)));
        deltaStream.Seek(0, SeekOrigin.Begin);

        var patchedDataStream = new MemoryStream();
        new DeltaApplier().Apply(baseDataStream, new BinaryDeltaReader(deltaStream, null), patchedDataStream);

        // Assert
        CollectionAssert.AreEqual(newData, patchedDataStream.ToArray());
    }

    [Test]
    public async Task SignatureBuilderAsyncXXHash_ForEmptyStream_BuildsSignature()
    {
        // Arrange
        var dataStream = new MemoryStream();
        var signatureStream = new MemoryStream();

        var progressReporter = Substitute.For<IProgress<ProgressReport>>();

        // Act
        var target = new SignatureBuilder
        {
            ProgressReport = progressReporter
        };
        await target.BuildAsync(dataStream, new SignatureWriter(signatureStream)).ConfigureAwait(false);

        // Assert
        CommonAsserts.ValidateSignature(signatureStream, SupportedAlgorithms.Hashing.XxHash(), Utils.GetMd5(dataStream.ToArray()), new Adler32RollingChecksum());

        progressReporter.Received().Report(Arg.Any<ProgressReport>());
    }

    [Test]
    public void SignatureBuilderSyncXXHash_ForEmptyStream_BuildsSignature()
    {
        // Arrange
        var dataStream = new MemoryStream();
        var signatureStream = new MemoryStream();

        var progressReporter = Substitute.For<IProgress<ProgressReport>>();

        // Act
        var target = new SignatureBuilder
        {
            ProgressReport = progressReporter
        };
        target.Build(dataStream, new SignatureWriter(signatureStream));

        // Assert
        CommonAsserts.ValidateSignature(signatureStream, SupportedAlgorithms.Hashing.XxHash(), Utils.GetMd5(dataStream.ToArray()), new Adler32RollingChecksum());

        progressReporter.Received().Report(Arg.Any<ProgressReport>());
    }
}