using System.IO;
using FastRsync.Core;
using FastRsync.Delta;
using FastRsync.Signature;
using FastRsync.Tests.FastRsyncLegacy;
using NUnit.Framework;

namespace FastRsync.Tests
{
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
            Assert.AreEqual(RsyncFormatType.FastRsync, target.Type);
            Assert.AreEqual(hashAlgorithm.Name, target.HashAlgorithm.Name);
            Assert.AreEqual(hashAlgorithm.HashLengthInBytes, target.HashAlgorithm.HashLengthInBytes);
            Assert.AreEqual(FastRsyncLegacyDeltaExpectedFileHash, target.Metadata.ExpectedFileHash);
            Assert.AreEqual("MD5", target.Metadata.ExpectedFileHashAlgorithm);
            Assert.AreEqual(hashAlgorithm.Name, target.Metadata.HashAlgorithm);
            Assert.Null(target.Metadata.BaseFileHash);
            Assert.Null(target.Metadata.BaseFileHashAlgorithm);
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
            Assert.AreEqual(hashAlgorithm.Name, target.HashAlgorithm.Name);
            Assert.AreEqual(hashAlgorithm.HashLengthInBytes, target.HashAlgorithm.HashLengthInBytes);
            Assert.AreEqual(RsyncFormatType.FastRsync, target.Type); 
            Assert.IsNotEmpty(target.Metadata.ExpectedFileHash);
            Assert.AreEqual("MD5", target.Metadata.ExpectedFileHashAlgorithm);
            Assert.AreEqual(hashAlgorithm.Name, target.Metadata.HashAlgorithm);
        }
    }
}
