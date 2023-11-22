using System;
using System.Collections;
using System.IO;
using System.Text.Json;
using FastRsync.Core;
using FastRsync.Diagnostics;

namespace FastRsync.Signature
{
    public class SignatureReader : ISignatureReader
    {
        private readonly IProgress<ProgressReport> report;
        private readonly BinaryReader reader;

        public SignatureReader(Stream stream, IProgress<ProgressReport> progressHandler)
        {
            this.report = progressHandler;
            this.reader = new BinaryReader(stream);
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

            if (StructuralComparisons.StructuralEqualityComparer.Equals(FastRsyncBinaryFormat.SignatureHeader,
                header))
            {
                return ReadFastRsyncSignatureHeader();
            }

            if (StructuralComparisons.StructuralEqualityComparer.Equals(OctoBinaryFormat.SignatureHeader, header))
            {
                return ReadOctoSignatureHeader();
            }

            throw new InvalidDataException("The signature file uses a different file format than this program can handle.");
        }

        private Signature ReadFastRsyncSignatureHeader()
        {
            var version = reader.ReadByte();
            if (version != FastRsyncBinaryFormat.Version)
                throw new InvalidDataException("The signature file uses a newer file format than this program can handle.");

            var metadataStr = reader.ReadString();
            var metadata = JsonSerializer.Deserialize<SignatureMetadata>(metadataStr, JsonSerializationSettings.JsonSettings);

            var signature = new Signature(metadata, RsyncFormatType.FastRsync);

            return signature;
        }

        private Signature ReadOctoSignatureHeader()
        {
            var version = reader.ReadByte();
            if (version != OctoBinaryFormat.Version)
                throw new InvalidDataException("The signature file uses a newer file format than this program can handle.");

            var hashAlgorithm = reader.ReadString();
            var rollingChecksumAlgorithm = reader.ReadString();

            var endOfMeta = reader.ReadBytes(OctoBinaryFormat.EndOfMetadata.Length);
            if (!StructuralComparisons.StructuralEqualityComparer.Equals(OctoBinaryFormat.EndOfMetadata, endOfMeta)) 
                throw new InvalidDataException("The signature file appears to be corrupt.");

            Progress();

            var hashAlgo = SupportedAlgorithms.Hashing.Create(hashAlgorithm);
            var rollingChecksumAlgo = SupportedAlgorithms.Checksum.Create(rollingChecksumAlgorithm);
            var signature = new Signature(new SignatureMetadata
                {
                    ChunkHashAlgorithm = hashAlgo.Name,
                    RollingChecksumAlgorithm = rollingChecksumAlgo.Name
                }, RsyncFormatType.Octodiff);

            ReadChunks(signature);

            return signature;
        }

        private void ReadChunks(Signature signature)
        {
            var expectedHashLength = signature.HashAlgorithm.HashLength;
            long start = 0;

            var signatureLength = reader.BaseStream.Length;
            var remainingBytes = signatureLength - reader.BaseStream.Position;
            var signatureSize = sizeof(ushort) + sizeof(uint) + expectedHashLength;
            if (remainingBytes % signatureSize != 0)
                throw new InvalidDataException("The signature file appears to be corrupt; at least one chunk has data missing.");

            while (reader.BaseStream.Position < signatureLength - 1)
            {
                var length = reader.ReadInt16();
                var checksum = reader.ReadUInt32();
                var chunkHash = reader.ReadBytes(expectedHashLength);

                signature.Chunks.Add(new ChunkSignature
                {
                    StartOffset = start,
                    Length = length,
                    RollingChecksum = checksum,
                    Hash = chunkHash
                });

                start += length;

                Progress();
            }
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