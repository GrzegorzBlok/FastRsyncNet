using System;
using System.Collections.Generic;
using System.IO;
using FastRsync.Core;
using FastRsync.Delta;
using FastRsync.Signature;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace FastRsync.Tests;

[TestFixture]
public class DeltaApplierTests
{
    [Test]
    public void Apply_FreshSeekableOutput_PreallocatesToTargetFileLength()
    {
        // Arrange
        const int newDataNumberOfBytes = 8452;
        var (baseDataStream, baseSignatureStream, newData, newDataStream) = Utils.PrepareTestData(16974, newDataNumberOfBytes, SignatureBuilder.DefaultChunkSize);
        var deltaStream = BuildDelta(baseSignatureStream, newDataStream);

        var outputStream = new SetLengthRecordingStream();

        // Act
        new DeltaApplier().Apply(baseDataStream, new BinaryDeltaReader(deltaStream, null), outputStream);

        // Assert - output was preallocated to the target size declared in the delta metadata
        Assert.That(outputStream.SetLengthCalls, Does.Contain((long)newDataNumberOfBytes));
        CollectionAssert.AreEqual(newData, outputStream.ToArray());
    }

    [Test]
    public void Apply_NonSeekableOutput_SkipHashCheck_PatchesFile()
    {
        // Arrange - simulates a network output stream (e.g. an upload stream): write-only,
        // non-seekable. The hash check requires seeking the output, so it is skipped.
        var (baseDataStream, baseSignatureStream, newData, newDataStream) = Utils.PrepareTestData(16974, 8452, SignatureBuilder.DefaultChunkSize);
        var deltaStream = BuildDelta(baseSignatureStream, newDataStream);

        var innerStream = new MemoryStream();
        var outputStream = new WriteOnlyStream(innerStream);

        // Act
        var deltaApplier = new DeltaApplier { SkipHashCheck = true };
        deltaApplier.Apply(baseDataStream, new BinaryDeltaReader(deltaStream, null), outputStream);

        // Assert
        CollectionAssert.AreEqual(newData, innerStream.ToArray());
    }

    [Test]
    public void Apply_OutputWithExistingContent_DoesNotPreallocate()
    {
        // Arrange - a non-empty output stream must keep today's behavior: no SetLength calls
        var (baseDataStream, baseSignatureStream, _, newDataStream) = Utils.PrepareTestData(16974, 8452, SignatureBuilder.DefaultChunkSize);
        var deltaStream = BuildDelta(baseSignatureStream, newDataStream);

        var outputStream = new SetLengthRecordingStream();
        outputStream.Write(new byte[100], 0, 100);

        // Act - the patched result will not match the hash (leftover content), so skip the check
        var deltaApplier = new DeltaApplier { SkipHashCheck = true };
        deltaApplier.Apply(baseDataStream, new BinaryDeltaReader(deltaStream, null), outputStream);

        // Assert
        Assert.That(outputStream.SetLengthCalls, Is.Empty);
    }

    [Test]
    [TestCase(true)]
    [TestCase(false)]
    public void Apply_WithAndWithoutBufferPool_ProducesCorrectOutput(bool useBufferPool)
    {
        // Arrange
        var (baseDataStream, baseSignatureStream, newData, newDataStream) = Utils.PrepareTestData(16974, 8452, SignatureBuilder.DefaultChunkSize);
        var deltaStream = BuildDelta(baseSignatureStream, newDataStream);

        // Act
        var outputStream = new MemoryStream();
        var deltaApplier = new DeltaApplier { UseBufferPool = useBufferPool };
        deltaApplier.Apply(baseDataStream, new BinaryDeltaReader(deltaStream, null), outputStream);

        // Assert - pooling must not affect the reconstructed file
        CollectionAssert.AreEqual(newData, outputStream.ToArray());
    }

    private static MemoryStream BuildDelta(MemoryStream baseSignatureStream, MemoryStream newDataStream)
    {
        var deltaStream = new MemoryStream();
        new DeltaBuilder().BuildDelta(newDataStream, new SignatureReader(baseSignatureStream, null), new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream)));
        deltaStream.Seek(0, SeekOrigin.Begin);
        return deltaStream;
    }

    private class SetLengthRecordingStream : MemoryStream
    {
        public List<long> SetLengthCalls { get; } = new List<long>();

        public override void SetLength(long value)
        {
            SetLengthCalls.Add(value);
            base.SetLength(value);
        }
    }

    private class WriteOnlyStream : Stream
    {
        private readonly Stream inner;

        public WriteOnlyStream(Stream inner)
        {
            this.inner = inner;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
