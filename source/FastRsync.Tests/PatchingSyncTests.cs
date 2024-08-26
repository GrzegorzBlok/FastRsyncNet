using System;
using System.IO;
using FastRsync.Core;
using FastRsync.Delta;
using FastRsync.Diagnostics;
using FastRsync.Signature;
using FastRsync.Tests.OctodiffLegacy;
using NSubstitute;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace FastRsync.Tests
{
    [TestFixture]
    public class PatchingSyncTests
    {       
        [Test]
        [TestCase(1378, 129, SignatureBuilder.MinimumChunkSize)]
        [TestCase(1378, 129, SignatureBuilder.DefaultChunkSize)]
        [TestCase(1378, 129, SignatureBuilder.MaximumChunkSize)]
        [TestCase(16974, 8452, SignatureBuilder.MinimumChunkSize)]
        [TestCase(16974, 8452, SignatureBuilder.DefaultChunkSize)]
        [TestCase(16974, 8452, SignatureBuilder.MaximumChunkSize)]
        [TestCase(6666, 6666, SignatureBuilder.MinimumChunkSize)]
        [TestCase(6666, 6666, SignatureBuilder.DefaultChunkSize)]
        [TestCase(6666, 6666, SignatureBuilder.MaximumChunkSize)]
        public void PatchingSyncXXHash_ForNewData_PatchesFile(int baseNumberOfBytes, int newDataNumberOfBytes, short chunkSize)
        {
            // Arrange
            var (baseDataStream, baseSignatureStream, newData, newDataStream) = Utils.PrepareTestData(baseNumberOfBytes, newDataNumberOfBytes, chunkSize);

            var progressReporter = Substitute.For<IProgress<ProgressReport>>();

            // Act
            var deltaStream = new MemoryStream();
            var deltaBuilder = new DeltaBuilder();
            deltaBuilder.BuildDelta(newDataStream, new SignatureReader(baseSignatureStream, null), new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream)));
            deltaStream.Seek(0, SeekOrigin.Begin);

            var patchedDataStream = new MemoryStream();
            var deltaApplier = new DeltaApplier();
            deltaApplier.Apply(baseDataStream, new BinaryDeltaReader(deltaStream, progressReporter), patchedDataStream);

            // Assert
            CollectionAssert.AreEqual(newData, patchedDataStream.ToArray());
            progressReporter.Received().Report(Arg.Any<ProgressReport>());
        }
        
        [Test]
        [TestCase(1378, 129, SignatureBuilder.MinimumChunkSize)]
        [TestCase(1378, 129, SignatureBuilder.DefaultChunkSize)]
        [TestCase(1378, 129, SignatureBuilder.MaximumChunkSize)]
        [TestCase(16974, 8452, SignatureBuilder.MinimumChunkSize)]
        [TestCase(16974, 8452, SignatureBuilder.DefaultChunkSize)]
        [TestCase(16974, 8452, SignatureBuilder.MaximumChunkSize)]
        [TestCase(6666, 6666, SignatureBuilder.MinimumChunkSize)]
        [TestCase(6666, 6666, SignatureBuilder.DefaultChunkSize)]
        [TestCase(6666, 6666, SignatureBuilder.MaximumChunkSize)]
        public void PatchingSyncXXHash_ForOctodiffSignature_PatchesFile(int baseNumberOfBytes, int newDataNumberOfBytes, short chunkSize)
        {
            // Arrange
            var (baseDataStream, baseSignatureStream, newData, newDataStream) = PrepareTestDataWithOctodiffSignature(baseNumberOfBytes, newDataNumberOfBytes, chunkSize);

            var progressReporter = Substitute.For<IProgress<ProgressReport>>();

            // Act
            var deltaStream = new MemoryStream();
            var deltaBuilder = new DeltaBuilder();
            deltaBuilder.BuildDelta(newDataStream, new SignatureReader(baseSignatureStream, null), new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream)));
            deltaStream.Seek(0, SeekOrigin.Begin);

            var patchedDataStream = new MemoryStream();
            var deltaApplier = new DeltaApplier();
            deltaApplier.Apply(baseDataStream, new BinaryDeltaReader(deltaStream, progressReporter), patchedDataStream);

            // Assert
            CollectionAssert.AreEqual(newData, patchedDataStream.ToArray());
            progressReporter.Received().Report(Arg.Any<ProgressReport>());
        }
        
        [Test]
        [TestCase(1378, 129, SignatureBuilder.MinimumChunkSize)]
        [TestCase(1378, 129, SignatureBuilder.DefaultChunkSize)]
        [TestCase(1378, 129, SignatureBuilder.MaximumChunkSize)]
        [TestCase(16974, 8452, SignatureBuilder.MinimumChunkSize)]
        [TestCase(16974, 8452, SignatureBuilder.DefaultChunkSize)]
        [TestCase(16974, 8452, SignatureBuilder.MaximumChunkSize)]
        [TestCase(6666, 6666, SignatureBuilder.MinimumChunkSize)]
        [TestCase(6666, 6666, SignatureBuilder.DefaultChunkSize)]
        [TestCase(6666, 6666, SignatureBuilder.MaximumChunkSize)]
        public void PatchingSyncXXHash_ForOctodiffSignatureAndPatch_PatchesFile(int baseNumberOfBytes, int newDataNumberOfBytes, short chunkSize)
        {
            // Arrange
            var (baseDataStream, baseSignatureStream, newData, newDataStream) = PrepareTestDataWithOctodiffSignature(baseNumberOfBytes, newDataNumberOfBytes, chunkSize);

            var progressReporter = Substitute.For<IProgress<ProgressReport>>();

            var deltaStream = new MemoryStream();
            var deltaBuilder = new OctodiffDeltaBuilder();
            deltaBuilder.BuildDelta(newDataStream, new OctodiffSignatureReader(baseSignatureStream, null), new OctodiffAggregateCopyOperationsDecorator(new OctodiffBinaryDeltaWriter(deltaStream)));
            deltaStream.Seek(0, SeekOrigin.Begin);

            // Act
            var patchedDataStream = new MemoryStream();
            var deltaApplier = new DeltaApplier();
            deltaApplier.Apply(baseDataStream, new BinaryDeltaReader(deltaStream, progressReporter), patchedDataStream);

            // Assert
            CollectionAssert.AreEqual(newData, patchedDataStream.ToArray());
            progressReporter.Received().Report(Arg.Any<ProgressReport>());
        }

        private static (MemoryStream baseDataStream, MemoryStream baseSignatureStream, byte[] newData, MemoryStream newDataStream) PrepareTestDataWithOctodiffSignature(int baseNumberOfBytes, int newDataNumberOfBytes,
            short chunkSize)
        {
            var baseData = new byte[baseNumberOfBytes];
            new Random().NextBytes(baseData);
            var baseDataStream = new MemoryStream(baseData);
            var baseSignatureStream = new MemoryStream();

            var signatureBuilder = new OctodiffSignatureBuilder
            {
                ChunkSize = chunkSize
            };
            signatureBuilder.Build(baseDataStream, new OctodiffSignatureWriter(baseSignatureStream));
            baseSignatureStream.Seek(0, SeekOrigin.Begin);

            var newData = new byte[newDataNumberOfBytes];
            new Random().NextBytes(newData);
            var newDataStream = new MemoryStream(newData);
            return (baseDataStream, baseSignatureStream, newData, newDataStream);
        }
    }
}
