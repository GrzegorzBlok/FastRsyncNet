using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FastRsync.Delta;

namespace FastRsync.Core
{
    // This decorator turns any sequential copy operations into a single operation, reducing 
    // the size of the delta file.
    // For example:
    //   Copy: 0x0000 - 0x0400
    //   Copy: 0x0401 - 0x0800
    //   Copy: 0x0801 - 0x0C00
    // Gets turned into:
    //   Copy: 0x0000 - 0x0C00
    public class AggregateCopyOperationsDecorator : IDeltaWriter
    {
        private readonly IDeltaWriter decorated;
        private DataRange bufferedCopy;

        public AggregateCopyOperationsDecorator(IDeltaWriter decorated)
        {
            this.decorated = decorated;
        }

        public void WriteDataCommand(Stream source, long offset, long length)
        {
            FlushCurrentCopyCommand();
            decorated.WriteDataCommand(source, offset, length);
        }

        public async Task WriteDataCommandAsync(Stream source, long offset, long length, CancellationToken cancellationToken)
        {
            FlushCurrentCopyCommand();
            await decorated.WriteDataCommandAsync(source, offset, length, cancellationToken).ConfigureAwait(false);
        }

        public void WriteMetadata(DeltaMetadata metadata)
        {
            decorated.WriteMetadata(metadata);
        }

        public void WriteCopyCommand(DataRange chunk)
        {
            if (bufferedCopy.Length > 0 && bufferedCopy.StartOffset + bufferedCopy.Length == chunk.StartOffset)
            {
                bufferedCopy.Length += chunk.Length;
            }
            else
            {
                FlushCurrentCopyCommand();
                bufferedCopy = chunk;
            }
        }

        private void FlushCurrentCopyCommand()
        {
            if (bufferedCopy.Length <= 0)
            {
                return;
            }

            decorated.WriteCopyCommand(bufferedCopy);
            bufferedCopy = new DataRange();
        }

        public void Finish()
        {
            FlushCurrentCopyCommand();
            decorated.Finish();
        }
    }
}