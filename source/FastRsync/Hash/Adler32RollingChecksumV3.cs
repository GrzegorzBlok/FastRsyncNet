using System.Reflection;
using System.Runtime.CompilerServices;

namespace FastRsync.Hash
{
    public class Adler32RollingChecksumV3 : IRollingChecksum
    {
        public string Name => "Adler32V3";

        private const ushort Modulus = 65521;

        public uint Calculate(byte[] block, int offset, int count)
        {
            var a = 1;
            var b = 0;

            for (count += offset; offset < count; ++offset)
            {
                a += block[offset];
                a -= a >= Modulus ? Modulus : 0;
                b += a;
                b -= b >= Modulus ? Modulus : 0;
            }
            return (uint)((b << 16) | a);
        }

        public uint Rotate(uint checksum, byte remove, byte add, int chunkSize)
        {
            var b = (ushort)(checksum >> 16);
            var a = (ushort)checksum;

            var temp = a - remove + add;
            a = (ushort)(temp < 0 ? temp + Modulus : temp >= Modulus ? temp - Modulus : temp);
            temp = (b - (chunkSize * remove) + a - 1) % Modulus;
            b = (ushort)(temp < 0 ? temp + Modulus : temp);

            return (uint)((b << 16) | a);
        }
    }
}
