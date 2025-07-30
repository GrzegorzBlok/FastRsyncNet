using System.IO;
using System.Threading.Tasks;
using FastRsync.Delta;
using FastRsync.Hash;

namespace FastRsync.Tests.OctodiffLegacy;

public interface IOctodiffDeltaWriter
{
    void WriteMetadata(IHashAlgorithm hashAlgorithm, byte[] expectedNewFileHash);
    void WriteCopyCommand(DataRange segment);
    void WriteDataCommand(Stream source, long offset, long length);
    void Finish();
}