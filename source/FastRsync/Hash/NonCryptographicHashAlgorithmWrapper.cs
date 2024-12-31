using System;
using System.IO.Hashing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FastRsync.Hash
{
    public class NonCryptographicHashAlgorithmWrapper : IHashAlgorithm
    {
        public string Name { get; }

        public int HashLengthInBytes => algorithm.HashLengthInBytes;

        private readonly NonCryptographicHashAlgorithm algorithm;

        public NonCryptographicHashAlgorithmWrapper(string name, NonCryptographicHashAlgorithm algorithm)
        {
            Name = name;
            this.algorithm = algorithm;
        }

        public byte[] ComputeHash(Stream stream)
        {
            algorithm.Append(stream);
            var hash = algorithm.GetHashAndReset();
            Array.Reverse(hash);

            return hash;
        }

        public async Task<byte[]> ComputeHashAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            await algorithm.AppendAsync(stream, cancellationToken).ConfigureAwait(false);
            var hash = algorithm.GetHashAndReset();
            Array.Reverse(hash);

            return hash;
        }

        public byte[] ComputeHash(byte[] buffer, int offset, int length)
        {
            var data = buffer.AsSpan(offset, length);
            algorithm.Append(data);
            var hash = algorithm.GetHashAndReset();
            Array.Reverse(hash);

            return hash;
        }
    }
}
