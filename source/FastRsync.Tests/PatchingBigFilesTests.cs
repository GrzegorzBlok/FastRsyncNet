using FastRsync.Core;
using FastRsync.Delta;
using FastRsync.Diagnostics;
using FastRsync.Signature;
using NSubstitute;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace FastRsync.Tests;

[TestFixture]
public class PatchingBigFilesTests
{
    [Test]
    [TestCase(2050, 2790)]
    public void PatchingSyncXXHash_BigFile(int baseNumberOfMBytes, int newNumberOfMBytes)
    {
        // Arrange
        var baseFileName = Path.GetTempFileName();
        var newFileName = Path.GetTempFileName();
        try
        {
            var deltaFileName = Path.GetTempFileName();
            var patchedFileName = Path.GetTempFileName();
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
                for (var i = 0; i < baseNumberOfMBytes * 64; i++)
                {
                    rng.NextBytes(buffer);
                    newFileStream.Write(buffer, 0, buffer.Length);
                }

                newFileStream.Seek(0, SeekOrigin.Begin);

                using var baseSignatureStream = PrepareTestData(baseFileStream);

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
        catch (Exception e)
        {
            Assert.Fail();
        }
        finally
        {
            File.Delete(baseFileName);
            File.Delete(newFileName);
        }
    }

    public static Stream PrepareTestData(Stream baseDataStream)
    {
        var baseSignatureStream = new MemoryStream();

        var signatureBuilder = new SignatureBuilder();
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

