using System.Text;

namespace FastRsync.Tests.OctodiffLegacy;

class OctodiffBinaryFormat
{
    public static readonly byte[] SignatureHeader = Encoding.ASCII.GetBytes("OCTOSIG");
    public static readonly byte[] DeltaHeader = Encoding.ASCII.GetBytes("OCTODELTA");
    public static readonly byte[] EndOfMetadata = Encoding.ASCII.GetBytes(">>>");
    public const byte CopyCommand = 0x60;
    public const byte DataCommand = 0x80;

    public const byte Version = 0x01;
}