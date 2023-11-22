using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FastRsync.Core;
using FastRsync.Delta;
using FastRsync.Diagnostics;
using FastRsync.Hash;
using FastRsync.Signature;
using Newtonsoft.Json;

namespace FastRsync.Tests.FastRsyncLegacy
{
    internal class BinaryFormat
    {
        public const int SignatureFormatHeaderLength = 7; // OCTOSIG or FRSNCSG
        public const int DeltaFormatHeaderLength = 9; // OCTODELTA or FRSNCDLTA

        public const byte CopyCommand = 0x60;
        public const byte DataCommand = 0x80;
    }

    internal class OctoBinaryFormat
    {
        public static readonly byte[] SignatureHeader = Encoding.ASCII.GetBytes("OCTOSIG");
        public static readonly byte[] DeltaHeader = Encoding.ASCII.GetBytes("OCTODELTA");
        public static readonly byte[] EndOfMetadata = Encoding.ASCII.GetBytes(">>>");

        public const byte Version = 0x01;
    }

    internal class FastRsyncBinaryFormat
    {
        public static readonly byte[] SignatureHeader = Encoding.ASCII.GetBytes("FRSNCSG");
        public static readonly byte[] DeltaHeader = Encoding.ASCII.GetBytes("FRSNCDLTA");

        public const byte Version = 0x01;
    }

    public sealed class ProgressReport
    {
        public ProgressOperationType Operation { get; internal set; }

        public long CurrentPosition { get; internal set; }

        public long Total { get; internal set; }
    }

    internal class BinaryDeltaReaderLegacy : IDeltaReaderLegacy
    {
        private readonly BinaryReader reader;
        private readonly IProgress<ProgressReport> progressReport;
        private byte[] expectedHash;
        private IHashAlgorithm hashAlgorithm;
        private readonly int readBufferSize;

        public BinaryDeltaReaderLegacy(Stream stream, IProgress<ProgressReport> progressHandler, int readBufferSize = 4 * 1024 * 1024)
        {
            this.reader = new BinaryReader(stream);
            this.progressReport = progressHandler;
            this.readBufferSize = readBufferSize;
        }

        private DeltaMetadataLegacy _metadata;
        public DeltaMetadataLegacy Metadata
        {
            get
            {
                ReadMetadata();
                return _metadata;
            }
        }

        public RsyncFormatType Type { get; private set; }

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

            if (StructuralComparisons.StructuralEqualityComparer.Equals(FastRsyncBinaryFormat.DeltaHeader, header))
            {
                ReadFastRsyncDeltaHeader();
                return;
            }

            if (StructuralComparisons.StructuralEqualityComparer.Equals(OctoBinaryFormat.DeltaHeader, header))
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
            _metadata = JsonConvert.DeserializeObject<DeltaMetadataLegacy>(metadataStr, NewtonsoftJsonSerializationSettings.JsonSettings);

            hashAlgorithm = SupportedAlgorithms.Hashing.Create(_metadata.HashAlgorithm);
            expectedHash = Convert.FromBase64String(_metadata.ExpectedFileHash);

            Type = RsyncFormatType.FastRsync;
        }

        private void ReadOctoDeltaHeader()
        {
            var version = reader.ReadByte();
            if (version != OctoBinaryFormat.Version)
                throw new InvalidDataException("The delta file uses a newer file format than this program can handle.");

            var hashAlgorithmName = reader.ReadString();
            hashAlgorithm = SupportedAlgorithms.Hashing.Create(hashAlgorithmName);

            var hashLength = reader.ReadInt32();
            expectedHash = reader.ReadBytes(hashLength);
            var endOfMeta = reader.ReadBytes(OctoBinaryFormat.EndOfMetadata.Length);
            if (!StructuralComparisons.StructuralEqualityComparer.Equals(OctoBinaryFormat.EndOfMetadata, endOfMeta))
                throw new InvalidDataException("The delta file appears to be corrupt.");

            _metadata = new DeltaMetadataLegacy
            {
                HashAlgorithm = hashAlgorithmName,
                ExpectedFileHashAlgorithm = hashAlgorithmName,
                ExpectedFileHash = Convert.ToBase64String(expectedHash)
            };

            Type = RsyncFormatType.Octodiff;
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
                    copy(start, length);
                }
                else if (b == BinaryFormat.DataCommand)
                {
                    var length = reader.ReadInt64();
                    long soFar = 0;
                    while (soFar < length)
                    {
                        var bytes = reader.ReadBytes((int)Math.Min(length - soFar, readBufferSize));
                        soFar += bytes.Length;
                        writeData(bytes);
                    }
                }
            }
        }

        public async Task ApplyAsync(
            Func<byte[], Task> writeData,
            Func<long, long, Task> copy)
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
                    await copy(start, length).ConfigureAwait(false);
                }
                else if (b == BinaryFormat.DataCommand)
                {
                    var length = reader.ReadInt64();
                    long soFar = 0;
                    while (soFar < length)
                    {
                        var bytesRead = await reader.BaseStream.ReadAsync(buffer, 0, (int)Math.Min(length - soFar, buffer.Length)).ConfigureAwait(false);
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
            }
        }
    }
}
