using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace FastRsync.Hash
{
    public class CryptographyHashAlgorithmWrapper : IHashAlgorithm
    {
        private readonly HashAlgorithm algorithm;

        public CryptographyHashAlgorithmWrapper(string name, HashAlgorithm algorithm)
        {
            Name = name;
            this.algorithm = algorithm;
        }

        public string Name { get; }
        public int HashLengthInBytes => algorithm.HashSize / 8;

        public byte[] ComputeHash(Stream stream)
        {
            return algorithm.ComputeHash(stream);
        }

        public async Task<byte[]> ComputeHashAsync(Stream stream, CancellationToken cancellationToken = default)
        {
#if (NET5_0_OR_GREATER)
            return await algorithm.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
#else
            return await Task.Run(() => algorithm.ComputeHash(stream), cancellationToken).ConfigureAwait(false);
#endif
        }

        public byte[] ComputeHash(byte[] buffer, int offset, int length)
        {
            return algorithm.ComputeHash(buffer, offset, length);
        }
    }
}