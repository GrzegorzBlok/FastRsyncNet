using System;

namespace FastRsync.Hash
{
    [Obsolete("Adler32V2 has buggy mod operation implemented. See https://github.com/GrzegorzBlok/FastRsyncNet/issues/20")]
    public class Adler32RollingChecksumV2 : IRollingChecksum
    {
        public string Name => "Adler32V2";

        private const ushort Modulus = 65521;

        public uint Calculate(byte[] block, int offset, int count)
        {
            var a = 1;
            var b = 0;
            for (var i = offset; i < offset + count; i++)
            {
                var z = block[i];
                a = (z + a) % Modulus;
                b = (b + a) % Modulus;
            }
            return (uint)((b << 16) | a);
        }

        public uint Rotate(uint checksum, byte remove, byte add, int chunkSize)
        {
            var b = (ushort)(checksum >> 16 & 0xffff);
            var a = (ushort)(checksum & 0xffff);

            a = (ushort)((a - remove + add) % Modulus);
            b = (ushort)((b - (chunkSize * remove) + a - 1) % Modulus);

            return (uint)((b << 16) | a);
        }
    }
}
