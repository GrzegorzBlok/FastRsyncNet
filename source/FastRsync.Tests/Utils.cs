using System;
using System.IO;
using System.Security.Cryptography;
using FastRsync.Signature;

namespace FastRsync.Tests
{
    class Utils
    {
        public static string GetMd5(byte[] data)
        {
            using MD5 md5Hash = MD5.Create();
            return Convert.ToBase64String(md5Hash.ComputeHash(data));
        }

        public static (MemoryStream baseDataStream, MemoryStream baseSignatureStream, byte[] newData, MemoryStream newDataStream) PrepareTestData(int baseNumberOfBytes, int newDataNumberOfBytes,
            short chunkSize)
        {
            var baseData = new byte[baseNumberOfBytes];
            new Random().NextBytes(baseData);
            var baseDataStream = new MemoryStream(baseData);
            var baseSignatureStream = new MemoryStream();

            var signatureBuilder = new SignatureBuilder
            {
                ChunkSize = chunkSize
            };
            signatureBuilder.Build(baseDataStream, new SignatureWriter(baseSignatureStream));
            baseSignatureStream.Seek(0, SeekOrigin.Begin);

            var newData = new byte[newDataNumberOfBytes];
            new Random().NextBytes(newData);
            var newDataStream = new MemoryStream(newData);
            return (baseDataStream, baseSignatureStream, newData, newDataStream);
        }
    }
}
