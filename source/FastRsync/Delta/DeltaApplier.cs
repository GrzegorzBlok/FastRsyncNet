using System;
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

        /// <summary>
        /// When enabled (default), the multi-megabyte working buffer is rented from the shared
        /// array pool instead of allocated per call. This reduces GC pressure when many apply
        /// operations run in the same process. Disable for one-shot/local use where retaining
        /// pooled buffers is undesirable. Does not affect output.
        /// </summary>
        public bool UseBufferPool { get; set; } = true;

        public void Apply(Stream basisFileStream, IDeltaReader delta, Stream outputStream)
        {
            if (!SkipHashCheck)
                ValidateBasisFileLength(basisFileStream, delta);

            var rented = PooledBuffer.Rent(readBufferSize, UseBufferPool);
            var buffer = rented.Array;
            try
            {
                var preallocatedLength = TryPreallocateOutput(outputStream, delta.Metadata);

                delta.Apply(
                    writeData: (data) => outputStream.Write(data, 0, data.Length),
                    copy: (startPosition, length) =>
                    {
                        basisFileStream.Seek(startPosition, SeekOrigin.Begin);

                        int read;
                        long soFar = 0;
                        while ((read = basisFileStream.Read(buffer, 0, (int)Math.Min(length - soFar, readBufferSize))) > 0)
                        {
                            soFar += read;
                            outputStream.Write(buffer, 0, read);
                        }
                    });

                TrimPreallocatedOutput(outputStream, preallocatedLength);
            }
            finally
            {
                rented.Dispose();
            }

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
            if (!SkipHashCheck)
                ValidateBasisFileLength(basisFileStream, delta);

            var rented = PooledBuffer.Rent(readBufferSize, UseBufferPool);
            var buffer = rented.Array;
            try
            {
                var preallocatedLength = TryPreallocateOutput(outputStream, delta.Metadata);

                await delta.ApplyAsync(
                    writeData: async (data) => await outputStream.WriteAsync(data, 0, data.Length, cancellationToken).ConfigureAwait(false),
                    copy: async (startPosition, length) =>
                    {
                        basisFileStream.Seek(startPosition, SeekOrigin.Begin);

                        int read;
                        long soFar = 0;
                        while ((read = await basisFileStream.ReadAsync(buffer, 0, (int)Math.Min(length - soFar, readBufferSize), cancellationToken).ConfigureAwait(false)) > 0)
                        {
                            soFar += read;
                            await outputStream.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                        }
                    }, cancellationToken).ConfigureAwait(false);

                TrimPreallocatedOutput(outputStream, preallocatedLength);
            }
            finally
            {
                rented.Dispose();
            }

            if (!SkipHashCheck)
            {
                if (!await HashCheckAsync(delta, outputStream, cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidDataException(
                        $"Verification of the patched file failed. The {delta.Metadata.ExpectedFileHashAlgorithm} hash of the patch result file, and the file that was used as input for the delta, do not match. This can happen if the basis file changed since the signatures were calculated.");
                }
            }
        }

        private static void ValidateBasisFileLength(Stream basisFileStream, IDeltaReader delta)
        {
            var expectedLength = delta.Metadata?.BaseFileLength;
            if (expectedLength.HasValue && basisFileStream.CanSeek && basisFileStream.Length != expectedLength.Value)
            {
                throw new InvalidDataException(
                    $"The basis file does not match the one the delta was built against (the delta expects a basis of {expectedLength.Value} bytes but the supplied basis is {basisFileStream.Length} bytes). Make sure the delta is being applied to the correct basis file.");
            }
        }

        // Preallocating the output improves large-file writes on seekable streams (fewer
        // incremental file extensions, less fragmentation). The guard requires a fresh output
        // (empty, at position zero, seekable and writable), so network, append-only or
        // partially written streams keep their existing behavior. Deltas from older versions
        // carry no target length and skip preallocation as well.
        private static long TryPreallocateOutput(Stream outputStream, DeltaMetadata metadata)
        {
            var targetFileLength = metadata?.TargetFileLength ?? 0;
            if (targetFileLength <= 0 || !outputStream.CanSeek || !outputStream.CanWrite
                || outputStream.Position != 0 || outputStream.Length != 0)
                return 0;

            try
            {
                outputStream.SetLength(targetFileLength);
                return targetFileLength;
            }
            catch (NotSupportedException)
            {
                // Seekable but fixed-size streams, e.g. a MemoryStream over a caller-supplied array.
                return 0;
            }
        }

        // If the delta produced fewer bytes than the declared target length, trim the
        // preallocated tail so the output matches what a non-preallocated apply produces.
        private static void TrimPreallocatedOutput(Stream outputStream, long preallocatedLength)
        {
            if (preallocatedLength > 0 && outputStream.Position < preallocatedLength)
            {
                outputStream.SetLength(outputStream.Position);
            }
        }

        public bool HashCheck(IDeltaReader delta, Stream outputStream)
        {
            outputStream.Seek(0, SeekOrigin.Begin);

            var sourceFileHash = delta.ExpectedHash;
            var algorithm = SupportedAlgorithms.Hashing.Create(delta.Metadata.ExpectedFileHashAlgorithm);

            var actualHash = algorithm.ComputeHash(outputStream);
            (algorithm as IDisposable)?.Dispose();

            return ByteArrayEquality.AreEqual(sourceFileHash, actualHash);
        }

        public Task<bool> HashCheckAsync(IDeltaReader delta, Stream outputStream) => HashCheckAsync(delta, outputStream, CancellationToken.None);

        public async Task<bool> HashCheckAsync(IDeltaReader delta, Stream outputStream, CancellationToken cancellationToken)
        {
            outputStream.Seek(0, SeekOrigin.Begin);

            var sourceFileHash = delta.ExpectedHash;
            var algorithm = SupportedAlgorithms.Hashing.Create(delta.Metadata.ExpectedFileHashAlgorithm);

            var actualHash = await algorithm.ComputeHashAsync(outputStream, cancellationToken).ConfigureAwait(false);
            (algorithm as IDisposable)?.Dispose();

            return ByteArrayEquality.AreEqual(sourceFileHash, actualHash);
        }
    }
}