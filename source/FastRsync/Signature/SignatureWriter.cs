using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FastRsync.Core;

namespace FastRsync.Signature
{
    public class SignatureWriter : ISignatureWriter
    {
        private readonly Stream _signatureStream;
        private readonly BinaryWriter signaturebw;

        public SignatureWriter(Stream signatureStream)
        {
            _signatureStream = signatureStream;
            signaturebw = new BinaryWriter(signatureStream);
        }

        private static void WriteMetadataInternal(BinaryWriter bw, SignatureMetadata metadata)
        {
            bw.Write(FastRsyncBinaryFormat.SignatureHeader);
            bw.Write(FastRsyncBinaryFormat.Version);
            var metadataStr = JsonSerializer.Serialize(metadata, JsonSerializationSettings.JsonSettings);
            bw.Write(metadataStr);
        }

        public void WriteMetadata(SignatureMetadata metadata)
        {
            WriteMetadataInternal(signaturebw, metadata);
        }

        public async Task WriteMetadataAsync(SignatureMetadata metadata)
        {
            var ms = new MemoryStream(256);
            var msbw = new BinaryWriter(ms);
            WriteMetadataInternal(msbw, metadata);
            ms.Seek(0, SeekOrigin.Begin);
            await ms.CopyToAsync(_signatureStream).ConfigureAwait(false);
        }

        public void WriteChunk(ChunkSignature signature)
        {
            signaturebw.Write(signature.Length);
            signaturebw.Write(signature.RollingChecksum);
            signaturebw.Write(signature.Hash);
        }

        public async Task WriteChunkAsync(ChunkSignature signature)
        {
            signaturebw.Write(signature.Length);
            signaturebw.Write(signature.RollingChecksum);
            await _signatureStream.WriteAsync(signature.Hash, 0, signature.Hash.Length).ConfigureAwait(false);
        }
    }
}
