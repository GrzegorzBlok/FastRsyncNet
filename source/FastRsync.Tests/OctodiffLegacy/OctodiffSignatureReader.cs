using System;
using System.Collections;
using System.IO;
using FastRsync.Core;
using FastRsync.Diagnostics;

namespace FastRsync.Tests.OctodiffLegacy;

public interface IOctodiffSignatureReader
{
    OctodiffSignature ReadSignature();
}

public class OctodiffSignatureReader : IOctodiffSignatureReader
{
    private readonly BinaryReader reader;

    public OctodiffSignatureReader(Stream stream, IProgress<ProgressReport> progressHandler)
    {
        this.reader = new BinaryReader(stream);
    }

    public OctodiffSignature ReadSignature()
    {
        var header = reader.ReadBytes(OctodiffBinaryFormat.SignatureHeader.Length);
        if (!StructuralComparisons.StructuralEqualityComparer.Equals(OctodiffBinaryFormat.SignatureHeader, header))
            throw new InvalidDataException("The signature file appears to be corrupt.");

        var version = reader.ReadByte();
        if (version != OctodiffBinaryFormat.Version)
            throw new InvalidDataException("The signature file uses a newer file format than this program can handle.");

        var hashAlgorithm = reader.ReadString();
        var rollingChecksumAlgorithm = reader.ReadString();

        var endOfMeta = reader.ReadBytes(OctodiffBinaryFormat.EndOfMetadata.Length);
        if (!StructuralComparisons.StructuralEqualityComparer.Equals(OctodiffBinaryFormat.EndOfMetadata, endOfMeta))
            throw new InvalidDataException("The signature file appears to be corrupt.");

        var hashAlgo = SupportedAlgorithms.Hashing.Create(hashAlgorithm);
        var signature = new OctodiffSignature(
            hashAlgo,
            SupportedAlgorithms.Checksum.Create(rollingChecksumAlgorithm));

        var expectedHashLength = hashAlgo.HashLengthInBytes;
        long start = 0;

        var fileLength = reader.BaseStream.Length;
        var remainingBytes = fileLength - reader.BaseStream.Position;
        var signatureSize = sizeof(ushort) + sizeof(uint) + expectedHashLength;
        if (remainingBytes % signatureSize != 0)
            throw new InvalidDataException("The signature file appears to be corrupt; at least one chunk has data missing.");

        while (reader.BaseStream.Position < fileLength - 1)
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
        }

        return signature;
    }
}