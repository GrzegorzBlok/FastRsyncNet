# FastRsyncNet - C# delta syncing library

FastRsyncNet is a .NET library for efficient file synchronization based on the Rsync algorithm. Instead of transferring a whole file when it changes, it transfers only the parts that differ, which makes it well suited for keeping large files in sync over the network (for example content stored in Azure Blob Storage).

It works in three steps:

1. **Signature** - from a *basis* file (the version the other side already has) you compute a compact signature describing its blocks.
2. **Delta** - given the signature and the *new* file, you compute a delta containing only the blocks that changed plus instructions to copy the unchanged blocks.
3. **Patch** - the delta is applied to the basis file to reconstruct the new file, and the result is verified with a whole-file hash.

Capabilities at a glance:

* Multiple hashing algorithms (xxHash64, xxHash3, SHA1, MD5) - the default xxHash64 is much faster and produces smaller signatures than SHA1.
* Multiple rolling checksum algorithms with different speed/quality trade-offs.
* Fully synchronous **and** asynchronous (`async`/`await`, with `CancellationToken`) APIs.
* Streaming design that works directly on network-backed streams such as Azure Blob.
* Single-pass signature building that reads the basis file only once - roughly twice as fast over the network.
* Optional rsync-friendly GZip compression via the companion `FastRsyncNet.Compression` package.

## Install [![NuGet](https://img.shields.io/nuget/v/FastRsyncNet.svg?style=flat)](https://www.nuget.org/packages/FastRsyncNet/)
Add to a project via NuGet:
1. Right click on a project and click 'Manage NuGet Packages'.
2. Search for 'FastRsyncNet' and click 'Install'.

Supported target frameworks: .NET Framework 4.6.2, .NET Standard 2.0, and .NET 7.0 / 8.0 / 9.0 / 10.0.

## Examples

### Calculating signature

```csharp
using FastRsync.Signature;

...

var signatureBuilder = new SignatureBuilder(SupportedAlgorithms.Hashing.XxHash3(), SupportedAlgorithms.Checksum.Adler32RollingV3());
using (var basisStream = new FileStream(basisFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
using (var signatureStream = new FileStream(signatureFilePath, FileMode.Create, FileAccess.Write, FileShare.Read))
{
    signatureBuilder.Build(basisStream, new SignatureWriter(signatureStream));
}
```

### Calculating delta

```csharp
using FastRsync.Core;
using FastRsync.Delta;
using FastRsync.Diagnostics;
using FastRsync.Signature;

...

var deltaBuilder = new DeltaBuilder();
deltaBuilder.ProgressReport = new ConsoleProgressReporter();
using (var newFileStream = new FileStream(newFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
using (var signatureStream = new FileStream(signatureFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
using (var deltaStream = new FileStream(deltaFilePath, FileMode.Create, FileAccess.Write, FileShare.Read))
{
    deltaBuilder.BuildDelta(newFileStream, new SignatureReader(signatureStream, null),
        new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream)));
}
```

### Patching (applying delta)

By default the applied result is verified: the whole-file hash stored in the delta is compared against the hash of the reconstructed file, and a mismatch throws. See [Security considerations](#security-considerations) for what that check does and does not guarantee.

```csharp
using FastRsync.Delta;

...

var deltaApplier = new DeltaApplier();
using (var basisStream = new FileStream(basisFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
using (var deltaStream = new FileStream(deltaFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
using (var newFileStream = new FileStream(newFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
{
    deltaApplier.Apply(basisStream, new BinaryDeltaReader(deltaStream, null), newFileStream);
}
```

Set `deltaApplier.SkipHashCheck = true` to skip the verification (for example when the output stream is not seekable, since the check needs to re-read the output).

### Asynchronous usage

All of the operations above have `async` counterparts (`BuildAsync`, `BuildDeltaAsync`, `ApplyAsync`), each accepting an optional `CancellationToken`. This is the preferred path for network-backed streams.

```csharp
using FastRsync.Delta;
using FastRsync.Signature;

...

var deltaBuilder = new DeltaBuilder();
using (var newFileStream = new FileStream(newFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
using (var signatureStream = new FileStream(signatureFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
using (var deltaStream = new FileStream(deltaFilePath, FileMode.Create, FileAccess.Write, FileShare.Read))
{
    await deltaBuilder.BuildDeltaAsync(newFileStream, new SignatureReader(signatureStream, null),
        new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream)), cancellationToken);
}
```

### Calculating signature on network stream (Azure blobs)

The examples below use the modern `Azure.Storage.Blobs` SDK. The input stream must be seekable; `OpenReadAsync` returns a seekable stream, so blobs work directly.

```csharp
using Azure.Storage.Blobs;
using FastRsync.Signature;

...

var containerClient = new BlobServiceClient("azure storage connectionstring").GetBlobContainerClient("containerName");
var basisBlob = containerClient.GetBlockBlobClient("blobName");
var signatureBlob = containerClient.GetBlockBlobClient("blob_signature");

var signatureBuilder = new SignatureBuilder(SupportedAlgorithms.Hashing.XxHash3(), SupportedAlgorithms.Checksum.Adler32RollingV3());
using (var basisStream = await basisBlob.OpenReadAsync())
using (var signatureStream = await signatureBlob.OpenWriteAsync(overwrite: true))
{
    await signatureBuilder.BuildAsync(basisStream, new SignatureWriter(signatureStream), cancellationToken);
}
```

## Performance tuning

* **`SignatureBuilder.SinglePassBuild`** (default `true`) - the basis file is read only once; the verification hash is computed incrementally while the chunk signatures are gathered. This roughly halves the I/O and is about twice as fast when the basis file is a network-backed stream. The produced signature is byte-for-byte identical to the two-pass output. It buffers the chunk signatures in memory (about 1% of the basis file size); set it to `false` to stream them directly at the cost of reading the basis file twice.
* **`DeltaBuilder.SkipDeltaIfHashesMatch`** (default `false`, opt-in) - when the new file's hash matches the basis file hash recorded in the signature, delta building skips scanning the file entirely and emits a minimal delta. Building deltas for unchanged files becomes almost free. Because it relies on hash equality, do not enable it if an adversary might supply colliding inputs.
* **Metadata file lengths** - signatures record the basis file length; deltas record the basis and target file lengths. `DeltaApplier` uses the target length to preallocate the output when writing to a fresh seekable stream. These fields are ignored by older versions.
* **`UseBufferPool`** on `DeltaBuilder`, `DeltaApplier` and `BinaryDeltaReader` (default `true`) - rents the multi-megabyte working buffer from the shared array pool instead of allocating it per operation. This removes a large-object-heap allocation per operation, which reduces GC pressure noticeably when many operations run in the same process (e.g. a server). It does not change the output. Set it to `false` for one-shot or memory-sensitive local use, where the pool retaining buffers between operations is not worthwhile.

## Available algorithms and relative performance

Following signature hashing algorithms are available:

 * XxHash64 - default algorithm, signature size 6.96 MB, signature calculation time 5209 ms.
 * XxHash3 - signature size 6.96 MB, signature calculation time 5024 ms. For all new use cases, use this one.
 * SHA1 - signature size 12.9 MB, signature calculation time 6519 ms.
 * MD5 - originally used in Rsync program, signature size 10.9 MB, signature calculation time 6767 ms.

The signature sizes and calculation times are relative indicators only, measured against a 0.99 GB file on one machine; real performance on your system will vary greatly.

Following rolling checksum algorithms are available:

 * Adler32RollingChecksum - default algorithm, it uses low level optimization that makes it faster but provides worse quality of checksum.
 * Adler32RollingChecksumV2 - Obsolete (using it produces a compiler warning). It has a bug that - while it does not make any data incorrect - results in unnecessarily big deltas. Do not use it, unless you need to for backward compatibility. It is the original (but incorrectly implemented) Adler32 algorithm (slower but better quality of checksum).
 * Adler32RollingChecksumV3 - for all new use cases, use this one. It is fast and has the best quality of checksum.

## GZip compression that is rsync compatible [![NuGet](https://img.shields.io/nuget/v/FastRsyncNet.Compression.svg?style=flat)](https://www.nuget.org/packages/FastRsyncNet.Compression/)
If you synchronize a compressed file, a small change in a compressed file may force rsync algorithm to synchronize the whole compressed file, instead of just the changed blocks. To fix this, a custom GZip compression method may be used that periodically resets the compressor state to make it block-sync friendly. Install [FastRsyncNet.Compression](https://www.nuget.org/packages/FastRsyncNet.Compression/) package and use following method:
```csharp
FastRsync.Compression.GZip.Compress(Stream sourceStream, Stream destStream)
```
To uncompress you may use any GZip method (e.g. System.IO.Compression.GZipStream).

## Compatibility

FastRsyncNet is derived from the [Octodiff](https://github.com/OctopusDeploy/Octodiff) tool. Unlike Octodiff, which is based on the SHA1 algorithm, FastRsyncNet offers a variety of hashing algorithms to choose from - the default xxHash64 offers significantly faster calculations and a smaller signature size than SHA1 while still providing sufficient hash quality. FastRsyncNet also supports SHA1 and is 100% compatible with signatures and deltas produced by Octodiff.

Version guarantees:

* FastRsyncNet 2.x can read signatures and deltas produced by FastRsyncNet 1.x and by Octodiff.
* Files produced by FastRsyncNet 2.x are **not** recognized by FastRsyncNet 1.x (the signature and delta format changed in 2.0.0).
* All 2.x releases are mutually compatible: files produced by any 2.x version can be read by any other 2.x version. Newer 2.x releases may add optional fields to the metadata, which older 2.x readers safely ignore.

## Security considerations

The verification performed while applying a delta compares the reconstructed file against a hash that is stored **inside the delta**. This reliably detects a corrupted delta or a basis file that does not match the one the delta was built against, but it does **not** authenticate the delta itself - an attacker who supplies the delta also controls the hash it is checked against. If deltas can come from an untrusted source, authenticate them out of band (for example with a digital signature or MAC over the delta bytes) before applying them.

## License

FastRsyncNet is licensed under the [Apache License 2.0](LICENSE).
