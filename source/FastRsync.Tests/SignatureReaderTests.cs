using System;
using System.IO;
using FastRsync.Core;
using FastRsync.Diagnostics;
using FastRsync.Hash;
using FastRsync.Signature;
using NSubstitute;
using NUnit.Framework;

namespace FastRsync.Tests
{
    [TestFixture]
    public class SignatureReaderTests
    {
        [Test]
        public void SignatureReader_ReadsOctodiffSignature()
        {
            // Arrange
            byte[] octodiffxxHashSignature = {
                0x4F, 0x43, 0x54, 0x4F, 0x53, 0x49, 0x47, 0x01, 0x05, 0x58, 0x58, 0x48, 0x36, 0x34, 0x07, 0x41, 0x64, 0x6C, 0x65, 0x72, 0x33, 0x32, 0x3E, 0x3E, 0x3E, 0x0D, 0x04, 0x2F, 0xFC, 0xF4, 0x6C, 0x7B, 0x52, 0x06, 0x17, 0x0A, 0x90, 0x3D, 0x70
            };
            var signatureStream = new MemoryStream(octodiffxxHashSignature);
            var progressReporter = Substitute.For<IProgress<ProgressReport>>();

            // Act
            var target = new SignatureReader(signatureStream, progressReporter).ReadSignature();

            // Assert
            var hashAlgorithm = SupportedAlgorithms.Hashing.Default();
            Assert.AreEqual(RsyncFormatType.Octodiff, target.Type);
            Assert.AreEqual(hashAlgorithm.Name, target.HashAlgorithm.Name);
            Assert.AreEqual(hashAlgorithm.HashLengthInBytes, target.HashAlgorithm.HashLengthInBytes);
            Assert.AreEqual(new Adler32RollingChecksum().Name, target.RollingChecksumAlgorithm.Name);

            progressReporter.Received().Report(Arg.Any<ProgressReport>());
        }

        [Test]
        public void SignatureReader_ReadsRandomData_ThrowsException()
        {
            // Arrange
            var signatureData = new byte[1458];
            new Random().NextBytes(signatureData);
            var signatureStream = new MemoryStream(signatureData);
            var progressReporter = Substitute.For<IProgress<ProgressReport>>();

            // Act
            var target = new SignatureReader(signatureStream, progressReporter);

            // Assert
            Assert.Throws<InvalidDataException>(() => target.ReadSignature());
        }
    }
}
