using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FastRsync.Core;
using FastRsync.Delta;
using FastRsync.Signature;
using NUnit.Framework;

namespace FastRsync.Tests;

[TestFixture]
public class BufferPoolConcurrencyTests
{
    private const int PairCount = 8;
    private const int FileSize = 1024 * 1024;

    // Distinct basis/new pairs, each with its precomputed signature, delta and expected output.
    private (byte[] Basis, byte[] NewFile, byte[] Delta)[] pairs;

    [OneTimeSetUp]
    public void SetUp()
    {
        pairs = Enumerable.Range(0, PairCount).Select(i =>
        {
            // Distinct content per pair, so any cross-operation buffer aliasing yields a mismatch.
            var basis = CreateBytes(FileSize, seed: 1000 + i);
            var newFile = (byte[])basis.Clone();
            // A small change so the delta contains copy commands that read the basis through the
            // pooled apply buffer (a pure data delta would not exercise the copy path).
            var patch = CreateBytes(8192, seed: 5000 + i);
            Array.Copy(patch, 0, newFile, FileSize / 2, patch.Length);

            var delta = BuildDelta(basis, newFile);
            return (basis, newFile, delta);
        }).ToArray();
    }

    [Test]
    public void ConcurrentApply_WithBufferPool_ProducesCorrectResults()
    {
        const int operations = 512;
        var errors = new ConcurrentBag<string>();

        Parallel.For(0, operations,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 },
            op =>
            {
                var (basis, newFile, delta) = pairs[op % PairCount];
                var output = new MemoryStream();
                // Each operation uses its own applier (pooling on by default) and its own streams.
                new DeltaApplier().Apply(new MemoryStream(basis), new BinaryDeltaReader(new MemoryStream(delta), null), output);
                if (!output.ToArray().AsSpan().SequenceEqual(newFile))
                    errors.Add($"apply op {op} (pair {op % PairCount}) produced incorrect output");
            });

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
    }

    [Test]
    public async Task ConcurrentApplyAsync_WithBufferPool_ProducesCorrectResults()
    {
        const int operations = 512;
        var errors = new ConcurrentBag<string>();

        var tasks = Enumerable.Range(0, operations).Select(op => Task.Run(async () =>
        {
            var (basis, newFile, delta) = pairs[op % PairCount];
            var output = new MemoryStream();
            await new DeltaApplier().ApplyAsync(new MemoryStream(basis), new BinaryDeltaReader(new MemoryStream(delta), null), output).ConfigureAwait(false);
            if (!output.ToArray().AsSpan().SequenceEqual(newFile))
                errors.Add($"applyAsync op {op} (pair {op % PairCount}) produced incorrect output");
        }));

        await Task.WhenAll(tasks).ConfigureAwait(false);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
    }

    [Test]
    public void ConcurrentBuildDelta_WithBufferPool_ProducesConsistentDeltas()
    {
        // Building the same delta must be deterministic; under concurrency every build must equal
        // the single-threaded reference. A shared scan buffer aliased across builds would corrupt
        // the matching and change the delta bytes.
        const int operations = 256;
        var errors = new ConcurrentBag<string>();

        Parallel.For(0, operations,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 },
            op =>
            {
                var (basis, newFile, expectedDelta) = pairs[op % PairCount];
                var delta = BuildDelta(basis, newFile);
                if (!delta.AsSpan().SequenceEqual(expectedDelta))
                    errors.Add($"build op {op} (pair {op % PairCount}) produced a different delta");
            });

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
    }

    [Test]
    public void ConcurrentMixedBuildAndApply_WithBufferPool_ProducesCorrectResults()
    {
        // Interleave builders and appliers so the shared pool is rented/returned from both sides
        // simultaneously.
        const int operations = 512;
        var errors = new ConcurrentBag<string>();

        Parallel.For(0, operations,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 },
            op =>
            {
                var (basis, newFile, expectedDelta) = pairs[op % PairCount];
                if (op % 2 == 0)
                {
                    var delta = BuildDelta(basis, newFile);
                    if (!delta.AsSpan().SequenceEqual(expectedDelta))
                        errors.Add($"mixed build op {op} produced a different delta");
                }
                else
                {
                    var output = new MemoryStream();
                    new DeltaApplier().Apply(new MemoryStream(basis), new BinaryDeltaReader(new MemoryStream(expectedDelta), null), output);
                    if (!output.ToArray().AsSpan().SequenceEqual(newFile))
                        errors.Add($"mixed apply op {op} produced incorrect output");
                }
            });

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
    }

    private static byte[] BuildDelta(byte[] basis, byte[] newFile)
    {
        var signatureStream = new MemoryStream();
        new SignatureBuilder().Build(new MemoryStream(basis), new SignatureWriter(signatureStream));
        signatureStream.Seek(0, SeekOrigin.Begin);

        var deltaStream = new MemoryStream();
        new DeltaBuilder().BuildDelta(new MemoryStream(newFile), new SignatureReader(signatureStream, null),
            new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream)));
        return deltaStream.ToArray();
    }

    private static byte[] CreateBytes(int length, int seed)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }
}
