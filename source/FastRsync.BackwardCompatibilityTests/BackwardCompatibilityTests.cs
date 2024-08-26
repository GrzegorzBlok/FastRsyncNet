using System;
using System.IO;
using System.Threading.Tasks;
using FastRsync.Core;
using FastRsync.Delta;
using FastRsync.Diagnostics;
using FastRsync.Signature;
using NSubstitute;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace FastRsync.BackwardCompatibilityTests
{
    [TestFixture]
    public class BackwardCompatibilityTests
    {
        [Test]
        [TestCase(1378, 129)]
        [TestCase(13780, 1290)]
        [TestCase(137800, 1290)]
        public async Task LegacyLibraryAppliesNewPatch(int baseNumberOfBytes, int newDataNumberOfBytes)
        {
            // Arrange - signature and patch from the current library
            var (baseDataStream, baseSignatureStream, newData, newDataStream) = await PrepareTestDataAsync(baseNumberOfBytes, newDataNumberOfBytes, SupportedAlgorithms.Checksum.Adler32Rolling()).ConfigureAwait(false);

            var deltaStream = new MemoryStream();
            var deltaBuilder = new DeltaBuilder();
            await deltaBuilder.BuildDeltaAsync(newDataStream, new SignatureReader(baseSignatureStream, null), new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream))).ConfigureAwait(false);
            deltaStream.Seek(0, SeekOrigin.Begin);

            // Act using the legacy library
            var progressReporter = Substitute.For<IProgress<FastRsyncLegacy231.Diagnostics.ProgressReport>>();
            var patchedDataStream = new MemoryStream();
            var deltaApplier = new FastRsyncLegacy231.Delta.DeltaApplier();
            await deltaApplier.ApplyAsync(baseDataStream, new FastRsyncLegacy231.Delta.BinaryDeltaReader(deltaStream, progressReporter), patchedDataStream).ConfigureAwait(false);

            // Assert
            CollectionAssert.AreEqual(newData, patchedDataStream.ToArray());
            progressReporter.Received().Report(Arg.Any<FastRsyncLegacy231.Diagnostics.ProgressReport>());
        }

        [Test]
        [TestCase(1378, 129)]
        [TestCase(13780, 1290)]
        [TestCase(137800, 1290)]
        public async Task LegacyLibraryPreparesAndAppliesNewPatch(int baseNumberOfBytes, int newDataNumberOfBytes)
        {
            // Arrange - signature from the current library
            var (baseDataStream, baseSignatureStream, newData, newDataStream) = await PrepareTestDataAsync(baseNumberOfBytes, newDataNumberOfBytes, SupportedAlgorithms.Checksum.Adler32Rolling()).ConfigureAwait(false);

            // Act using the legacy library
            var deltaStream = new MemoryStream();
            var deltaBuilder = new FastRsyncLegacy231.Delta.DeltaBuilder();
            await deltaBuilder.BuildDeltaAsync(newDataStream, new FastRsyncLegacy231.Signature.SignatureReader(baseSignatureStream, null), 
                new FastRsyncLegacy231.Core.AggregateCopyOperationsDecorator(new FastRsyncLegacy231.Delta.BinaryDeltaWriter(deltaStream))).ConfigureAwait(false);
            deltaStream.Seek(0, SeekOrigin.Begin);

            var progressReporter = Substitute.For<IProgress<FastRsyncLegacy231.Diagnostics.ProgressReport>>();
            var patchedDataStream = new MemoryStream();
            var deltaApplier = new FastRsyncLegacy231.Delta.DeltaApplier();
            await deltaApplier.ApplyAsync(baseDataStream, new FastRsyncLegacy231.Delta.BinaryDeltaReader(deltaStream, progressReporter), patchedDataStream).ConfigureAwait(false);

            // Assert
            CollectionAssert.AreEqual(newData, patchedDataStream.ToArray());
            progressReporter.Received().Report(Arg.Any<FastRsyncLegacy231.Diagnostics.ProgressReport>());
        }

        [Test]
        [TestCase(1378, 129)]
        [TestCase(13780, 1290)]
        [TestCase(137800, 1290)]
        public async Task LegacyLibraryPreparesPatchForNewLibraryToApplyIt(int baseNumberOfBytes, int newDataNumberOfBytes)
        {
            // Arrange - signature from current library, patch from old library
            var (baseDataStream, baseSignatureStream, newData, newDataStream) = await PrepareTestDataAsync(baseNumberOfBytes, newDataNumberOfBytes, SupportedAlgorithms.Checksum.Adler32Rolling()).ConfigureAwait(false);

            var deltaStream = new MemoryStream();
            var deltaBuilder = new FastRsyncLegacy231.Delta.DeltaBuilder();
            await deltaBuilder.BuildDeltaAsync(newDataStream, new FastRsyncLegacy231.Signature.SignatureReader(baseSignatureStream, null),
                new FastRsyncLegacy231.Core.AggregateCopyOperationsDecorator(new FastRsyncLegacy231.Delta.BinaryDeltaWriter(deltaStream))).ConfigureAwait(false);
            deltaStream.Seek(0, SeekOrigin.Begin);

            // Act - apply patch using the current library
            var progressReporter = Substitute.For<IProgress<ProgressReport>>();
            var patchedDataStream = new MemoryStream();
            var deltaApplier = new DeltaApplier();
            await deltaApplier.ApplyAsync(baseDataStream, new BinaryDeltaReader(deltaStream, progressReporter), patchedDataStream).ConfigureAwait(false);

            // Assert
            CollectionAssert.AreEqual(newData, patchedDataStream.ToArray());
            progressReporter.Received().Report(Arg.Any<ProgressReport>());
        }

        private static async Task<(MemoryStream baseDataStream, MemoryStream baseSignatureStream, byte[] newData, MemoryStream newDataStream)> PrepareTestDataAsync(int baseNumberOfBytes, int newDataNumberOfBytes, Hash.IRollingChecksum rollingChecksumAlg)
        {
            var baseData = new byte[baseNumberOfBytes];
            new Random().NextBytes(baseData);
            var baseDataStream = new MemoryStream(baseData);
            var baseSignatureStream = new MemoryStream();

            var signatureBuilder = new SignatureBuilder(SupportedAlgorithms.Hashing.XxHash(), rollingChecksumAlg);
            await signatureBuilder.BuildAsync(baseDataStream, new SignatureWriter(baseSignatureStream)).ConfigureAwait(false);
            baseSignatureStream.Seek(0, SeekOrigin.Begin);

            var newData = new byte[newDataNumberOfBytes];
            new Random().NextBytes(newData);
            var newDataStream = new MemoryStream(newData);
            return (baseDataStream, baseSignatureStream, newData, newDataStream);
        }
    }
}
