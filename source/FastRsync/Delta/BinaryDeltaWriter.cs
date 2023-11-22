using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FastRsync.Core;

namespace FastRsync.Delta
{
    public class BinaryDeltaWriter : IDeltaWriter
    {
        private readonly Stream deltaStream;
        private readonly BinaryWriter writer;
        private readonly int readWriteBufferSize;

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
            var metadataStr = JsonSerializer.Serialize(metadata, JsonSerializationSettings.JsonSettings);
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

                var buffer = new byte[(int)Math.Min(length, readWriteBufferSize)];

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

        public async Task WriteDataCommandAsync(Stream source, long offset, long length)
        {
            writer.Write(BinaryFormat.DataCommand);
            writer.Write(length);

            var originalPosition = source.Position;
            try
            {
                source.Seek(offset, SeekOrigin.Begin);

                var buffer = new byte[(int)Math.Min(length, readWriteBufferSize)];

                int read;
                long soFar = 0;
                while ((read = await source.ReadAsync(buffer, 0, (int)Math.Min(length - soFar, buffer.Length)).ConfigureAwait(false)) > 0)
                {
                    soFar += read;
                    await deltaStream.WriteAsync(buffer, 0, read).ConfigureAwait(false);
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