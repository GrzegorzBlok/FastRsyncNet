using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FastRsync.Core;
using FastRsync.Diagnostics;
using FastRsync.Signature;

namespace FastRsync.Delta
{
    public class DeltaBuilder
    {
        private readonly int readBufferSize;

        public DeltaBuilder(int readBufferSize = 4 * 1024 * 1024)
        {
            // The scan window is repositioned by the largest chunk size found in the signature,
            // so the buffer must be able to hold at least one whole chunk of any legal size.
            if (readBufferSize < SignatureBuilder.MaximumChunkSize)
                throw new ArgumentOutOfRangeException(nameof(readBufferSize),
                    $"Read buffer size must be at least {SignatureBuilder.MaximumChunkSize} bytes.");

            ProgressReport = null;
            this.readBufferSize = readBufferSize;
        }

        public IProgress<ProgressReport> ProgressReport { get; set; }

        /// <summary>
        /// When enabled and the signature metadata contains a base file hash computed with the same
        /// algorithm as the new file verification hash, and both hashes are equal, the delta builder
        /// skips scanning the new file and writes a delta containing a single copy command spanning
        /// the whole basis file. This makes building deltas for unchanged files almost free.
        /// Note: the comparison relies on hash equality (MD5), so do not enable it if an adversary
        /// may supply colliding inputs. Signatures without a base file hash (e.g. the legacy
        /// Octodiff format) fall back to a full scan. Default: false.
        /// </summary>
        public bool SkipDeltaIfHashesMatch { get; set; }

        public void BuildDelta(Stream newFileStream, ISignatureReader signatureReader, IDeltaWriter deltaWriter)
        {
            var newFileVerificationHashAlgorithm = SupportedAlgorithms.Hashing.Md5();
            newFileStream.Seek(0, SeekOrigin.Begin);
            var newFileHash = newFileVerificationHashAlgorithm.ComputeHash(newFileStream);
            newFileStream.Seek(0, SeekOrigin.Begin);
            var newFileHashAlgorithmName = newFileVerificationHashAlgorithm.Name;
            // MD5 instances hold disposable resources and are only needed for the hash above.
            (newFileVerificationHashAlgorithm as IDisposable)?.Dispose();

            var signature = signatureReader.ReadSignature();

            var deltaMetadata = new DeltaMetadata
            {
                HashAlgorithm = signature.HashAlgorithm.Name,
                ExpectedFileHashAlgorithm = newFileHashAlgorithmName,
                ExpectedFileHash = Convert.ToBase64String(newFileHash),
                BaseFileHash = signature.Metadata.BaseFileHash,
                BaseFileHashAlgorithm = signature.Metadata.BaseFileHashAlgorithm,
                BaseFileLength = signature.Metadata.BaseFileLength,
                TargetFileLength = newFileStream.Length
            };

            if (SkipDeltaIfHashesMatch && BaseFileHashMatches(signature.Metadata, newFileHash, newFileHashAlgorithmName))
            {
                WriteUnchangedFileDelta(signature, deltaMetadata, deltaWriter);
                return;
            }

            var chunks = OrderChunksByChecksum(signature.Chunks);
            var chunkMap = CreateChunkMap(chunks, out int maxChunkSize, out int minChunkSize);

            deltaWriter.WriteMetadata(deltaMetadata);

            var checksumAlgorithm = signature.RollingChecksumAlgorithm;

            var buffer = new byte[readBufferSize];
            long lastMatchPosition = 0;

            var fileSize = newFileStream.Length;
            ProgressReport?.Report(new ProgressReport
            {
                Operation = ProgressOperationType.BuildingDelta,
                CurrentPosition = 0,
                Total = fileSize
            });

            while (true)
            {
                var startPosition = newFileStream.Position;
                var read = newFileStream.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                    break;

                ProgressReport?.Report(new ProgressReport
                {
                    Operation = ProgressOperationType.BuildingDelta,
                    CurrentPosition = startPosition,
                    Total = fileSize
                });
                
                uint checksum = 0;

                var remainingPossibleChunkSize = maxChunkSize;

                for (var i = 0; i < read - minChunkSize + 1; i++)
                {
                    var readSoFar = startPosition + i;

                    var remainingBytes = read - i;
                    if (remainingBytes < maxChunkSize)
                    {
                        remainingPossibleChunkSize = minChunkSize;
                    }

                    if (i == 0 || remainingBytes < maxChunkSize)
                    {
                        checksum = checksumAlgorithm.Calculate(buffer, i, remainingPossibleChunkSize);
                    }
                    else
                    {
                        var remove = buffer[i - 1];
                        var add = buffer[i + remainingPossibleChunkSize - 1];
                        checksum = checksumAlgorithm.Rotate(checksum, remove, add, remainingPossibleChunkSize);
                    }

                    if (readSoFar - (lastMatchPosition - remainingPossibleChunkSize) < remainingPossibleChunkSize)
                        continue;

                    if (!chunkMap.TryGetValue(checksum, out var startIndex)) 
                        continue;

                    for (var j = startIndex; j < chunks.Count && chunks[j].RollingChecksum == checksum; j++)
                    {
                        var chunk = chunks[j];
                        var hash = signature.HashAlgorithm.ComputeHash(buffer, i, remainingPossibleChunkSize);

                        if (ByteArrayEquality.AreEqual(hash, chunks[j].Hash))
                        {
                            readSoFar += remainingPossibleChunkSize;

                            var missing = readSoFar - lastMatchPosition;
                            if (missing > remainingPossibleChunkSize)
                            {
                                deltaWriter.WriteDataCommand(newFileStream, lastMatchPosition, missing - remainingPossibleChunkSize);
                            }

                            deltaWriter.WriteCopyCommand(new DataRange(chunk.StartOffset, chunk.Length));
                            lastMatchPosition = readSoFar;
                            break;
                        }
                    }
                }

                if (read < buffer.Length)
                {
                    break;
                }

                newFileStream.Position = newFileStream.Position - maxChunkSize + 1;
            }

            if (newFileStream.Length != lastMatchPosition)
            {
                deltaWriter.WriteDataCommand(newFileStream, lastMatchPosition, newFileStream.Length - lastMatchPosition);
            }

            deltaWriter.Finish();
        }

        public Task BuildDeltaAsync(Stream newFileStream, ISignatureReader signatureReader, IDeltaWriter deltaWriter) =>
            BuildDeltaAsync(newFileStream, signatureReader, deltaWriter, CancellationToken.None);

        public async Task BuildDeltaAsync(Stream newFileStream, ISignatureReader signatureReader, IDeltaWriter deltaWriter, CancellationToken cancellationToken)
        {
            var newFileVerificationHashAlgorithm = SupportedAlgorithms.Hashing.Md5();
            newFileStream.Seek(0, SeekOrigin.Begin);
            var newFileHash = await newFileVerificationHashAlgorithm.ComputeHashAsync(newFileStream, cancellationToken).ConfigureAwait(false);
            newFileStream.Seek(0, SeekOrigin.Begin);
            var newFileHashAlgorithmName = newFileVerificationHashAlgorithm.Name;
            // MD5 instances hold disposable resources and are only needed for the hash above.
            (newFileVerificationHashAlgorithm as IDisposable)?.Dispose();

            var signature = signatureReader.ReadSignature();

            var deltaMetadata = new DeltaMetadata
            {
                HashAlgorithm = signature.HashAlgorithm.Name,
                ExpectedFileHashAlgorithm = newFileHashAlgorithmName,
                ExpectedFileHash = Convert.ToBase64String(newFileHash),
                BaseFileHash = signature.Metadata.BaseFileHash,
                BaseFileHashAlgorithm = signature.Metadata.BaseFileHashAlgorithm,
                BaseFileLength = signature.Metadata.BaseFileLength,
                TargetFileLength = newFileStream.Length
            };

            if (SkipDeltaIfHashesMatch && BaseFileHashMatches(signature.Metadata, newFileHash, newFileHashAlgorithmName))
            {
                WriteUnchangedFileDelta(signature, deltaMetadata, deltaWriter);
                return;
            }

            var chunks = OrderChunksByChecksum(signature.Chunks);
            var chunkMap = CreateChunkMap(chunks, out int maxChunkSize, out int minChunkSize);

            deltaWriter.WriteMetadata(deltaMetadata);

            var checksumAlgorithm = signature.RollingChecksumAlgorithm;

            var buffer = new byte[readBufferSize];
            long lastMatchPosition = 0;

            var fileSize = newFileStream.Length;
            ProgressReport?.Report(new ProgressReport
            {
                Operation = ProgressOperationType.BuildingDelta,
                CurrentPosition = 0,
                Total = fileSize
            });

            while (true)
            {
                var startPosition = newFileStream.Position;
                var read = await newFileStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                    break;

                ProgressReport?.Report(new ProgressReport
                {
                    Operation = ProgressOperationType.BuildingDelta,
                    CurrentPosition = startPosition,
                    Total = fileSize
                });

                uint checksum = 0;

                var remainingPossibleChunkSize = maxChunkSize;

                for (var i = 0; i < read - minChunkSize + 1; i++)
                {
                    var readSoFar = startPosition + i;

                    var remainingBytes = read - i;
                    if (remainingBytes < maxChunkSize)
                    {
                        remainingPossibleChunkSize = minChunkSize;
                    }

                    if (i == 0 || remainingBytes < maxChunkSize)
                    {
                        checksum = checksumAlgorithm.Calculate(buffer, i, remainingPossibleChunkSize);
                    }
                    else
                    {
                        var remove = buffer[i - 1];
                        var add = buffer[i + remainingPossibleChunkSize - 1];
                        checksum = checksumAlgorithm.Rotate(checksum, remove, add, remainingPossibleChunkSize);
                    }

                    if (readSoFar - (lastMatchPosition - remainingPossibleChunkSize) < remainingPossibleChunkSize)
                        continue;

                    if (!chunkMap.TryGetValue(checksum, out var startIndex))
                        continue;

                    for (var j = startIndex; j < chunks.Count && chunks[j].RollingChecksum == checksum; j++)
                    {
                        var chunk = chunks[j];
                        var hash = signature.HashAlgorithm.ComputeHash(buffer, i, remainingPossibleChunkSize);

                        if (ByteArrayEquality.AreEqual(hash, chunks[j].Hash))
                        {
                            readSoFar += remainingPossibleChunkSize;

                            var missing = readSoFar - lastMatchPosition;
                            if (missing > remainingPossibleChunkSize)
                            {
                                await deltaWriter.WriteDataCommandAsync(newFileStream, lastMatchPosition, missing - remainingPossibleChunkSize, cancellationToken).ConfigureAwait(false);
                            }

                            deltaWriter.WriteCopyCommand(new DataRange(chunk.StartOffset, chunk.Length));
                            lastMatchPosition = readSoFar;
                            break;
                        }
                    }
                }

                if (read < buffer.Length)
                {
                    break;
                }

                newFileStream.Position = newFileStream.Position - maxChunkSize + 1;
            }

            if (newFileStream.Length != lastMatchPosition)
            {
                await deltaWriter.WriteDataCommandAsync(newFileStream, lastMatchPosition, newFileStream.Length - lastMatchPosition, cancellationToken).ConfigureAwait(false);
            }

            deltaWriter.Finish();
        }

        private static bool BaseFileHashMatches(SignatureMetadata signatureMetadata, byte[] newFileHash, string newFileHashAlgorithmName)
        {
            if (string.IsNullOrEmpty(signatureMetadata.BaseFileHash) ||
                signatureMetadata.BaseFileHashAlgorithm != newFileHashAlgorithmName)
                return false;

            byte[] baseFileHash;
            try
            {
                baseFileHash = Convert.FromBase64String(signatureMetadata.BaseFileHash);
            }
            catch (FormatException)
            {
                return false;
            }

            return ByteArrayEquality.AreEqual(baseFileHash, newFileHash);
        }

        private static void WriteUnchangedFileDelta(Signature.Signature signature, DeltaMetadata deltaMetadata, IDeltaWriter deltaWriter)
        {
            deltaWriter.WriteMetadata(deltaMetadata);

            long baseFileLength = 0;
            foreach (var chunk in signature.Chunks)
            {
                baseFileLength += chunk.Length;
            }

            if (baseFileLength > 0)
            {
                deltaWriter.WriteCopyCommand(new DataRange(0, baseFileLength));
            }

            deltaWriter.Finish();
        }

        private static List<ChunkSignature> OrderChunksByChecksum(List<ChunkSignature> chunks)
        {
            chunks.Sort(new ChunkSignatureChecksumComparer());
            return chunks;
        }

        private Dictionary<uint, int> CreateChunkMap(IList<ChunkSignature> chunks, out int maxChunkSize, out int minChunkSize)
        {
            ProgressReport?.Report(new ProgressReport
            {
                Operation = ProgressOperationType.CreatingChunkMap,
                CurrentPosition = 0,
                Total = chunks.Count
            });

            maxChunkSize = 0;
            minChunkSize = int.MaxValue;

            var chunkMap = new Dictionary<uint, int>();
            for (var i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                if (chunk.Length > maxChunkSize)
                {
                    maxChunkSize = chunk.Length;
                }

                if (chunk.Length < minChunkSize)
                {
                    minChunkSize = chunk.Length;
                }

                if (!chunkMap.ContainsKey(chunk.RollingChecksum))
                {
                    chunkMap[chunk.RollingChecksum] = i;
                }

                ProgressReport?.Report(new ProgressReport
                {
                    Operation = ProgressOperationType.CreatingChunkMap,
                    CurrentPosition = i,
                    Total = chunks.Count
                });
            }
            return chunkMap;
        }
    }
}