using NUnit.Framework;
using System;
using System.IO;
using System.Threading.Tasks;
using FastRsync.Core;
using FastRsync.Delta;
using FastRsync.Diagnostics;
using FastRsync.Signature;
using NSubstitute;

namespace FastRsync.Tests
{
    [TestFixture]
    public class BackwardCompatibilityTests
    {
        [Test]
        [TestCase(".\\TestData\\patch112.bin")]
        [TestCase(".\\TestData\\patch200.bin")]
        [TestCase(".\\TestData\\patch231.bin")]
        public async Task ApplyOldPatch(string deltaFileName)
        {
            // Arrange
            const string baseFileName = ".\\TestData\\basefile.bin";
            const string baseFile2Name = ".\\TestData\\basefile2.bin";

            await using var baseFileStream = new FileStream(baseFileName, FileMode.Open);
            await using var deltaFileStream = new FileStream(deltaFileName, FileMode.Open);

            var progressReporter = Substitute.For<IProgress<ProgressReport>>();

            // Act
            var patchedDataStream = new MemoryStream();
            var deltaApplier = new DeltaApplier();
            await deltaApplier.ApplyAsync(baseFileStream, new BinaryDeltaReader(deltaFileStream, progressReporter), patchedDataStream).ConfigureAwait(false);

            // Assert
            var newFileExpectedData = await File.ReadAllBytesAsync(baseFile2Name);
            CollectionAssert.AreEqual(newFileExpectedData, patchedDataStream.ToArray());
            progressReporter.Received().Report(Arg.Any<ProgressReport>());
        }

        [Test]
        [TestCase(".\\TestData\\signature112.bin")]
        [TestCase(".\\TestData\\signature200.bin")]
        [TestCase(".\\TestData\\signature231.bin")]
        public async Task BuildPatchFromOldSignature(string signatureFileName)
        {
            // Arrange
            const string baseFileName = ".\\TestData\\basefile.bin";
            const string baseFile2Name = ".\\TestData\\basefile2.bin";

            await using var signatureFileStream = new FileStream(signatureFileName, FileMode.Open);

            var progressReporter = Substitute.For<IProgress<ProgressReport>>();
            var patchedDataStream = new MemoryStream();

            // Act
            await using (var baseFile2Stream = new FileStream(baseFile2Name, FileMode.Open))
            {
                var deltaStream = new MemoryStream();
                var deltaBuilder = new DeltaBuilder();
                await deltaBuilder.BuildDeltaAsync(baseFile2Stream, new SignatureReader(signatureFileStream, null), new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream))).ConfigureAwait(false);
                deltaStream.Seek(0, SeekOrigin.Begin);

                await using var baseFileStream = new FileStream(baseFileName, FileMode.Open);
                var deltaApplier = new DeltaApplier();
                await deltaApplier.ApplyAsync(baseFileStream, new BinaryDeltaReader(deltaStream, progressReporter), patchedDataStream).ConfigureAwait(false);
            }

            // Assert
            var newFileExpectedData = await File.ReadAllBytesAsync(baseFile2Name);
            CollectionAssert.AreEqual(newFileExpectedData, patchedDataStream.ToArray());
            progressReporter.Received().Report(Arg.Any<ProgressReport>());
        }
    }
}
