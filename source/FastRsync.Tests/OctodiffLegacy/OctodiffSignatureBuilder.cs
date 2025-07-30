using System;
using System.IO;
using FastRsync.Core;
using FastRsync.Hash;

namespace FastRsync.Tests.OctodiffLegacy;

public class OctodiffSignatureBuilder
{
    public const short MinimumChunkSize = 128;
    public const short DefaultChunkSize = 2048;
    public const short MaximumChunkSize = 31 * 1024;

    private short chunkSize;

    public OctodiffSignatureBuilder() : this(SupportedAlgorithms.Hashing.Sha1(), SupportedAlgorithms.Checksum.Default())
    {
    }

    public OctodiffSignatureBuilder(IHashAlgorithm hashAlgorithm, IRollingChecksum rollingChecksumAlgorithm)
    {
        HashAlgorithm = hashAlgorithm;
        RollingChecksumAlgorithm = rollingChecksumAlgorithm;
        ChunkSize = DefaultChunkSize;
    }

    public IHashAlgorithm HashAlgorithm { get; set; }

    public IRollingChecksum RollingChecksumAlgorithm { get; set; }

    public short ChunkSize
    {
        get => chunkSize;
        set
        {
            if (value < MinimumChunkSize)
                throw new ArgumentException($"Chunk size cannot be less than {MinimumChunkSize}");
            if (value > MaximumChunkSize)
                throw new ArgumentException($"Chunk size cannot be exceed {MaximumChunkSize}");
            chunkSize = value;
        }
    }

    public void Build(Stream stream, IOctodiffSignatureWriter signatureWriter)
    {
        WriteMetadata(stream, signatureWriter);
        WriteChunkSignatures(stream, signatureWriter);
    }

    private void WriteMetadata(Stream stream, IOctodiffSignatureWriter signatureWriter)
    {
        stream.Seek(0, SeekOrigin.Begin);
        signatureWriter.WriteMetadata(HashAlgorithm, RollingChecksumAlgorithm);
    }
        
    private void WriteChunkSignatures(Stream stream, IOctodiffSignatureWriter signatureWriter)
    {
        var checksumAlgorithm = RollingChecksumAlgorithm;
        var hashAlgorithm = HashAlgorithm;

        stream.Seek(0, SeekOrigin.Begin);

        long start = 0;
        int read;
        var block = new byte[ChunkSize];
        while ((read = stream.Read(block, 0, block.Length)) > 0)
        {
            signatureWriter.WriteChunk(new ChunkSignature
            {
                StartOffset = start,
                Length = (short)read,
                Hash = hashAlgorithm.ComputeHash(block, 0, read),
                RollingChecksum = checksumAlgorithm.Calculate(block, 0, read)
            });

            start += read;
        }
    }
}