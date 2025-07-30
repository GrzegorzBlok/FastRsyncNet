using System.IO;
using FastRsync.Core;
using FastRsync.Hash;

namespace FastRsync.Tests.OctodiffLegacy;

public interface IOctodiffSignatureWriter
{
    void WriteMetadata(IHashAlgorithm hashAlgorithm, IRollingChecksum rollingChecksumAlgorithm);
    void WriteChunk(ChunkSignature signature);
}

public class OctodiffSignatureWriter(Stream signatureStream) : IOctodiffSignatureWriter
{
    private readonly BinaryWriter signaturebw = new(signatureStream);

    public void WriteMetadata(IHashAlgorithm hashAlgorithm, IRollingChecksum rollingChecksumAlgorithm)
    {
        signaturebw.Write(OctodiffBinaryFormat.SignatureHeader);
        signaturebw.Write(OctodiffBinaryFormat.Version);
        signaturebw.Write(hashAlgorithm.Name);
        signaturebw.Write(rollingChecksumAlgorithm.Name);
        signaturebw.Write(OctodiffBinaryFormat.EndOfMetadata);
    }
        
    public void WriteChunk(ChunkSignature signature)
    {
        signaturebw.Write(signature.Length);
        signaturebw.Write(signature.RollingChecksum);
        signaturebw.Write(signature.Hash);
    }
}