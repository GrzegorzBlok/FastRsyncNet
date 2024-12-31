using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FastRsync.Hash;

namespace FastRsync.Delta
{
    public interface IDeltaWriter
    {
        void WriteMetadata(DeltaMetadata metadata);
        void WriteCopyCommand(DataRange segment);
        void WriteDataCommand(Stream source, long offset, long length);
        Task WriteDataCommandAsync(Stream source, long offset, long length, CancellationToken cancellationToken);
        void Finish();
    }
}