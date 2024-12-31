using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FastRsync.Hash
{
    public interface IHashAlgorithm
    {
        string Name { get; }
        int HashLengthInBytes { get; }
        byte[] ComputeHash(Stream stream);
        Task<byte[]> ComputeHashAsync(Stream stream, CancellationToken cancellationToken = default);
        byte[] ComputeHash(byte[] buffer, int offset, int length);
    }
}