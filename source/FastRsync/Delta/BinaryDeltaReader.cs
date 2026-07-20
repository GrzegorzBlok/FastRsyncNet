using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FastRsync.Core;
using FastRsync.Diagnostics;
using FastRsync.Hash;
using FastRsync.Signature;

namespace FastRsync.Delta
{
    public class BinaryDeltaReader : IDeltaReader
    {
        // Buffers the many small reads of the metadata and command headers, which matters when
        // the delta is a network-backed stream (e.g. Azure Blob). Bulk data-command reads are
        // larger than this buffer and bypass it, going directly to the underlying stream.
        private const int StreamBufferSize = 64 * 1024;

        private readonly BinaryReader reader;
        private readonly IProgress<ProgressReport> progressReport;
        private byte[] expectedHash;
        private IHashAlgorithm hashAlgorithm;
        private readonly int readBufferSize;

        public BinaryDeltaReader(Stream stream, IProgress<ProgressReport> progressHandler,
            int readBufferSize = 4 * 1024 * 1024)
        {
            this.reader = new BinaryReader(new BufferedStream(stream, StreamBufferSize));
            this.progressReport = progressHandler;
            this.readBufferSize = readBufferSize;
        }

        private DeltaMetadata _metadata;
        private RsyncFormatType type;

        public DeltaMetadata Metadata
        {
            get
            {
                ReadMetadata();
                return _metadata;
            }
        }

        public RsyncFormatType Type
        {
            get
            {
                ReadMetadata();
                return type;
            }
        }

        public byte[] ExpectedHash
        {
            get
            {
                ReadMetadata();
                return expectedHash;
            }
        }

        public IHashAlgorithm HashAlgorithm
        {
            get
            {
                ReadMetadata();
                return hashAlgorithm;
            }
        }

        private void ReadMetadata()
        {
            if (_metadata != null)
                return;

            reader.BaseStream.Seek(0, SeekOrigin.Begin);

            var header = reader.ReadBytes(BinaryFormat.DeltaFormatHeaderLength);

            if (ByteArrayEquality.AreEqual(FastRsyncBinaryFormat.DeltaHeader, header))
            {
                ReadFastRsyncDeltaHeader();
                return;
            }

            if (ByteArrayEquality.AreEqual(OctoBinaryFormat.DeltaHeader, header))
            {
                ReadOctoDeltaHeader();
                return;
            }

            throw new InvalidDataException("The delta file uses a different file format than this program can handle.");
        }

        private void ReadFastRsyncDeltaHeader()
        {
            var version = reader.ReadByte();
            if (version != FastRsyncBinaryFormat.Version)
                throw new InvalidDataException("The delta file uses a newer file format than this program can handle.");

            var metadataStr = reader.ReadString();
#if NET7_0_OR_GREATER
            _metadata = JsonSerializer.Deserialize(metadataStr, JsonContextCore.Default.DeltaMetadata);
#else
            _metadata = JsonSerializer.Deserialize<DeltaMetadata>(metadataStr, JsonSerializationSettings.JsonSettings);
#endif

            if (_metadata == null)
                throw new InvalidDataException("The delta file appears to be corrupt; the metadata is missing.");
            if (string.IsNullOrEmpty(_metadata.ExpectedFileHash))
                throw new InvalidDataException("The delta file appears to be corrupt; the expected file hash is missing.");

            hashAlgorithm = SupportedAlgorithms.Hashing.Create(_metadata.HashAlgorithm);
            try
            {
                expectedHash = Convert.FromBase64String(_metadata.ExpectedFileHash);
            }
            catch (FormatException ex)
            {
                throw new InvalidDataException("The delta file appears to be corrupt; the expected file hash is not valid Base64.", ex);
            }

            type = RsyncFormatType.FastRsync;
        }

        private void ReadOctoDeltaHeader()
        {
            var version = reader.ReadByte();
            if (version != OctoBinaryFormat.Version)
                throw new InvalidDataException("The delta file uses a newer file format than this program can handle.");

            var hashAlgorithmName = reader.ReadString();
            hashAlgorithm = SupportedAlgorithms.Hashing.Create(hashAlgorithmName);

            var hashLength = reader.ReadInt32();
            var remainingBytes = reader.BaseStream.Length - reader.BaseStream.Position;
            if (hashLength < 0 || hashLength > remainingBytes)
                throw new InvalidDataException("The delta file appears to be corrupt; the expected hash length is invalid.");
            expectedHash = reader.ReadBytes(hashLength);
            if (expectedHash.Length != hashLength)
                throw new InvalidDataException("The delta file appears to be corrupt; the expected hash is truncated.");
            var endOfMeta = reader.ReadBytes(OctoBinaryFormat.EndOfMetadata.Length);
            if (!ByteArrayEquality.AreEqual(OctoBinaryFormat.EndOfMetadata, endOfMeta))
                throw new InvalidDataException("The delta file appears to be corrupt.");

            _metadata = new DeltaMetadata
            {
                HashAlgorithm = hashAlgorithmName,
                ExpectedFileHashAlgorithm = hashAlgorithmName,
                ExpectedFileHash = Convert.ToBase64String(expectedHash)
            };

            type = RsyncFormatType.Octodiff;
        }

        public void Apply(
            Action<byte[]> writeData,
            Action<long, long> copy)
        {
            var fileLength = reader.BaseStream.Length;

            ReadMetadata();

            while (reader.BaseStream.Position != fileLength)
            {
                var b = reader.ReadByte();

                progressReport?.Report(new ProgressReport
                {
                    Operation = ProgressOperationType.ApplyingDelta,
                    CurrentPosition = reader.BaseStream.Position,
                    Total = fileLength
                });

                if (b == BinaryFormat.CopyCommand)
                {
                    var start = reader.ReadInt64();
                    var length = reader.ReadInt64();
                    if (start < 0 || length < 0)
                        throw new InvalidDataException("The delta file appears to be corrupt; a copy command has a negative offset or length.");
                    copy(start, length);
                }
                else if (b == BinaryFormat.DataCommand)
                {
                    var length = reader.ReadInt64();
                    if (length < 0)
                        throw new InvalidDataException("The delta file appears to be corrupt; a data command has a negative length.");
                    long soFar = 0;
                    while (soFar < length)
                    {
                        var bytes = reader.ReadBytes((int)Math.Min(length - soFar, readBufferSize));
                        if (bytes.Length == 0)
                            throw new InvalidDataException("The delta file appears to be corrupt; a data command is truncated.");
                        soFar += bytes.Length;
                        writeData(bytes);
                    }
                }
                else
                {
                    throw new InvalidDataException($"The delta file appears to be corrupt; encountered an unknown command 0x{b:X2}.");
                }
            }
        }

        public Task ApplyAsync(Func<byte[], Task> writeData, Func<long, long, Task> copy) =>
            ApplyAsync(writeData, copy, CancellationToken.None);

        public async Task ApplyAsync(Func<byte[], Task> writeData, Func<long, long, Task> copy,
            CancellationToken cancellationToken)
        {
            var fileLength = reader.BaseStream.Length;

            ReadMetadata();

            var buffer = new byte[readBufferSize];

            while (reader.BaseStream.Position != fileLength)
            {
                var b = reader.ReadByte();

                progressReport?.Report(new ProgressReport
                {
                    Operation = ProgressOperationType.ApplyingDelta,
                    CurrentPosition = reader.BaseStream.Position,
                    Total = fileLength
                });

                if (b == BinaryFormat.CopyCommand)
                {
                    var start = reader.ReadInt64();
                    var length = reader.ReadInt64();
                    if (start < 0 || length < 0)
                        throw new InvalidDataException("The delta file appears to be corrupt; a copy command has a negative offset or length.");
                    await copy(start, length).ConfigureAwait(false);
                }
                else if (b == BinaryFormat.DataCommand)
                {
                    var length = reader.ReadInt64();
                    if (length < 0)
                        throw new InvalidDataException("The delta file appears to be corrupt; a data command has a negative length.");
                    long soFar = 0;
                    while (soFar < length)
                    {
                        var bytesRead = await reader.BaseStream
                            .ReadAsync(buffer, 0, (int)Math.Min(length - soFar, buffer.Length), cancellationToken)
                            .ConfigureAwait(false);
                        if (bytesRead == 0)
                            throw new InvalidDataException("The delta file appears to be corrupt; a data command is truncated.");
                        var bytes = buffer;
                        if (bytesRead != buffer.Length)
                        {
                            bytes = new byte[bytesRead];
                            Array.Copy(buffer, bytes, bytesRead);
                        }

                        soFar += bytes.Length;
                        await writeData(bytes).ConfigureAwait(false);
                    }
                }
                else
                {
                    throw new InvalidDataException($"The delta file appears to be corrupt; encountered an unknown command 0x{b:X2}.");
                }
            }
        }
    }
}