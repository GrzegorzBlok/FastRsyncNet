using System;
using System.IO;
using System.Text.Json;
using FastRsync.Core;
using FastRsync.Diagnostics;

namespace FastRsync.Signature
{
    public class SignatureReader : ISignatureReader
    {
        // Chunk records are 14-30 bytes each and are read individually, so a signature of a large
        // file causes millions of tiny reads. Buffering batches them into large reads, which
        // matters when the signature is a network-backed stream (e.g. Azure Blob).
        private const int StreamBufferSize = 64 * 1024;

        // Progress is throttled to once per this many chunks to avoid allocating a report
        // object for every chunk.
        private const int ProgressChunkInterval = 1024;

        private readonly IProgress<ProgressReport> report;
        private readonly BinaryReader reader;

        public SignatureReader(Stream stream, IProgress<ProgressReport> progressHandler)
        {
            this.report = progressHandler;
            this.reader = new BinaryReader(new BufferedStream(stream, StreamBufferSize));
        }

        public Signature ReadSignature()
        {
            Progress();
            var signature = ReadSignatureMetadata();
            ReadChunks(signature);
            return signature;
        }

        public Signature ReadSignatureMetadata()
        {
            var header = reader.ReadBytes(BinaryFormat.SignatureFormatHeaderLength);

            if (ByteArrayEquality.AreEqual(FastRsyncBinaryFormat.SignatureHeader, header))
            {
                return ReadFastRsyncSignatureHeader();
            }

            if (ByteArrayEquality.AreEqual(OctoBinaryFormat.SignatureHeader, header))
            {
                return ReadOctoSignatureHeader();
            }

            throw new InvalidDataException(
                "The signature file uses a different file format than this program can handle.");
        }

        private Signature ReadFastRsyncSignatureHeader()
        {
            var version = reader.ReadByte();
            if (version != FastRsyncBinaryFormat.Version)
                throw new InvalidDataException(
                    "The signature file uses a newer file format than this program can handle.");

            var metadataStr = reader.ReadString();
#if NET7_0_OR_GREATER
            var metadata = JsonSerializer.Deserialize(metadataStr, JsonContextCore.Default.SignatureMetadata);
#else
            var metadata =
 JsonSerializer.Deserialize<SignatureMetadata>(metadataStr, JsonSerializationSettings.JsonSettings);
#endif

            if (metadata == null)
                throw new InvalidDataException("The signature file appears to be corrupt; the metadata is missing.");

            var signature = new Signature(metadata, RsyncFormatType.FastRsync);

            return signature;
        }

        private Signature ReadOctoSignatureHeader()
        {
            var version = reader.ReadByte();
            if (version != OctoBinaryFormat.Version)
                throw new InvalidDataException(
                    "The signature file uses a newer file format than this program can handle.");

            var hashAlgorithmName = reader.ReadString();
            var rollingChecksumAlgorithmName = reader.ReadString();

            var endOfMeta = reader.ReadBytes(OctoBinaryFormat.EndOfMetadata.Length);
            if (!ByteArrayEquality.AreEqual(OctoBinaryFormat.EndOfMetadata, endOfMeta))
                throw new InvalidDataException("The signature file appears to be corrupt.");

            Progress();

            var hashAlgorithm = SupportedAlgorithms.Hashing.Create(hashAlgorithmName);
            var rollingChecksumAlgorithm = SupportedAlgorithms.Checksum.Create(rollingChecksumAlgorithmName);
            var signature = new Signature(new SignatureMetadata
            {
                ChunkHashAlgorithm = hashAlgorithm.Name,
                RollingChecksumAlgorithm = rollingChecksumAlgorithm.Name
            }, RsyncFormatType.Octodiff);

            return signature;
        }

        private void ReadChunks(Signature signature)
        {
            var expectedHashLength = signature.HashAlgorithm.HashLengthInBytes;
            long start = 0;

            var signatureLength = reader.BaseStream.Length;
            var remainingBytes = signatureLength - reader.BaseStream.Position;
            var signatureSize = sizeof(ushort) + sizeof(uint) + expectedHashLength;
            if (remainingBytes % signatureSize != 0)
                throw new InvalidDataException(
                    "The signature file appears to be corrupt; at least one chunk has data missing.");

            long chunkCount = 0;
            while (reader.BaseStream.Position < signatureLength - 1)
            {
                var length = reader.ReadInt16();
                if (length < 0)
                    throw new InvalidDataException("The signature file appears to be corrupt; a chunk has a negative length.");
                var checksum = reader.ReadUInt32();
                var chunkHash = reader.ReadBytes(expectedHashLength);
                if (chunkHash.Length != expectedHashLength)
                    throw new InvalidDataException("The signature file appears to be corrupt; a chunk hash is truncated.");

                signature.Chunks.Add(new ChunkSignature
                {
                    StartOffset = start,
                    Length = length,
                    RollingChecksum = checksum,
                    Hash = chunkHash
                });

                start += length;

                if (++chunkCount % ProgressChunkInterval == 0)
                    Progress();
            }

            Progress();
        }

        private void Progress()
        {
            report?.Report(new ProgressReport
            {
                Operation = ProgressOperationType.ReadingSignature,
                CurrentPosition = reader.BaseStream.Position,
                Total = reader.BaseStream.Length
            });
        }
    }
}