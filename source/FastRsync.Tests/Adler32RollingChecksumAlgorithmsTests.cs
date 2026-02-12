using FastRsync.Hash;
using NSubstitute;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FastRsync.Core;
using FastRsync.Delta;
using FastRsync.Diagnostics;

namespace FastRsync.Tests;

[TestFixture]
public class Adler32RollingChecksumAlgorithmsTests
{
    public static IRollingChecksum[] RollingChecksumAlgorithms =>
        typeof(IRollingChecksum).Assembly.GetTypes()
            .Where(t => typeof(IRollingChecksum).IsAssignableFrom(t)
                        && t is {IsClass: true, IsAbstract:false}
                        && t.GetConstructor(Type.EmptyTypes) != null
                        && t.GetCustomAttribute<ObsoleteAttribute>() == null)
            .Select(t => (IRollingChecksum)Activator.CreateInstance(t))
            .ToArray();

    private static readonly int[] Sizes = [1024 * 1024 / 2, 10 * 1024 * 1024, 20 * 1024 * 1024];
    private static readonly int[] ModificationPercentages = [1, 3, 5, 10];

    [Test]
    [TestCaseSource(nameof(RollingChecksumAlgorithms))]
    public void EveryRollingChecksumAlgorithmCalculatesRotateCorrectly(IRollingChecksum rollingChecksumAlgorithmInstance)
    {
        // Arrange
        const int len = 8 * 1024;
        const int windowSize = len - 1;
        var bytes = new byte[len];
        new Random().NextBytes(bytes);

        // Act
        var rotatedChecksum = rollingChecksumAlgorithmInstance.Rotate(
            rollingChecksumAlgorithmInstance.Calculate(bytes, 0, windowSize), 
            bytes[0], 
            bytes[^1], 
            windowSize);
        var checksum = rollingChecksumAlgorithmInstance.Calculate(bytes, 1, windowSize);

        // Assert
        Assert.That(rotatedChecksum, Is.EqualTo(checksum));
    }

    public static IEnumerable<TestCaseData> SignDeltaAndApplyPatchTestCases()
    {
        return
            from size in Sizes
            from modPercentage in ModificationPercentages
            from algo in RollingChecksumAlgorithms
            select new TestCaseData(size, modPercentage, algo);
    }

    [Test]
    [TestCaseSource(nameof(SignDeltaAndApplyPatchTestCases))]
    public async Task SignDeltaAndApplyPatchWithEveryRollingChecksumAlgorithm(int size, int modificationPercentage, IRollingChecksum algorithm)
    {
        //Arrange
        var bytesToPatchFrom = GetRandomBytes(size);
        var targetBytes = ModifyBytes(bytesToPatchFrom, modificationPercentage);
        //Act
        var progressReporter = Substitute.For<IProgress<ProgressReport>>();
        //generate signature of target bytes
        var signatureStream = new MemoryStream();
        var signatureBuilder = new Signature.SignatureBuilder(SupportedAlgorithms.Hashing.XxHash(), algorithm);
        await signatureBuilder.BuildAsync(new MemoryStream(bytesToPatchFrom), new Signature.SignatureWriter(signatureStream));
        signatureStream.Seek(0, SeekOrigin.Begin);

        //generate delta of base bytes and target bytes
        var deltaStream = new MemoryStream();
        var deltaBuilder = new DeltaBuilder();
        await deltaBuilder.BuildDeltaAsync(new MemoryStream(targetBytes), new Signature.SignatureReader(signatureStream, progressReporter), new BinaryDeltaWriter(deltaStream));
        deltaStream.Seek(0, SeekOrigin.Begin);

        //apply delta to base bytes 
        var patchedDataStream = new MemoryStream();
        var deltaApplier = new DeltaApplier();
        await deltaApplier.ApplyAsync(new MemoryStream(bytesToPatchFrom), new BinaryDeltaReader(deltaStream, progressReporter), patchedDataStream);
        patchedDataStream.Seek(0, SeekOrigin.Begin);

        //Assert
        CollectionAssert.AreEqual(targetBytes, patchedDataStream.ToArray());
        //make sure that the delta is reasonably small, allowing for some overhead but ensuring that the rolling checksum is effective
        Assert.That(deltaStream.Length, Is.LessThanOrEqualTo(2.5 * modificationPercentage / 100.0 * bytesToPatchFrom.Length));
    }

    private static byte[] ModifyBytes(ReadOnlySpan<byte> input, int percentage)
    {
        Assert.That(percentage, Is.InRange(0, 100));
        var length = input.Length;
        var modified = new byte[length];
        input.CopyTo(modified);

        var bytesToModify = (int)(length * (percentage / 100.0));
        if (bytesToModify == 0) return modified;

        var random = new Random();
        var maxStartPosition = length - bytesToModify;
        var startIndex = maxStartPosition > 0 ? random.Next(maxStartPosition + 1) : 0;

        // Modify bytes in a continuous range
        for (var i = startIndex; i < startIndex + bytesToModify && i < length; i++)
        {
            modified[i] = (byte)(modified[i] + 1);
        }

        return modified;
    }

    private static byte[] GetRandomBytes(int size)
    {
        var data = new byte[size];
        new Random().NextBytes(data);
        return data;
    }
}