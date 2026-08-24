using System.Text;

namespace FastRsync.Core
{
    internal class BinaryFormat
    {
        public const int SignatureFormatHeaderLength = 7; // OCTOSIG or FRSNCSG
        public const int DeltaFormatHeaderLength = 9; // OCTODELTA or FRSNCDLTA
    }

    internal class OctoBinaryFormat
    {
        public static readonly byte[] SignatureHeader = Encoding.ASCII.GetBytes("OCTOSIG");
        public static readonly byte[] DeltaHeader = Encoding.ASCII.GetBytes("OCTODELTA");
        public static readonly byte[] EndOfMetadata = Encoding.ASCII.GetBytes(">>>");

        public const byte Version = 0x01;
    }

    internal class FastRsyncBinaryFormat
    {
        public static readonly byte[] SignatureHeader = Encoding.ASCII.GetBytes("FRSNCSG");
        public static readonly byte[] DeltaHeader = Encoding.ASCII.GetBytes("FRSNCDLTA");

        public const byte Version = 0x01;
    }
}