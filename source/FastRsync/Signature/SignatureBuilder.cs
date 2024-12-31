using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FastRsync.Core;
using FastRsync.Diagnostics;
using FastRsync.Hash;

namespace FastRsync.Signature
{
    public class SignatureBuilder
    {
        public const short MinimumChunkSize = 128;
        public const short DefaultChunkSize = 2048;
        public const short MaximumChunkSize = 31 * 1024;

        private short chunkSize;

        public SignatureBuilder() : this(SupportedAlgorithms.Hashing.Default(), SupportedAlgorithms.Checksum.Default())
        {
        }

        public SignatureBuilder(IHashAlgorithm hashAlgorithm, IRollingChecksum rollingChecksumAlgorithm)
        {
            HashAlgorithm = hashAlgorithm;
            RollingChecksumAlgorithm = rollingChecksumAlgorithm;
            ChunkSize = DefaultChunkSize;
            ProgressReport = null;
        }

        public IProgress<ProgressReport> ProgressReport { get; set; }

        public IHashAlgorithm HashAlgorithm { get; set; }

        public IRollingChecksum RollingChecksumAlgorithm { get; set; }

        public short ChunkSize
        {
            get => chunkSize;
            set
            {
                if (value < MinimumChunkSize)
                    throw new ArgumentException($"Chunk size cannot be less than {MinimumChunkSize}");
                if (value > MaximumChunkSize)
                    throw new ArgumentException($"Chunk size cannot be exceed {MaximumChunkSize}");
                chunkSize = value;
            }
        }

        public void Build(Stream baseDataStream, ISignatureWriter signatureWriter)
        {
            WriteMetadata(baseDataStream, signatureWriter);
            WriteChunkSignatures(baseDataStream, signatureWriter);
        }

        public Task BuildAsync(Stream baseDataStream, ISignatureWriter signatureWriter) =>
            BuildAsync(baseDataStream, signatureWriter, CancellationToken.None);

        public async Task BuildAsync(Stream baseDataStream, ISignatureWriter signatureWriter, CancellationToken cancellationToken)
        {
            await WriteMetadataAsync(baseDataStream, signatureWriter, cancellationToken).ConfigureAwait(false);
            await WriteChunkSignaturesAsync(baseDataStream, signatureWriter, cancellationToken).ConfigureAwait(false);
        }

        private void WriteMetadata(Stream baseFileStream, ISignatureWriter signatureWriter)
        {
            ProgressReport?.Report(new ProgressReport
            {
                Operation = ProgressOperationType.HashingFile,
                CurrentPosition = 0,
                Total = baseFileStream.Length
            });

            baseFileStream.Seek(0, SeekOrigin.Begin);
            var baseFileVerificationHashAlgorithm = SupportedAlgorithms.Hashing.Md5();
            var baseFileHash = baseFileVerificationHashAlgorithm.ComputeHash(baseFileStream);

            signatureWriter.WriteMetadata(new SignatureMetadata
            {
                ChunkHashAlgorithm = HashAlgorithm.Name,
                RollingChecksumAlgorithm = RollingChecksumAlgorithm.Name,
                BaseFileHashAlgorithm = baseFileVerificationHashAlgorithm.Name,
                BaseFileHash = Convert.ToBase64String(baseFileHash)
            });

            ProgressReport?.Report(new ProgressReport
            {
                Operation = ProgressOperationType.HashingFile,
                CurrentPosition = baseFileStream.Length,
                Total = baseFileStream.Length
            });
        }

        private async Task WriteMetadataAsync(Stream baseFileStream, ISignatureWriter signatureWriter, CancellationToken cancellationToken)
        {
            ProgressReport?.Report(new ProgressReport
            {
                Operation = ProgressOperationType.HashingFile,
                CurrentPosition = 0,
                Total = baseFileStream.Length
            });

            baseFileStream.Seek(0, SeekOrigin.Begin);
            var baseFileVerificationHashAlgorithm = SupportedAlgorithms.Hashing.Md5();
            var baseFileHash = await baseFileVerificationHashAlgorithm.ComputeHashAsync(baseFileStream, cancellationToken).ConfigureAwait(false);

            await signatureWriter.WriteMetadataAsync(new SignatureMetadata
            {
                ChunkHashAlgorithm = HashAlgorithm.Name,
                RollingChecksumAlgorithm = RollingChecksumAlgorithm.Name,
                BaseFileHashAlgorithm = baseFileVerificationHashAlgorithm.Name,
                BaseFileHash = Convert.ToBase64String(baseFileHash)
            }, cancellationToken).ConfigureAwait(false);

            ProgressReport?.Report(new ProgressReport
            {
                Operation = ProgressOperationType.HashingFile,
                CurrentPosition = baseFileStream.Length,
                Total = baseFileStream.Length
            });
        }

        private void WriteChunkSignatures(Stream baseFileStream, ISignatureWriter signatureWriter)
        {
            var checksumAlgorithm = RollingChecksumAlgorithm;
            var hashAlgorithm = HashAlgorithm;

            ProgressReport?.Report(new ProgressReport
            {
                Operation = ProgressOperationType.BuildingSignatures,
                CurrentPosition = 0,
                Total = baseFileStream.Length
            });
            baseFileStream.Seek(0, SeekOrigin.Begin);

            long start = 0;
            int read;
            var block = new byte[ChunkSize];
            while ((read = baseFileStream.Read(block, 0, block.Length)) > 0)
            {
                signatureWriter.WriteChunk(new ChunkSignature
                {
                    StartOffset = start,
                    Length = (short)read,
                    Hash = hashAlgorithm.ComputeHash(block, 0, read),
                    RollingChecksum = checksumAlgorithm.Calculate(block, 0, read)
                });

                start += read;
                ProgressReport?.Report(new ProgressReport
                {
                    Operation = ProgressOperationType.BuildingSignatures,
                    CurrentPosition = start,
                    Total = baseFileStream.Length
                });
            }
        }

        private async Task WriteChunkSignaturesAsync(Stream baseFileStream, ISignatureWriter signatureWriter, CancellationToken cancellationToken)
        {
            var checksumAlgorithm = RollingChecksumAlgorithm;
            var hashAlgorithm = HashAlgorithm;

            ProgressReport?.Report(new ProgressReport
            {
                Operation = ProgressOperationType.BuildingSignatures,
                CurrentPosition = 0,
                Total = baseFileStream.Length
            });
            baseFileStream.Seek(0, SeekOrigin.Begin);

            long start = 0;
            int read;
            var block = new byte[ChunkSize];
            while ((read = await baseFileStream.ReadAsync(block, 0, block.Length, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await signatureWriter.WriteChunkAsync(new ChunkSignature
                {
                    StartOffset = start,
                    Length = (short)read,
                    Hash = hashAlgorithm.ComputeHash(block, 0, read),
                    RollingChecksum = checksumAlgorithm.Calculate(block, 0, read)
                }, cancellationToken).ConfigureAwait(false);

                start += read;
                ProgressReport?.Report(new ProgressReport
                {
                    Operation = ProgressOperationType.BuildingSignatures,
                    CurrentPosition = start,
                    Total = baseFileStream.Length
                });
            }
        }
    }
}