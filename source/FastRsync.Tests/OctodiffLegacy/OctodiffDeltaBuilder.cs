using System.Collections;
using System.Collections.Generic;
using System.IO;
using FastRsync.Core;
using FastRsync.Delta;

namespace FastRsync.Tests.OctodiffLegacy;

public class OctodiffDeltaBuilder
{
    private readonly int readBufferSize;

    public OctodiffDeltaBuilder(int readBufferSize = 4 * 1024 * 1024)
    {
        this.readBufferSize = readBufferSize;
    }

    public void BuildDelta(Stream newFileStream, IOctodiffSignatureReader signatureReader, IOctodiffDeltaWriter deltaWriter)
    {
        var signature = signatureReader.ReadSignature();
        var chunks = signature.Chunks;

        var newFileHash = signature.HashAlgorithm.ComputeHash(newFileStream);
        newFileStream.Seek(0, SeekOrigin.Begin);

        deltaWriter.WriteMetadata(signature.HashAlgorithm, newFileHash);

        chunks = OrderChunksByChecksum(chunks);

        var chunkMap = CreateChunkMap(chunks, out int maxChunkSize, out int minChunkSize);

        var buffer = new byte[readBufferSize];
        long lastMatchPosition = 0;

        while (true)
        {
            var startPosition = newFileStream.Position;
            var read = newFileStream.Read(buffer, 0, buffer.Length);
            if (read < 0)
                break;

            var checksumAlgorithm = signature.RollingChecksumAlgorithm;
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

                if (!chunkMap.ContainsKey(checksum))
                    continue;

                var startIndex = chunkMap[checksum];

                for (var j = startIndex; j < chunks.Count && chunks[j].RollingChecksum == checksum; j++)
                {
                    var chunk = chunks[j];
                    var hash = signature.HashAlgorithm.ComputeHash(buffer, i, remainingPossibleChunkSize);

                    if (StructuralComparisons.StructuralEqualityComparer.Equals(hash, chunks[j].Hash))
                    {
                        readSoFar = readSoFar + remainingPossibleChunkSize;

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

    private class ChunkSignatureChecksumComparer : IComparer<ChunkSignature>
    {
        public int Compare(ChunkSignature x, ChunkSignature y)
        {
            var comparison = x.RollingChecksum.CompareTo(y.RollingChecksum);
            return comparison == 0 ? x.StartOffset.CompareTo(y.StartOffset) : comparison;
        }
    }

    private static List<ChunkSignature> OrderChunksByChecksum(List<ChunkSignature> chunks)
    {
        chunks.Sort(new ChunkSignatureChecksumComparer());
        return chunks;
    }

    private Dictionary<uint, int> CreateChunkMap(IList<ChunkSignature> chunks, out int maxChunkSize, out int minChunkSize)
    {
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
        }
        return chunkMap;
    }
}