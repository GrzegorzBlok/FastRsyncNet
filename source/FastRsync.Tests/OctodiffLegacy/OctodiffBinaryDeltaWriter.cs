using System;
using System.IO;
using FastRsync.Delta;
using FastRsync.Hash;

namespace FastRsync.Tests.OctodiffLegacy;

public class OctodiffBinaryDeltaWriter : IOctodiffDeltaWriter
{
    private readonly BinaryWriter writer;
    private readonly int readWriteBufferSize;

    public OctodiffBinaryDeltaWriter(Stream stream, int readWriteBufferSize = 1024 * 1024)
    {
        writer = new BinaryWriter(stream);
        this.readWriteBufferSize = readWriteBufferSize;
    }

    public void WriteMetadata(IHashAlgorithm hashAlgorithm, byte[] expectedNewFileHash)
    {
        writer.Write(OctodiffBinaryFormat.DeltaHeader);
        writer.Write(OctodiffBinaryFormat.Version);
        writer.Write(hashAlgorithm.Name);
        writer.Write(expectedNewFileHash.Length);
        writer.Write(expectedNewFileHash);
        writer.Write(OctodiffBinaryFormat.EndOfMetadata);
    }

    public void WriteCopyCommand(DataRange segment)
    {
        writer.Write(OctodiffBinaryFormat.CopyCommand);
        writer.Write(segment.StartOffset);
        writer.Write(segment.Length);
    }

    public void WriteDataCommand(Stream source, long offset, long length)
    {
        writer.Write(OctodiffBinaryFormat.DataCommand);
        writer.Write(length);

        var originalPosition = source.Position;
        try
        {
            source.Seek(offset, SeekOrigin.Begin);

            var buffer = new byte[Math.Min((int)length, readWriteBufferSize)];

            int read;
            long soFar = 0;
            while ((read = source.Read(buffer, 0, (int)Math.Min(length - soFar, buffer.Length))) > 0)
            {
                soFar += read;
                writer.Write(buffer, 0, read);
            }
        }
        finally
        {
            source.Seek(originalPosition, SeekOrigin.Begin);
        }
    }
        
    public void Finish()
    {
    }
}