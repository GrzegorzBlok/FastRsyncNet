using System;
using System.IO;
using System.Linq;
using FastRsync.Core;
using FastRsync.Delta;
using FastRsync.Diagnostics;
using FastRsync.Signature;
using NSubstitute;
using NUnit.Framework;
using System.Security.Cryptography;
using NUnit.Framework.Legacy;

namespace FastRsync.Tests
{
    [TestFixture]
    public class PatchingBigFilesTests
    {
        [Test]
        [TestCase(@"c:\bigfile1.bin", @"c:\bigfile2.bin")]
        public void PatchingSyncXXHash_BigFile(string originalFileName, string newFileName)
        {
            try
            {
                // Arrange
                var (baseDataStream, baseSignatureStream) = PrepareTestData(originalFileName);

                var progressReporter = Substitute.For<IProgress<ProgressReport>>();

                var deltaFileName = Path.GetTempFileName();
                var patchedFileName = Path.GetTempFileName();

                // Act
                using (var deltaStream = new FileStream(deltaFileName, FileMode.OpenOrCreate))
                using (var patchedDataStream = new FileStream(patchedFileName, FileMode.OpenOrCreate)) 
                using(var newDataStream = new FileStream(newFileName, FileMode.Open))
                {
                    var deltaBuilder = new DeltaBuilder();
                    deltaBuilder.BuildDelta(newDataStream, new SignatureReader(baseSignatureStream, null),
                        new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream)));
                    deltaStream.Seek(0, SeekOrigin.Begin);

                    var deltaApplier = new DeltaApplier();
                    deltaApplier.Apply(baseDataStream, new BinaryDeltaReader(deltaStream, progressReporter),
                        patchedDataStream);
                }

                // Assert
                Assert.That(new FileInfo(newFileName).Length, Is.EqualTo(new FileInfo(patchedFileName).Length));
                Assert.That(CompareFilesByHash(newFileName, patchedFileName), Is.True);
                progressReporter.Received().Report(Arg.Any<ProgressReport>());
            }
            catch (Exception e)
            {
                Assert.Fail();
            }
        }

        public static (Stream baseDataStream, Stream baseSignatureStream) PrepareTestData(string originalFileName)
        {
            var baseDataStream = new FileStream(originalFileName, FileMode.Open);
            var baseSignatureStream = new MemoryStream();

            var signatureBuilder = new SignatureBuilder();
            signatureBuilder.Build(baseDataStream, new SignatureWriter(baseSignatureStream));
            baseSignatureStream.Seek(0, SeekOrigin.Begin);
            return (baseDataStream, baseSignatureStream);
        }

        public static bool CompareFilesByHash(string fileName1, string fileName2)
        {
            byte[] hash1, hash2;

            using (var stream = File.OpenRead(fileName1))
                hash1 = MD5.Create().ComputeHash(stream);

            using (var stream = File.OpenRead(fileName2))
                hash2 = MD5.Create().ComputeHash(stream);

            return hash1.SequenceEqual(hash2);
        }
    }
}
