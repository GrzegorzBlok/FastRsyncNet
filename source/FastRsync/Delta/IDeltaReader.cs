using System;
using System.Threading;
using System.Threading.Tasks;
using FastRsync.Hash;
using FastRsync.Signature;

namespace FastRsync.Delta
{
    public interface IDeltaReader
    {
        byte[] ExpectedHash { get; }
        IHashAlgorithm HashAlgorithm { get; }
        DeltaMetadata Metadata { get; }
        RsyncFormatType Type { get; }
        void Apply(Action<byte[]> writeData, Action<long, long> copy);
        Task ApplyAsync(Func<byte[], Task> writeData, Func<long, long, Task> copy);
        Task ApplyAsync(Func<byte[], Task> writeData, Func<long, long, Task> copy, CancellationToken cancellationToken);
    }
}