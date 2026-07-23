using FastRsync.Core;
using FastRsync.Delta;
using FastRsync.Diagnostics;
using FastRsync.Signature;
using NSubstitute;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using FastRsync.Hash;

namespace FastRsync.Tests;

[TestFixture]
// Multi-gigabyte files (~8GB temp disk, minutes per case); excluded from fast/coverage runs.
[Category("Integration")]
public class PatchingBigFilesTests
{
    [Test]
    [TestCase(2050, 2790, "XXH64", "Adler32")]
    [TestCase(2050, 2790, "XXH3", "Adler32")]
    [TestCase(2050, 2790, "XXH64", "Adler32V3")]
    [TestCase(2050, 2790, "XXH3", "Adler32V3")]
    public void PatchingSyncXXHash_BigFile(int baseNumberOfMBytes, int newNumberOfMBytes, string signatureHashingAlgorithm, string rollingChecksumAlgorithm)
    {
        // Arrange
        var signatureHash = SupportedAlgorithms.Hashing.Create(signatureHashingAlgorithm);
        var rollingChecksum = SupportedAlgorithms.Checksum.Create(rollingChecksumAlgorithm);

        var baseFileName = Path.GetTempFileName();
        var newFileName = Path.GetTempFileName();
        var deltaFileName = Path.GetTempFileName();
        var patchedFileName = Path.GetTempFileName();
        try
        {
            var progressReporter = Substitute.For<IProgress<ProgressReport>>();

            {
                var buffer = new byte[16384];
                var rng = new Random();

                using var baseFileStream = new FileStream(baseFileName, FileMode.Create);
                for (var i = 0; i < baseNumberOfMBytes * 64; i++)
                {
                    rng.NextBytes(buffer);
                    baseFileStream.Write(buffer, 0, buffer.Length);
                }

                baseFileStream.Seek(0, SeekOrigin.Begin);

                using var newFileStream = new FileStream(newFileName, FileMode.Create);
                for (var i = 0; i < newNumberOfMBytes * 64; i++)
                {
                    rng.NextBytes(buffer);
                    newFileStream.Write(buffer, 0, buffer.Length);
                }

                newFileStream.Seek(0, SeekOrigin.Begin);

                using var baseSignatureStream = PrepareTestData(baseFileStream, signatureHash, rollingChecksum);

                // Act
                using var deltaStream = new FileStream(deltaFileName, FileMode.OpenOrCreate);
                using var patchedDataStream = new FileStream(patchedFileName, FileMode.OpenOrCreate);
                var deltaBuilder = new DeltaBuilder();
                deltaBuilder.BuildDelta(newFileStream, new SignatureReader(baseSignatureStream, null),
                    new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream)));
                deltaStream.Seek(0, SeekOrigin.Begin);

                var deltaApplier = new DeltaApplier();
                deltaApplier.Apply(baseFileStream, new BinaryDeltaReader(deltaStream, progressReporter),
                    patchedDataStream);
            }

            // Assert
            Assert.That(new FileInfo(newFileName).Length, Is.EqualTo(new FileInfo(patchedFileName).Length));
            Assert.That(CompareFilesByHash(newFileName, patchedFileName), Is.True);
            progressReporter.Received().Report(Arg.Any<ProgressReport>());
        }
        finally
        {
            File.Delete(baseFileName);
            File.Delete(newFileName);
            File.Delete(deltaFileName);
            File.Delete(patchedFileName);
        }
    }

    // The random-data test above produces a delta with no copy commands, because nothing
    // matches between the files. This test patches a file that mostly matches the basis file,
    // so the delta consists of copy commands whose offsets exceed int.MaxValue - protecting
    // the 64-bit offset arithmetic on the copy path.
    [Test]
    [TestCase(2200, "XXH64", "Adler32")]
    public void PatchingSyncXXHash_BigFileWithSmallChanges_PatchesUsingCopyCommands(int numberOfMBytes, string signatureHashingAlgorithm, string rollingChecksumAlgorithm)
    {
        // Arrange
        var signatureHash = SupportedAlgorithms.Hashing.Create(signatureHashingAlgorithm);
        var rollingChecksum = SupportedAlgorithms.Checksum.Create(rollingChecksumAlgorithm);

        var baseFileName = Path.GetTempFileName();
        var newFileName = Path.GetTempFileName();
        var deltaFileName = Path.GetTempFileName();
        var patchedFileName = Path.GetTempFileName();
        try
        {
            var progressReporter = Substitute.For<IProgress<ProgressReport>>();

            {
                var buffer = new byte[16384];
                var rng = new Random();

                // Both files share content except a few modified blocks, one of them close to
                // the end of the file, i.e. beyond the 2 GB boundary.
                var totalBlocks = numberOfMBytes * 64;
                var modifiedBlocks = new HashSet<int> { 100, totalBlocks / 2, totalBlocks - 2 };

                using var baseFileStream = new FileStream(baseFileName, FileMode.Create);
                using var newFileStream = new FileStream(newFileName, FileMode.Create);
                for (var i = 0; i < totalBlocks; i++)
                {
                    rng.NextBytes(buffer);
                    baseFileStream.Write(buffer, 0, buffer.Length);
                    if (modifiedBlocks.Contains(i))
                    {
                        rng.NextBytes(buffer);
                    }
                    newFileStream.Write(buffer, 0, buffer.Length);
                }

                baseFileStream.Seek(0, SeekOrigin.Begin);
                newFileStream.Seek(0, SeekOrigin.Begin);

                using var baseSignatureStream = PrepareTestData(baseFileStream, signatureHash, rollingChecksum);

                // Act
                using var deltaStream = new FileStream(deltaFileName, FileMode.OpenOrCreate);
                using var patchedDataStream = new FileStream(patchedFileName, FileMode.OpenOrCreate);
                var deltaBuilder = new DeltaBuilder();
                deltaBuilder.BuildDelta(newFileStream, new SignatureReader(baseSignatureStream, null),
                    new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream)));
                deltaStream.Seek(0, SeekOrigin.Begin);

                var deltaApplier = new DeltaApplier();
                deltaApplier.Apply(baseFileStream, new BinaryDeltaReader(deltaStream, progressReporter),
                    patchedDataStream);
            }

            // Assert - the delta must be far smaller than the file, proving the copy path was used
            Assert.That(new FileInfo(deltaFileName).Length, Is.LessThan(64 * 1024 * 1024));
            Assert.That(new FileInfo(newFileName).Length, Is.EqualTo(new FileInfo(patchedFileName).Length));
            Assert.That(CompareFilesByHash(newFileName, patchedFileName), Is.True);
            progressReporter.Received().Report(Arg.Any<ProgressReport>());
        }
        finally
        {
            File.Delete(baseFileName);
            File.Delete(newFileName);
            File.Delete(deltaFileName);
            File.Delete(patchedFileName);
        }
    }

    public static Stream PrepareTestData(Stream baseDataStream, IHashAlgorithm signatureHashingAlgorithm, IRollingChecksum rollingChecksumAlgorithm)
    {
        var baseSignatureStream = new MemoryStream();

        var signatureBuilder = new SignatureBuilder(signatureHashingAlgorithm, rollingChecksumAlgorithm);
        signatureBuilder.Build(baseDataStream, new SignatureWriter(baseSignatureStream));
        baseSignatureStream.Seek(0, SeekOrigin.Begin);
        return baseSignatureStream;
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

