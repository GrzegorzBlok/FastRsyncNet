using System;
using System.Collections.Generic;
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
        // Buffer size used when reading the delta commands; see Apply.
        private const int StreamBufferSize = 64 * 1024;

        private readonly BinaryReader reader;
        private readonly IProgress<ProgressReport> progressReport;
        private byte[] expectedHash;
        private IHashAlgorithm hashAlgorithm;
        private readonly int readBufferSize;

        public BinaryDeltaReader(Stream stream, IProgress<ProgressReport> progressHandler,
            int readBufferSize = 4 * 1024 * 1024)
        {
            // The reader must operate directly on the caller's stream (no read-ahead
            // buffering here) so that repositioning the underlying stream between calls
            // remains visible to the reader, as it was in earlier versions.
            this.reader = new BinaryReader(stream);
            this.progressReport = progressHandler;
            this.readBufferSize = readBufferSize;
        }

        /// <summary>
        /// When enabled (default), the async apply buffer is rented from the shared array pool
        /// instead of allocated per call. Reduces GC pressure across many operations. Does not
        /// affect output.
        /// </summary>
        public bool UseBufferPool { get; set; } = true;

        private DeltaMetadata _metadata;
        private RsyncFormatType type;
        // Stream position where the command section begins (right after the metadata), recorded
        // when the header is parsed. Used by ReadCommands to reposition reliably.
        private long commandsStartPosition = -1;

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
            commandsStartPosition = reader.BaseStream.Position;
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
            commandsStartPosition = reader.BaseStream.Position;
        }

        /// <summary>
        /// Parses the delta's command stream and returns the ordered plan of copy/data operations
        /// without applying it. For each <see cref="DeltaCommandType.DataCommand"/> command the returned
        /// <see cref="DeltaCommand.Offset"/> is the absolute offset of the payload within the delta
        /// stream, so callers can read the new bytes directly from the delta (e.g. to assemble an
        /// Azure block blob with Put Block From URL). Does not read the data payloads.
        /// The delta stream must be seekable. Idempotent - may be called more than once.
        /// </summary>
        public IReadOnlyList<DeltaCommand> ReadCommands()
        {
            ReadMetadata();

            var fileLength = reader.BaseStream.Length;
            reader.BaseStream.Seek(commandsStartPosition, SeekOrigin.Begin);

            var commands = new List<DeltaCommand>();
            while (reader.BaseStream.Position != fileLength)
            {
                var b = reader.ReadByte();
                if (b == (byte)DeltaCommandType.CopyCommand)
                {
                    var start = reader.ReadInt64();
                    var length = reader.ReadInt64();
                    if (start < 0 || length < 0)
                        throw new InvalidDataException("The delta file appears to be corrupt; a copy command has a negative offset or length.");
                    commands.Add(new DeltaCommand(DeltaCommandType.CopyCommand, start, length));
                }
                else if (b == (byte)DeltaCommandType.DataCommand)
                {
                    var length = reader.ReadInt64();
                    if (length < 0)
                        throw new InvalidDataException("The delta file appears to be corrupt; a data command has a negative length.");
                    var payloadOffset = reader.BaseStream.Position;
                    if (payloadOffset + length > fileLength)
                        throw new InvalidDataException("The delta file appears to be corrupt; a data command is truncated.");
                    commands.Add(new DeltaCommand(DeltaCommandType.DataCommand, payloadOffset, length));
                    // Skip the payload rather than reading it.
                    reader.BaseStream.Seek(length, SeekOrigin.Current);
                }
                else
                {
                    throw new InvalidDataException($"The delta file appears to be corrupt; encountered an unknown command 0x{b:X2}.");
                }
            }

            return commands;
        }

        public void Apply(
            Action<byte[]> writeData,
            Action<long, long> copy)
        {
            var fileLength = reader.BaseStream.Length;

            ReadMetadata();

            // Command headers are tiny reads while data payloads are larger than the buffer
            // and bypass it, so buffering here batches the small reads (a win for
            // network-backed delta streams). The buffering is scoped to this method because
            // the command section is always consumed to the end of the stream, which keeps
            // it invisible to callers.
            var commandReader = new BinaryReader(new BufferedStream(reader.BaseStream, StreamBufferSize));

            while (commandReader.BaseStream.Position != fileLength)
            {
                var b = commandReader.ReadByte();

                progressReport?.Report(new ProgressReport
                {
                    Operation = ProgressOperationType.ApplyingDelta,
                    CurrentPosition = commandReader.BaseStream.Position,
                    Total = fileLength
                });

                if (b == (byte)DeltaCommandType.CopyCommand)
                {
                    var start = commandReader.ReadInt64();
                    var length = commandReader.ReadInt64();
                    if (start < 0 || length < 0)
                        throw new InvalidDataException("The delta file appears to be corrupt; a copy command has a negative offset or length.");
                    copy(start, length);
                }
                else if (b == (byte)DeltaCommandType.DataCommand)
                {
                    var length = commandReader.ReadInt64();
                    if (length < 0)
                        throw new InvalidDataException("The delta file appears to be corrupt; a data command has a negative length.");
                    long soFar = 0;
                    while (soFar < length)
                    {
                        var bytes = commandReader.ReadBytes((int)Math.Min(length - soFar, readBufferSize));
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

            var rented = PooledBuffer.Rent(readBufferSize, UseBufferPool);
            var buffer = rented.Array;
            try
            {
                // See the comment in Apply: buffering is scoped to the command section.
                var commandReader = new BinaryReader(new BufferedStream(reader.BaseStream, StreamBufferSize));

                while (commandReader.BaseStream.Position != fileLength)
                {
                    var b = commandReader.ReadByte();

                    progressReport?.Report(new ProgressReport
                    {
                        Operation = ProgressOperationType.ApplyingDelta,
                        CurrentPosition = commandReader.BaseStream.Position,
                        Total = fileLength
                    });

                    if (b == (byte)DeltaCommandType.CopyCommand)
                    {
                        var start = commandReader.ReadInt64();
                        var length = commandReader.ReadInt64();
                        if (start < 0 || length < 0)
                            throw new InvalidDataException("The delta file appears to be corrupt; a copy command has a negative offset or length.");
                        await copy(start, length).ConfigureAwait(false);
                    }
                    else if (b == (byte)DeltaCommandType.DataCommand)
                    {
                        var length = commandReader.ReadInt64();
                        if (length < 0)
                            throw new InvalidDataException("The delta file appears to be corrupt; a data command has a negative length.");
                        long soFar = 0;
                        while (soFar < length)
                        {
                            var bytesRead = await commandReader.BaseStream
                                .ReadAsync(buffer, 0, (int)Math.Min(length - soFar, readBufferSize), cancellationToken)
                                .ConfigureAwait(false);
                            if (bytesRead == 0)
                                throw new InvalidDataException("The delta file appears to be corrupt; a data command is truncated.");
                            var bytes = buffer;
                            // A pooled buffer is larger than readBufferSize, so this is always true when
                            // pooling and a correctly-sized copy is handed to writeData.
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
            finally
            {
                rented.Dispose();
            }
        }
    }
}