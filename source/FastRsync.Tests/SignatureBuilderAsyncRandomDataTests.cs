using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FastRsync.Core;
using FastRsync.Diagnostics;
using FastRsync.Hash;
using FastRsync.Signature;
using NSubstitute;
using NUnit.Framework;

namespace FastRsync.Tests
{
    [TestFixture]
    public class SignatureBuilderAsyncRandomDataTests
    {
        [Test]
        [TestCase(2, SignatureBuilder.MinimumChunkSize)]
        [TestCase(10, SignatureBuilder.MinimumChunkSize)]
        [TestCase(16974, SignatureBuilder.MinimumChunkSize)]
        [TestCase(2, SignatureBuilder.DefaultChunkSize)]
        [TestCase(10, SignatureBuilder.DefaultChunkSize)]
        [TestCase(16974, SignatureBuilder.DefaultChunkSize)]
        [TestCase(2, SignatureBuilder.MaximumChunkSize)]
        [TestCase(10, SignatureBuilder.MaximumChunkSize)]
        [TestCase(16974, SignatureBuilder.MaximumChunkSize)]
        public async Task SignatureBuilderAsyncXXHash_ForRandomData_BuildsSignature(int numberOfBytes, short chunkSize)
        {
            // Arrange
            var data = new byte[numberOfBytes];
            new Random().NextBytes(data);
            var dataStream = new MemoryStream(data);
            var signatureStream = new MemoryStream();

            var progressReporter = Substitute.For<IProgress<ProgressReport>>();

            // Act
            var target = new SignatureBuilder
            {
                ChunkSize = chunkSize,
                ProgressReport = progressReporter
            };
            await target.BuildAsync(dataStream, new SignatureWriter(signatureStream)).ConfigureAwait(false);

            // Assert
            CommonAsserts.ValidateSignature(signatureStream, SupportedAlgorithms.Hashing.XxHash(), Utils.GetMd5(data), new Adler32RollingChecksum());

            progressReporter.Received().Report(Arg.Any<ProgressReport>());
        }

        [Test]
        [TestCase(2, SignatureBuilder.MinimumChunkSize)]
        [TestCase(10, SignatureBuilder.MinimumChunkSize)]
        [TestCase(16974, SignatureBuilder.MinimumChunkSize)]
        [TestCase(2, SignatureBuilder.DefaultChunkSize)]
        [TestCase(10, SignatureBuilder.DefaultChunkSize)]
        [TestCase(16974, SignatureBuilder.DefaultChunkSize)]
        [TestCase(2, SignatureBuilder.MaximumChunkSize)]
        [TestCase(10, SignatureBuilder.MaximumChunkSize)]
        [TestCase(16974, SignatureBuilder.MaximumChunkSize)]
        public async Task SignatureBuilderAsyncXXHashAdlerV2_ForRandomData_BuildsSignature(int numberOfBytes, short chunkSize)
        {
            // Arrange
            var data = new byte[numberOfBytes];
            new Random().NextBytes(data);
            var dataStream = new MemoryStream(data);
            var signatureStream = new MemoryStream();

            var progressReporter = Substitute.For<IProgress<ProgressReport>>();

            // Act
            var target = new SignatureBuilder(SupportedAlgorithms.Hashing.XxHash(), SupportedAlgorithms.Checksum.Adler32RollingV2())
            {
                ChunkSize = chunkSize,
                ProgressReport = progressReporter
            };
            await target.BuildAsync(dataStream, new SignatureWriter(signatureStream)).ConfigureAwait(false);

            // Assert
            CommonAsserts.ValidateSignature(signatureStream, SupportedAlgorithms.Hashing.XxHash(), Utils.GetMd5(data), new Adler32RollingChecksumV2());

            progressReporter.Received().Report(Arg.Any<ProgressReport>());
        }

        [Test]
        [TestCase(2, SignatureBuilder.MinimumChunkSize)]
        [TestCase(10, SignatureBuilder.MinimumChunkSize)]
        [TestCase(16974, SignatureBuilder.MinimumChunkSize)]
        [TestCase(2, SignatureBuilder.DefaultChunkSize)]
        [TestCase(10, SignatureBuilder.DefaultChunkSize)]
        [TestCase(16974, SignatureBuilder.DefaultChunkSize)]
        [TestCase(2, SignatureBuilder.MaximumChunkSize)]
        [TestCase(10, SignatureBuilder.MaximumChunkSize)]
        [TestCase(16974, SignatureBuilder.MaximumChunkSize)]
        public async Task SignatureBuilderAsyncSha1_ForRandomData_BuildsSignature(int numberOfBytes, short chunkSize)
        {
            // Arrange
            var data = new byte[numberOfBytes];
            new Random().NextBytes(data);
            var dataStream = new MemoryStream(data);
            var signatureStream = new MemoryStream();

            var progressReporter = Substitute.For<IProgress<ProgressReport>>();

            // Act
            var target = new SignatureBuilder(SupportedAlgorithms.Hashing.Sha1(), SupportedAlgorithms.Checksum.Adler32Rolling())
            {
                ChunkSize = chunkSize,
                ProgressReport = progressReporter
            };
            await target.BuildAsync(dataStream, new SignatureWriter(signatureStream)).ConfigureAwait(false);

            // Assert
            CommonAsserts.ValidateSignature(signatureStream, new CryptographyHashAlgorithmWrapper("SHA1", SHA1.Create()), Utils.GetMd5(data), new Adler32RollingChecksum());

            progressReporter.Received().Report(Arg.Any<ProgressReport>());
        }
    }
}
