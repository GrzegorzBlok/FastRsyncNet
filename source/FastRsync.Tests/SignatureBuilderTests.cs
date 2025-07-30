using System;
using System.IO;
using System.Threading.Tasks;
using FastRsync.Core;
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

    private readonly byte[] xxhash1037TestSignature = {
        0x46, 0x52, 0x53, 0x4E, 0x43, 0x53, 0x47, 0x01, 0x8B, 0x01, 0x7B, 0x22, 0x63, 0x68, 0x75, 0x6E, 0x6B, 0x48, 0x61, 0x73, 0x68, 0x41, 0x6C, 0x67, 0x6F, 0x72, 0x69, 0x74, 0x68, 0x6D, 0x22, 0x3A, 0x22, 0x58, 0x58, 0x48, 0x36, 0x34, 0x22, 0x2C, 0x22, 0x72, 0x6F, 0x6C, 0x6C, 0x69, 0x6E, 0x67, 0x43, 0x68, 0x65, 0x63, 0x6B, 0x73, 0x75, 0x6D, 0x41, 0x6C, 0x67, 0x6F, 0x72, 0x69, 0x74, 0x68, 0x6D, 0x22, 0x3A, 0x22, 0x41, 0x64, 0x6C, 0x65, 0x72, 0x33, 0x32, 0x22, 0x2C, 0x22, 0x62, 0x61, 0x73, 0x65, 0x46, 0x69, 0x6C, 0x65, 0x48, 0x61, 0x73, 0x68, 0x41, 0x6C, 0x67, 0x6F, 0x72, 0x69, 0x74, 0x68, 0x6D, 0x22, 0x3A, 0x22, 0x4D, 0x44, 0x35, 0x22, 0x2C, 0x22, 0x62, 0x61, 0x73, 0x65, 0x46, 0x69, 0x6C, 0x65, 0x48, 0x61, 0x73, 0x68, 0x22, 0x3A, 0x22, 0x41, 0x33, 0x37, 0x45, 0x79, 0x65, 0x6A, 0x4E, 0x6E, 0x4B, 0x6F, 0x6C, 0x62, 0x68, 0x64, 0x34, 0x68, 0x73, 0x6F, 0x4E, 0x6F, 0x51, 0x3D, 0x3D, 0x22, 0x7D, 0x0D, 0x04, 0x2F, 0xFC, 0xF4, 0x6C, 0x7B, 0x52, 0x06, 0x17, 0x0A, 0x90, 0x3D, 0x70
    };

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
        CollectionAssert.AreEqual(xxhash1037TestSignature, signatureStream.ToArray());

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
        CollectionAssert.AreEqual(xxhash1037TestSignature, signatureStream.ToArray());

        CommonAsserts.ValidateSignature(signatureStream, SupportedAlgorithms.Hashing.XxHash(), Utils.GetMd5(data), new Adler32RollingChecksum());

        progressReporter.Received().Report(Arg.Any<ProgressReport>());
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