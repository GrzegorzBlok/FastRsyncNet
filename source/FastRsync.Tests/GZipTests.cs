using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using FastRsync.Compression;
using FastRsync.Core;
using FastRsync.Delta;
using FastRsync.Signature;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace FastRsync.Tests
{
    [TestFixture]
    public class GZipTests
    {
        [Test]
        [TestCase(120)]
        [TestCase(5 * 1024 * 1024)]
        public void GZip_CompressRandomData(int dataLength)
        {
            // Arrange
            var data = new byte[dataLength];

            // Main flow
            CompressData(data);
        }

        [Test]
        public void GZip_CompressData()
        {
            // Arrange
            var data = File.ReadAllBytes(".\\TestData\\basefile.bin");

            // Main flow
            var destStream = CompressData(data);

            // Additional asserts
            Assert.AreEqual(6982738, destStream.Length);
        }

        private static MemoryStream CompressData(byte[] data)
        {
            // Arrange
            var srcStream = new MemoryStream(data);
            var destStream = new MemoryStream();

            // Act
            GZip.Compress(srcStream, destStream);

            // Assert
            destStream.Seek(0, SeekOrigin.Begin);
            var decompressedStream = new MemoryStream();
            using (var gz = new GZipStream(destStream, CompressionMode.Decompress, true))
            {
                gz.CopyTo(decompressedStream);
            }

            var dataOutput = decompressedStream.ToArray();
            Assert.AreEqual(data, dataOutput);
            return destStream;
        }

        [Test]
        [TestCase(120)]
        [TestCase(5 * 1024 * 1024)]
        public void GZip_CompressRandomData_RsyncSignatureAndPatch(int dataLength)
        {
            // Arrange
            var dataBasis = new byte[dataLength];
            new Random().NextBytes(dataBasis);
            var basisStream = new MemoryStream(dataBasis);

            var newFileStream = new MemoryStream();
            newFileStream.Write(dataBasis, 10, dataLength * 4 / 5);
            var newRandomData = new byte[dataLength * 2 / 5];
            new Random().NextBytes(newRandomData);
            newFileStream.Write(newRandomData, 0, newRandomData.Length);
            newFileStream.Seek(0, SeekOrigin.Begin);

            // Main flow
            FullCompressedRsyncFlow(basisStream, newFileStream);
        }

        [Test]
        public void GZip_CompressData_RsyncSignatureAndPatch()
        {
            // Arrange
            // Arrange
            var dataBasis = File.ReadAllBytes(".\\TestData\\basefile.bin");
            var basisStream = new MemoryStream(dataBasis);

            var newData = File.ReadAllBytes(".\\TestData\\basefile2.bin");
            var newFileStream = new MemoryStream(newData);

            // Main flow
            var deltaStream = FullCompressedRsyncFlow(basisStream, newFileStream);

            // Not calculated, taken from the valid flow
            Assert.AreEqual(662628, deltaStream.Length);
        }

        private static MemoryStream FullCompressedRsyncFlow(MemoryStream basisStream, MemoryStream newFileStream)
        {
            var basisStreamCompressed = new MemoryStream();
            var basisStreamCompressedSignature = new MemoryStream();

            var newFileStreamCompressed = new MemoryStream();
            var deltaStream = new MemoryStream();
            var patchedCompressedStream = new MemoryStream();

            // Act
            GZip.Compress(basisStream, basisStreamCompressed);
            basisStreamCompressed.Seek(0, SeekOrigin.Begin);

            var signatureBuilder = new SignatureBuilder();
            signatureBuilder.Build(basisStreamCompressed, new SignatureWriter(basisStreamCompressedSignature));
            basisStreamCompressedSignature.Seek(0, SeekOrigin.Begin);

            GZip.Compress(newFileStream, newFileStreamCompressed);
            newFileStreamCompressed.Seek(0, SeekOrigin.Begin);

            var deltaBuilder = new DeltaBuilder();
            deltaBuilder.BuildDelta(newFileStreamCompressed, new SignatureReader(basisStreamCompressedSignature, null),
                new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream)));
            deltaStream.Seek(0, SeekOrigin.Begin);

            var deltaApplier = new DeltaApplier
            {
                SkipHashCheck = true
            };
            var deltaReader = new BinaryDeltaReader(deltaStream, null);
            deltaApplier.Apply(basisStreamCompressed, deltaReader, patchedCompressedStream);
            deltaApplier.HashCheck(deltaReader, patchedCompressedStream);

            // Assert
            Assert.AreEqual(newFileStreamCompressed.ToArray(), patchedCompressedStream.ToArray());

            patchedCompressedStream.Seek(0, SeekOrigin.Begin);
            var decompressedStream = new MemoryStream();
            using (var gz = new GZipStream(patchedCompressedStream, CompressionMode.Decompress))
            {
                gz.CopyTo(decompressedStream);
            }

            var dataOutput = decompressedStream.ToArray();
            Assert.AreEqual(newFileStream.ToArray(), dataOutput);
            return deltaStream;
        }

        [Test]
        [TestCase(120)]
        [TestCase(5 * 1024 * 1024)]
        public async Task GZip_CompressRandomDataAsync_RsyncSignatureAndPatch(int dataLength)
        {
            // Arrange
            var dataBasis = new byte[dataLength];
            new Random().NextBytes(dataBasis);

            var newFileStream = new MemoryStream();
            newFileStream.Write(dataBasis, 10, dataLength * 4 / 5);
            var newRandomData = new byte[dataLength * 2 / 5];
            new Random().NextBytes(newRandomData);
            newFileStream.Write(newRandomData, 0, newRandomData.Length);
            newFileStream.Seek(0, SeekOrigin.Begin);

            var basisStream = new MemoryStream(dataBasis);

            // Main flow
            await FullCompressedRsyncFlowAsync(basisStream, newFileStream);
        }

        [Test]
        public async Task GZip_CompressDataAsync_RsyncSignatureAndPatch()
        {
            // Arrange
            var dataBasis = File.ReadAllBytes(".\\TestData\\basefile.bin");
            var basisStream = new MemoryStream(dataBasis);

            var newData = File.ReadAllBytes(".\\TestData\\basefile2.bin");
            var newFileStream = new MemoryStream(newData);

            // Main flow
            var deltaStream = await FullCompressedRsyncFlowAsync(basisStream, newFileStream);

            // Not calculated, taken from the valid flow
            Assert.AreEqual(662628, deltaStream.Length);
        }

        private static async Task<MemoryStream> FullCompressedRsyncFlowAsync(MemoryStream basisStream, MemoryStream newFileStream)
        {
            // Arrange
            var basisStreamCompressed = new MemoryStream();
            var basisStreamCompressedSignature = new MemoryStream();

            var newFileStreamCompressed = new MemoryStream();
            var deltaStream = new MemoryStream();
            var patchedCompressedStream = new MemoryStream();

            // Act
            await GZip.CompressAsync(basisStream, basisStreamCompressed);
            basisStreamCompressed.Seek(0, SeekOrigin.Begin);

            var signatureBuilder = new SignatureBuilder();
            await signatureBuilder.BuildAsync(basisStreamCompressed, new SignatureWriter(basisStreamCompressedSignature));
            basisStreamCompressedSignature.Seek(0, SeekOrigin.Begin);

            await GZip.CompressAsync(newFileStream, newFileStreamCompressed);
            newFileStreamCompressed.Seek(0, SeekOrigin.Begin);

            var deltaBuilder = new DeltaBuilder();
            await deltaBuilder.BuildDeltaAsync(newFileStreamCompressed,
                new SignatureReader(basisStreamCompressedSignature, null),
                new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream)));
            deltaStream.Seek(0, SeekOrigin.Begin);

            var deltaApplier = new DeltaApplier
            {
                SkipHashCheck = true
            };
            var deltaReader = new BinaryDeltaReader(deltaStream, null);
            await deltaApplier.ApplyAsync(basisStreamCompressed, deltaReader, patchedCompressedStream);
            await deltaApplier.HashCheckAsync(deltaReader, patchedCompressedStream);

            // Assert
            Assert.AreEqual(newFileStreamCompressed.ToArray(), patchedCompressedStream.ToArray());

            patchedCompressedStream.Seek(0, SeekOrigin.Begin);
            var decompressedStream = new MemoryStream();
            await using (var gz = new GZipStream(patchedCompressedStream, CompressionMode.Decompress))
            {
                await gz.CopyToAsync(decompressedStream);
            }

            Assert.AreEqual(newFileStream.ToArray(), decompressedStream.ToArray());
            return deltaStream;
        }
    }
}
