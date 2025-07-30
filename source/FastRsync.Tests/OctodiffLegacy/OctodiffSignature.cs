using System.Collections.Generic;
using FastRsync.Core;
using FastRsync.Hash;

namespace FastRsync.Tests.OctodiffLegacy;

public class OctodiffSignature
{
    public OctodiffSignature(IHashAlgorithm hashAlgorithm, IRollingChecksum rollingChecksumAlgorithm)
    {
        HashAlgorithm = hashAlgorithm;
        RollingChecksumAlgorithm = rollingChecksumAlgorithm;
        Chunks = new List<ChunkSignature>();
    }

    public IHashAlgorithm HashAlgorithm { get; private set; }
    public IRollingChecksum RollingChecksumAlgorithm { get; private set; }
    public List<ChunkSignature> Chunks { get; private set; }
}