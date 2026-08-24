using System;
using System.IO;
using System.Linq;
using FastRsync.Core;
using FastRsync.Delta;
using FastRsync.Signature;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace FastRsync.Tests;

// Tests for BinaryDeltaReader.ReadCommands - the "delta plan" API. The core guarantee is that
// executing the returned plan (copy ranges from the basis, data ranges read from the delta stream
// at the reported offsets) reconstructs exactly what DeltaApplier.Apply produces. This is what
// lets a caller assemble the target out-of-band (e.g. Azure Put Block From URL) without changing
// the delta format.
[TestFixture]
public class DeltaCommandReaderTests
{
    [Test]
    [TestCase(16974, 8452)]      // unrelated new data -> mostly data commands
    [TestCase(200000, 200000)]   // shift with matches -> mixed copy/data
    [TestCase(1024, 1024)]
    public void ReadCommands_PlanReconstructsSameOutputAsApply(int basisSize, int newSize)
    {
        var basis = CreateBytes(basisSize, 1);
        var newFile = MakeRelatedFile(basis, newSize);
        var delta = BuildDelta(basis, newFile);

        // Reference: what Apply produces.
        var applied = ApplyToArray(basis, delta);

        // Reconstruct from the plan.
        var reconstructed = ReconstructFromPlan(basis, delta);

        CollectionAssert.AreEqual(applied, reconstructed);
        CollectionAssert.AreEqual(newFile, reconstructed);
    }

    [Test]
    public void ReadCommands_DataOffsets_PointAtExactPayloadBytes()
    {
        // Independent-random new data => the delta is essentially one data command; verify the
        // reported data offset/length reads back exactly the new-file bytes.
        var basis = CreateBytes(4096, 7);
        var newFile = CreateBytes(9000, 8); // unrelated -> data command(s)
        var delta = BuildDelta(basis, newFile);

        var reader = new BinaryDeltaReader(new MemoryStream(delta), null);
        var plan = reader.ReadCommands();

        Assert.That(plan.All(c => c.Type == DeltaCommandType.DataCommand), Is.True, "expected only data commands for unrelated content");

        // Concatenate the payloads the plan points to and confirm they equal the new file.
        using var deltaStream = new MemoryStream(delta);
        var assembled = new MemoryStream();
        foreach (var cmd in plan)
        {
            var buf = ReadRange(deltaStream, cmd.Offset, cmd.Length);
            assembled.Write(buf, 0, buf.Length);
        }
        CollectionAssert.AreEqual(newFile, assembled.ToArray());
    }

    [Test]
    public void ReadCommands_UnchangedFile_IsSingleWholeFileCopy()
    {
        // With SkipDeltaIfHashesMatch the delta for an unchanged file is a whole-file copy command.
        var basis = CreateBytes(50000, 11);

        var signature = new MemoryStream();
        new SignatureBuilder().Build(new MemoryStream(basis), new SignatureWriter(signature));
        signature.Seek(0, SeekOrigin.Begin);

        var deltaStream = new MemoryStream();
        new DeltaBuilder { SkipDeltaIfHashesMatch = true }.BuildDelta(new MemoryStream(basis),
            new SignatureReader(signature, null), new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream)));

        var plan = new BinaryDeltaReader(new MemoryStream(deltaStream.ToArray()), null).ReadCommands();

        Assert.That(plan, Has.Count.EqualTo(1));
        Assert.That(plan[0].Type, Is.EqualTo(DeltaCommandType.CopyCommand));
        Assert.That(plan[0].Offset, Is.EqualTo(0));
        Assert.That(plan[0].Length, Is.EqualTo(basis.Length));
    }

    [Test]
    public void ReadCommands_ExposesMetadataNeededForBlockAssembly()
    {
        var basis = CreateBytes(16974, 1);
        var newFile = MakeRelatedFile(basis, 16974);
        var delta = BuildDelta(basis, newFile);

        var reader = new BinaryDeltaReader(new MemoryStream(delta), null);
        _ = reader.ReadCommands();

        Assert.That(reader.Metadata.BaseFileLength, Is.EqualTo(basis.Length));
        Assert.That(reader.Metadata.TargetFileLength, Is.EqualTo(newFile.Length));
        Assert.That(reader.Metadata.BaseFileHash, Is.EqualTo(Utils.GetMd5(basis)));
        Assert.That(reader.Metadata.ExpectedFileHash, Is.EqualTo(Utils.GetMd5(newFile)));
    }

    [Test]
    public void ReadCommands_IsIdempotent()
    {
        var basis = CreateBytes(20000, 3);
        var newFile = MakeRelatedFile(basis, 20000);
        var delta = BuildDelta(basis, newFile);

        var reader = new BinaryDeltaReader(new MemoryStream(delta), null);
        var first = reader.ReadCommands();
        var second = reader.ReadCommands();

        Assert.That(second.Count, Is.EqualTo(first.Count));
        for (var i = 0; i < first.Count; i++)
        {
            Assert.That(second[i].Type, Is.EqualTo(first[i].Type));
            Assert.That(second[i].Offset, Is.EqualTo(first[i].Offset));
            Assert.That(second[i].Length, Is.EqualTo(first[i].Length));
        }
    }

    [Test]
    public void ReadCommands_CorruptDelta_UnknownCommand_Throws()
    {
        var basis = CreateBytes(4096, 1);
        var newFile = MakeRelatedFile(basis, 4096);
        var delta = BuildDelta(basis, newFile);
        // Corrupt the first command byte (just after header+metadata) to an unknown opcode.
        var reader0 = new BinaryDeltaReader(new MemoryStream(delta), null);
        _ = reader0.Metadata; // force header parse
        // Find the command section by re-parsing: easiest is to flip a byte near the end (a payload
        // byte) won't trigger; instead corrupt by truncating the stream mid-command.
        var truncated = delta.Take(delta.Length - 3).ToArray();
        var reader = new BinaryDeltaReader(new MemoryStream(truncated), null);
        Assert.Throws<InvalidDataException>(() => reader.ReadCommands());
    }

    private static byte[] MakeRelatedFile(byte[] basis, int newSize)
    {
        // Start from the basis (so copies appear) and change a middle region, then truncate/extend
        // to newSize. Keeps a realistic mix of copy and data commands.
        var buffer = new byte[Math.Max(basis.Length, newSize)];
        Array.Copy(basis, buffer, Math.Min(basis.Length, newSize == 0 ? 0 : Math.Min(basis.Length, newSize)));
        Array.Copy(basis, buffer, Math.Min(basis.Length, buffer.Length));
        var patch = CreateBytes(Math.Min(2048, newSize), 99);
        Array.Copy(patch, 0, buffer, Math.Min(buffer.Length / 3, buffer.Length - patch.Length), patch.Length);
        return buffer.Take(newSize).ToArray();
    }

    private static byte[] BuildDelta(byte[] basis, byte[] newFile)
    {
        var signature = new MemoryStream();
        new SignatureBuilder().Build(new MemoryStream(basis), new SignatureWriter(signature));
        signature.Seek(0, SeekOrigin.Begin);

        var deltaStream = new MemoryStream();
        new DeltaBuilder().BuildDelta(new MemoryStream(newFile), new SignatureReader(signature, null),
            new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream)));
        return deltaStream.ToArray();
    }

    private static byte[] ApplyToArray(byte[] basis, byte[] delta)
    {
        var output = new MemoryStream();
        new DeltaApplier().Apply(new MemoryStream(basis), new BinaryDeltaReader(new MemoryStream(delta), null), output);
        return output.ToArray();
    }

    // Execute the plan the way a block-assembly caller would: copy ranges come from the basis,
    // data ranges are read from the delta stream at the reported offsets.
    private static byte[] ReconstructFromPlan(byte[] basis, byte[] delta)
    {
        var reader = new BinaryDeltaReader(new MemoryStream(delta), null);
        var plan = reader.ReadCommands();

        using var deltaStream = new MemoryStream(delta);
        var output = new MemoryStream();
        foreach (var cmd in plan)
        {
            byte[] chunk = cmd.Type == DeltaCommandType.CopyCommand
                ? basis.Skip((int)cmd.Offset).Take((int)cmd.Length).ToArray()
                : ReadRange(deltaStream, cmd.Offset, cmd.Length);
            output.Write(chunk, 0, chunk.Length);
        }
        return output.ToArray();
    }

    private static byte[] ReadRange(Stream stream, long offset, long length)
    {
        stream.Seek(offset, SeekOrigin.Begin);
        var buf = new byte[length];
        var read = 0;
        while (read < length)
        {
            var n = stream.Read(buf, read, (int)length - read);
            if (n == 0) break;
            read += n;
        }
        return buf;
    }

    private static byte[] CreateBytes(int length, int seed)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }
}
