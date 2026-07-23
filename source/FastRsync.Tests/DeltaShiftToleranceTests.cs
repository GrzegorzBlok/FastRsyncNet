using System;
using System.IO;
using System.Threading.Tasks;
using FastRsync.Core;
using FastRsync.Delta;
using FastRsync.Signature;
using NUnit.Framework;

namespace FastRsync.Tests;

// Verifies the defining rsync property: inserting or deleting bytes (which shifts everything
// after the edit) is absorbed by the rolling checksum into a compact delta that still
// reconstructs the file exactly. The rest of the suite only covers independent-random data or
// same-length in-place edits, so this is the one place the shift-resync behaviour is exercised.
// Parametrized across the supported chunk-hash (XXH64, XXH3) and rolling-checksum (Adler32 v1,
// Adler32 v3) algorithms. Adapted from ByteSync's DeltaByteShiftToleranceTests.
[TestFixture]
public class DeltaShiftToleranceTests
{
    private const long MaxDeltaPercent = 5;
    private const int SmallSize = 2 * 1024 * 1024;
    // Larger than DeltaBuilder's 4MB default read buffer, so the shifted region spans the
    // buffer-reposition boundary in the scan loop.
    private const int LargeSize = 5 * 1024 * 1024;

    private const string XxHash64 = "XXH64";
    private const string XxHash3 = "XXH3";
    private const string Adler32 = "Adler32";
    private const string Adler32V3 = "Adler32V3";

    [Test, Combinatorial]
    public void BuildDelta_WithByteInsertionShift_ReconstructsSourceAndKeepsDeltaCompact(
        [Values(XxHash64, XxHash3)] string hashAlgorithm,
        [Values(Adler32, Adler32V3)] string rollingChecksumAlgorithm,
        [Values(SmallSize, LargeSize)] int basisSize)
    {
        var basis = CreateDeterministicBytes(basisSize, seed: 1);
        var newFile = InsertBytes(basis, basisSize / 2 + 123, CreateDeterministicBytes(4096, seed: 2));

        var (reconstructed, deltaLength) = BuildAndApplyDelta(basis, newFile, hashAlgorithm, rollingChecksumAlgorithm);

        AssertReconstructedAndCompact(newFile, reconstructed, deltaLength);
    }

    [Test, Combinatorial]
    public void BuildDelta_WithByteDeletionShift_ReconstructsSourceAndKeepsDeltaCompact(
        [Values(XxHash64, XxHash3)] string hashAlgorithm,
        [Values(Adler32, Adler32V3)] string rollingChecksumAlgorithm,
        [Values(SmallSize, LargeSize)] int basisSize)
    {
        var basis = CreateDeterministicBytes(basisSize, seed: 1);
        var newFile = RemoveBytes(basis, basisSize / 3 + 77, 8192);

        var (reconstructed, deltaLength) = BuildAndApplyDelta(basis, newFile, hashAlgorithm, rollingChecksumAlgorithm);

        AssertReconstructedAndCompact(newFile, reconstructed, deltaLength);
    }

    [Test, Combinatorial]
    public async Task BuildDeltaAsync_WithByteInsertionShift_ReconstructsSourceAndKeepsDeltaCompact(
        [Values(XxHash64, XxHash3)] string hashAlgorithm,
        [Values(Adler32, Adler32V3)] string rollingChecksumAlgorithm)
    {
        var basis = CreateDeterministicBytes(SmallSize, seed: 1);
        var newFile = InsertBytes(basis, 512 * 1024 + 123, CreateDeterministicBytes(4096, seed: 2));

        var (reconstructed, deltaLength) = await BuildAndApplyDeltaAsync(basis, newFile, hashAlgorithm, rollingChecksumAlgorithm);

        AssertReconstructedAndCompact(newFile, reconstructed, deltaLength);
    }

    [Test, Combinatorial]
    public async Task BuildDeltaAsync_WithByteDeletionShift_ReconstructsSourceAndKeepsDeltaCompact(
        [Values(XxHash64, XxHash3)] string hashAlgorithm,
        [Values(Adler32, Adler32V3)] string rollingChecksumAlgorithm)
    {
        var basis = CreateDeterministicBytes(SmallSize, seed: 1);
        var newFile = RemoveBytes(basis, 700 * 1024 + 77, 8192);

        var (reconstructed, deltaLength) = await BuildAndApplyDeltaAsync(basis, newFile, hashAlgorithm, rollingChecksumAlgorithm);

        AssertReconstructedAndCompact(newFile, reconstructed, deltaLength);
    }

    private static void AssertReconstructedAndCompact(byte[] expected, byte[] reconstructed, long deltaLength)
    {
        Assert.That(reconstructed.Length, Is.EqualTo(expected.Length));
        Assert.That(reconstructed.AsSpan().SequenceEqual(expected), Is.True, "reconstructed bytes differ from the source");
        // A shift handled correctly costs only the changed bytes plus a little overhead; if the
        // rolling re-sync were broken the delta would balloon toward the full file size.
        Assert.That(deltaLength, Is.LessThanOrEqualTo(expected.Length * MaxDeltaPercent / 100));
    }

    private static (byte[] reconstructed, long deltaLength) BuildAndApplyDelta(byte[] basisBytes, byte[] newBytes,
        string hashAlgorithm, string rollingChecksumAlgorithm)
    {
        var signatureBuilder = new SignatureBuilder(
            SupportedAlgorithms.Hashing.Create(hashAlgorithm),
            SupportedAlgorithms.Checksum.Create(rollingChecksumAlgorithm));

        var signatureStream = new MemoryStream();
        signatureBuilder.Build(new MemoryStream(basisBytes), new SignatureWriter(signatureStream));
        signatureStream.Seek(0, SeekOrigin.Begin);

        var deltaStream = new MemoryStream();
        new DeltaBuilder().BuildDelta(new MemoryStream(newBytes), new SignatureReader(signatureStream, null),
            new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream)));
        var deltaLength = deltaStream.Length;
        deltaStream.Seek(0, SeekOrigin.Begin);

        var reconstructed = new MemoryStream();
        new DeltaApplier().Apply(new MemoryStream(basisBytes), new BinaryDeltaReader(deltaStream, null), reconstructed);
        return (reconstructed.ToArray(), deltaLength);
    }

    private static async Task<(byte[] reconstructed, long deltaLength)> BuildAndApplyDeltaAsync(byte[] basisBytes, byte[] newBytes,
        string hashAlgorithm, string rollingChecksumAlgorithm)
    {
        var signatureBuilder = new SignatureBuilder(
            SupportedAlgorithms.Hashing.Create(hashAlgorithm),
            SupportedAlgorithms.Checksum.Create(rollingChecksumAlgorithm));

        var signatureStream = new MemoryStream();
        await signatureBuilder.BuildAsync(new MemoryStream(basisBytes), new SignatureWriter(signatureStream)).ConfigureAwait(false);
        signatureStream.Seek(0, SeekOrigin.Begin);

        var deltaStream = new MemoryStream();
        await new DeltaBuilder().BuildDeltaAsync(new MemoryStream(newBytes), new SignatureReader(signatureStream, null),
            new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream))).ConfigureAwait(false);
        var deltaLength = deltaStream.Length;
        deltaStream.Seek(0, SeekOrigin.Begin);

        var reconstructed = new MemoryStream();
        await new DeltaApplier().ApplyAsync(new MemoryStream(basisBytes), new BinaryDeltaReader(deltaStream, null), reconstructed).ConfigureAwait(false);
        return (reconstructed.ToArray(), deltaLength);
    }

    private static byte[] CreateDeterministicBytes(int length, int seed)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    private static byte[] InsertBytes(byte[] source, int offset, byte[] toInsert)
    {
        var result = new byte[source.Length + toInsert.Length];
        Array.Copy(source, 0, result, 0, offset);
        Array.Copy(toInsert, 0, result, offset, toInsert.Length);
        Array.Copy(source, offset, result, offset + toInsert.Length, source.Length - offset);
        return result;
    }

    private static byte[] RemoveBytes(byte[] source, int offset, int count)
    {
        var result = new byte[source.Length - count];
        Array.Copy(source, 0, result, 0, offset);
        Array.Copy(source, offset + count, result, offset, source.Length - offset - count);
        return result;
    }
}
