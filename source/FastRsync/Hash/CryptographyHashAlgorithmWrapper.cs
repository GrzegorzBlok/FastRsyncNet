using System.IO;
using System.Security.Cryptography;
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

        public Task<byte[]> ComputeHashAsync(Stream stream)
        {
            return Task.FromResult(algorithm.ComputeHash(stream));
        }

        public byte[] ComputeHash(byte[] buffer, int offset, int length)
        {
            return algorithm.ComputeHash(buffer, offset, length);
        }
    }
}