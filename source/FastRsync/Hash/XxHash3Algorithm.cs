using System;
using System.IO.Hashing;
using System.IO;
using System.Threading.Tasks;
using System.Buffers;

namespace FastRsync.Hash
{
    public class XxHash3Algorithm : IHashAlgorithm
    {
        public string Name => "XXH3";
        public int HashLengthInBytes => algorithm.HashLengthInBytes;

        private readonly NonCryptographicHashAlgorithm algorithm = new XxHash3();

        public byte[] ComputeHash(Stream stream)
        {
            algorithm.Append(stream);
            var hash = algorithm.GetHashAndReset();
            Array.Reverse(hash);

            return hash;
        }

        public async Task<byte[]> ComputeHashAsync(Stream stream)
        {
            await algorithm.AppendAsync(stream).ConfigureAwait(false);
            var hash = algorithm.GetHashAndReset();
            Array.Reverse(hash);

            return hash;
        }

        public byte[] ComputeHash(byte[] buffer, int offset, int length)
        {
            byte[] data = new byte[length];
            Buffer.BlockCopy(buffer, offset, data, 0, length);
            algorithm.Append(data);
            var hash = algorithm.GetHashAndReset();
            Array.Reverse(hash);

            return hash;
        }
    }
}