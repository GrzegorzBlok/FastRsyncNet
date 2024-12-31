using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FastRsync.Core;

namespace FastRsync.Signature
{
    public class SignatureWriter : ISignatureWriter
    {
        private readonly Stream signatureStream;
        private readonly BinaryWriter signaturebw;

        public SignatureWriter(Stream signatureStream)
        {
            this.signatureStream = signatureStream;
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

        public async Task WriteMetadataAsync(SignatureMetadata metadata, CancellationToken cancellationToken)
        {
            var ms = new MemoryStream(256);
            var msbw = new BinaryWriter(ms);
            WriteMetadataInternal(msbw, metadata);
            ms.Seek(0, SeekOrigin.Begin);
#if (NET5_0_OR_GREATER)
            await ms.CopyToAsync(signatureStream, cancellationToken).ConfigureAwait(false);
#else
            await ms.CopyToAsync(signatureStream).ConfigureAwait(false);
#endif
        }

        public void WriteChunk(ChunkSignature signature)
        {
            signaturebw.Write(signature.Length);
            signaturebw.Write(signature.RollingChecksum);
            signaturebw.Write(signature.Hash);
        }

        public async Task WriteChunkAsync(ChunkSignature signature, CancellationToken cancellationToken)
        {
            signaturebw.Write(signature.Length);
            signaturebw.Write(signature.RollingChecksum);
            await signatureStream.WriteAsync(signature.Hash, 0, signature.Hash.Length, cancellationToken).ConfigureAwait(false);
        }
    }
}
