using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FastRsync.Core;

namespace FastRsync.Delta
{
    public class BinaryDeltaWriter : IDeltaWriter
    {
        private readonly Stream deltaStream;
        private readonly BinaryWriter writer;
        private readonly int readWriteBufferSize;
        private byte[] dataCommandBuffer;

        public BinaryDeltaWriter(Stream stream, int readWriteBufferSize = 1024 * 1024)
        {
            deltaStream = stream;
            writer = new BinaryWriter(stream);
            this.readWriteBufferSize = readWriteBufferSize;
        }

        public void WriteMetadata(DeltaMetadata metadata)
        {
            writer.Write(FastRsyncBinaryFormat.DeltaHeader);
            writer.Write(FastRsyncBinaryFormat.Version);
#if NET7_0_OR_GREATER
            var metadataStr = JsonSerializer.Serialize(metadata, JsonContextCore.Default.DeltaMetadata);
#else
            var metadataStr = JsonSerializer.Serialize(metadata, JsonSerializationSettings.JsonSettings);
#endif
            writer.Write(metadataStr);
        }

        public void WriteCopyCommand(DataRange segment)
        {
            writer.Write(BinaryFormat.CopyCommand);
            writer.Write(segment.StartOffset);
            writer.Write(segment.Length);
        }

        public void WriteDataCommand(Stream source, long offset, long length)
        {
            writer.Write(BinaryFormat.DataCommand);
            writer.Write(length);

            var originalPosition = source.Position;
            try
            {
                source.Seek(offset, SeekOrigin.Begin);

                // Reused across data commands; deltas of heavily changed files write many of them.
                var bufferSize = (int)Math.Min(length, readWriteBufferSize);
                if (dataCommandBuffer == null || dataCommandBuffer.Length < bufferSize)
                    dataCommandBuffer = new byte[bufferSize];
                var buffer = dataCommandBuffer;

                int read;
                long soFar = 0;
                while ((read = source.Read(buffer, 0, (int)Math.Min(length - soFar, buffer.Length))) > 0)
                {
                    soFar += read;
                    writer.Write(buffer, 0, read);
                }
            }
            finally
            {
                source.Seek(originalPosition, SeekOrigin.Begin);
            }
        }

        public async Task WriteDataCommandAsync(Stream source, long offset, long length,
            CancellationToken cancellationToken)
        {
            writer.Write(BinaryFormat.DataCommand);
            writer.Write(length);

            var originalPosition = source.Position;
            try
            {
                source.Seek(offset, SeekOrigin.Begin);

                // Reused across data commands; deltas of heavily changed files write many of them.
                var bufferSize = (int)Math.Min(length, readWriteBufferSize);
                if (dataCommandBuffer == null || dataCommandBuffer.Length < bufferSize)
                    dataCommandBuffer = new byte[bufferSize];
                var buffer = dataCommandBuffer;

                int read;
                long soFar = 0;
                while ((read = await source
                           .ReadAsync(buffer, 0, (int)Math.Min(length - soFar, buffer.Length), cancellationToken)
                           .ConfigureAwait(false)) > 0)
                {
                    soFar += read;
                    await deltaStream.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                source.Seek(originalPosition, SeekOrigin.Begin);
            }
        }

        public void Finish()
        {
        }
    }
}