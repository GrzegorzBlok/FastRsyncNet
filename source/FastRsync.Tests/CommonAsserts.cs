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
            Assert.AreEqual(RsyncFormatType.FastRsync, sig.Type);
            Assert.AreEqual(hashAlgorithm.Name, sig.HashAlgorithm.Name);
            Assert.AreEqual(hashAlgorithm.Name, sig.Metadata.ChunkHashAlgorithm);
            Assert.AreEqual(hashAlgorithm.HashLength, sig.HashAlgorithm.HashLength);
            Assert.AreEqual(rollingAlgorithm.Name, sig.RollingChecksumAlgorithm.Name);
            Assert.AreEqual(rollingAlgorithm.Name, sig.Metadata.RollingChecksumAlgorithm);
            Assert.AreEqual("MD5", sig.Metadata.BaseFileHashAlgorithm);
            Assert.AreEqual(baseFileHash, sig.Metadata.BaseFileHash);
        }
    }
}
