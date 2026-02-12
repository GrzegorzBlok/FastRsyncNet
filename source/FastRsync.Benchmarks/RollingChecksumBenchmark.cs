using System;
using BenchmarkDotNet.Attributes;
using FastRsync.Core;
using FastRsync.Hash;

namespace FastRsync.Benchmarks
{
    public class RollingCheckSumBenchmark
    {
        [Params(128, 16974, 356879)] 
        public int N { get; set; }

        private byte[] data;

        private readonly IRollingChecksum adler32RollingAlgorithm = SupportedAlgorithms.Checksum.Adler32Rolling();
        private readonly IRollingChecksum adler32RollingV2Algorithm = SupportedAlgorithms.Checksum.Adler32RollingV2();
        private readonly IRollingChecksum adler32RollingV3Algorithm = SupportedAlgorithms.Checksum.Adler32RollingV3();

        private uint checksum32;
        private uint checksum32V2;
        private uint checksum32V3;

        [GlobalSetup]
        public void GlobalSetup()
        {
            data = new byte[N];
            new Random().NextBytes(data);
            checksum32 = adler32RollingAlgorithm.Calculate(data, 0, data.Length - 1);
            checksum32V2 = adler32RollingV2Algorithm.Calculate(data, 0, data.Length - 1);
            checksum32V3 = adler32RollingV3Algorithm.Calculate(data, 0, data.Length - 1);
        }

        [Benchmark]
        public uint Adler32RollingCalculateChecksum()
        {
            return adler32RollingAlgorithm.Calculate(data, 0, data.Length);
        }

        [Benchmark]
        public uint Adler32RollingV2CalculateChecksum()
        {
            return adler32RollingV2Algorithm.Calculate(data, 0, data.Length);
        }

        [Benchmark]
        public uint Adler32RollingV3CalculateChecksum()
        {
            return adler32RollingV3Algorithm.Calculate(data, 0, data.Length);
        }

        [Benchmark]
        public uint Adler32RollingRotateChecksum()
        {
            return adler32RollingAlgorithm.Rotate(checksum32, data[0], data[^1], data.Length - 1);
        }

        [Benchmark]
        public uint Adler32RollingV2RotateChecksum()
        {
            return adler32RollingV2Algorithm.Rotate(checksum32V2, data[0], data[^1], data.Length - 1);
        }

        [Benchmark]
        public uint Adler32RollingV3RotateChecksum()
        {
            return adler32RollingV3Algorithm.Rotate(checksum32V3, data[0], data[^1], data.Length - 1);
        }
    }
}