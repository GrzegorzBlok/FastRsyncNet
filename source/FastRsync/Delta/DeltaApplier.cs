using System;
using System.Collections;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FastRsync.Core;

namespace FastRsync.Delta
{
    public class DeltaApplier
    {
        private readonly int readBufferSize;

        public DeltaApplier(int readBufferSize = 4 * 1024 * 1024)
        {
            SkipHashCheck = false;
            this.readBufferSize = readBufferSize;
        }

        public bool SkipHashCheck { get; set; }

        public void Apply(Stream basisFileStream, IDeltaReader delta, Stream outputStream)
        {
            var buffer = new byte[readBufferSize];

            delta.Apply(
                writeData: (data) => outputStream.Write(data, 0, data.Length),
                copy: (startPosition, length) =>
                {
                    basisFileStream.Seek(startPosition, SeekOrigin.Begin);

                    int read;
                    long soFar = 0;
                    while ((read = basisFileStream.Read(buffer, 0, (int)Math.Min(length - soFar, buffer.Length))) > 0)
                    {
                        soFar += read;
                        outputStream.Write(buffer, 0, read);
                    }
                });

            if (!SkipHashCheck)
            {
                if (!HashCheck(delta, outputStream))
                {
                    throw new InvalidDataException(
                        $"Verification of the patched file failed. The {delta.HashAlgorithm.Name} hash of the patch result file, and the file that was used as input for the delta, do not match. This can happen if the basis file changed since the signatures were calculated.");
                }
            }
        }

        public Task ApplyAsync(Stream basisFileStream, IDeltaReader delta, Stream outputStream) =>
            ApplyAsync(basisFileStream, delta, outputStream, CancellationToken.None);

        public async Task ApplyAsync(Stream basisFileStream, IDeltaReader delta, Stream outputStream, CancellationToken cancellationToken)
        {
            var buffer = new byte[readBufferSize];

            await delta.ApplyAsync(
                writeData: async (data) => await outputStream.WriteAsync(data, 0, data.Length, cancellationToken).ConfigureAwait(false),
                copy: async (startPosition, length) =>
                {
                    basisFileStream.Seek(startPosition, SeekOrigin.Begin);

                    int read;
                    long soFar = 0;
                    while ((read = await basisFileStream.ReadAsync(buffer, 0, (int)Math.Min(length - soFar, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        soFar += read;
                        await outputStream.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                    }
                }, cancellationToken).ConfigureAwait(false);

            if (!SkipHashCheck)
            {
                if (!await HashCheckAsync(delta, outputStream, cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidDataException(
                        $"Verification of the patched file failed. The {delta.Metadata.ExpectedFileHashAlgorithm} hash of the patch result file, and the file that was used as input for the delta, do not match. This can happen if the basis file changed since the signatures were calculated.");
                }
            }
        }

        public bool HashCheck(IDeltaReader delta, Stream outputStream)
        {
            outputStream.Seek(0, SeekOrigin.Begin);

            var sourceFileHash = delta.ExpectedHash;
            var algorithm = SupportedAlgorithms.Hashing.Create(delta.Metadata.ExpectedFileHashAlgorithm);

            var actualHash = algorithm.ComputeHash(outputStream);

            return StructuralComparisons.StructuralEqualityComparer.Equals(sourceFileHash, actualHash);
        }

        public Task<bool> HashCheckAsync(IDeltaReader delta, Stream outputStream) => HashCheckAsync(delta, outputStream, CancellationToken.None);

        public async Task<bool> HashCheckAsync(IDeltaReader delta, Stream outputStream, CancellationToken cancellationToken)
        {
            outputStream.Seek(0, SeekOrigin.Begin);

            var sourceFileHash = delta.ExpectedHash;
            var algorithm = SupportedAlgorithms.Hashing.Create(delta.Metadata.ExpectedFileHashAlgorithm);

            var actualHash = await algorithm.ComputeHashAsync(outputStream, cancellationToken).ConfigureAwait(false);

            return StructuralComparisons.StructuralEqualityComparer.Equals(sourceFileHash, actualHash);
        }
    }
}