using System.IO;
using FastRsync.Hash;
using FastRsync.Signature;
using NUnit.Framework;

namespace FastRsync.Tests
{
    class CommonAsserts
    {
        public static void ValidateSignature(Stream signatureStream, IHashAlgorithm hashAlgorithm, string baseFileHash, IRollingChecksum rollingAlgorithm)
        {
            signatureStream.Seek(0, SeekOrigin.Begin);
            var sig = new SignatureReader(signatureStream, null).ReadSignature();
            Assert.That(sig.Type, Is.EqualTo(RsyncFormatType.FastRsync));
            Assert.That(sig.HashAlgorithm.Name, Is.EqualTo(hashAlgorithm.Name));
            Assert.That(sig.Metadata.ChunkHashAlgorithm, Is.EqualTo(hashAlgorithm.Name));
            Assert.That(sig.HashAlgorithm.HashLengthInBytes, Is.EqualTo(hashAlgorithm.HashLengthInBytes));
            Assert.That(sig.RollingChecksumAlgorithm.Name, Is.EqualTo(rollingAlgorithm.Name));
            Assert.That(sig.Metadata.RollingChecksumAlgorithm, Is.EqualTo(rollingAlgorithm.Name));
            Assert.That(sig.Metadata.BaseFileHashAlgorithm, Is.EqualTo("MD5"));
            Assert.That(sig.Metadata.BaseFileHash, Is.EqualTo(baseFileHash));
        }
    }
}
