# FastRsyncNet - C# delta syncing library

The Fast Rsync .NET library is Rsync implementation derived from [Octodiff](https://github.com/OctopusDeploy/Octodiff) tool.

Unlike the Octodiff which is based on SHA1 algorithm, the FastRsyncNet allows a variety of hashing algorithms to choose from.
The default one, that is xxHash64, offers significantly faster calculations and smaller signature size than the SHA1, while still providing sufficient quality of hash results.

FastRsyncNet supports also SHA1 and is 100% compatible with signatures and deltas produced by Octodiff.

Since version 2.0.0 the signature and delta format has changed. FastRsyncNet 2.x is still able to work with signatures and deltas from FastRsync 1.x and Octodiff. However, files made with FastRsyncNet 2.x are not going to be recognized by FastRsyncNet 1.x.

## Install [![NuGet](https://img.shields.io/nuget/v/FastRsyncNet.svg?style=flat)](https://www.nuget.org/packages/FastRsyncNet/)
Add To project via NuGet:  
1. Right click on a project and click 'Manage NuGet Packages'.  
2. Search for 'FastRsyncNet' and click 'Install'.  

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
using FastRsync.Delta;

...

var delta = new DeltaBuilder();
builder.ProgressReport = new ConsoleProgressReporter();
using (var newFileStream = new FileStream(newFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
using (var signatureStream = new FileStream(signatureFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
using (var deltaStream = new FileStream(deltaFilePath, FileMode.Create, FileAccess.Write, FileShare.Read))
{
    delta.BuildDelta(newFileStream, new SignatureReader(signatureStream, delta.ProgressReporter), new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream)));
}
```

### Patching (applying delta)

```csharp
using FastRsync.Delta;

...

var delta = new DeltaApplier
        {
            SkipHashCheck = true
        };
using (var basisStream = new FileStream(basisFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
using (var deltaStream = new FileStream(deltaFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
using (var newFileStream = new FileStream(newFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
{
    delta.Apply(basisStream, new BinaryDeltaReader(deltaStream, progressReporter), newFileStream);
}
```
### Calculating signature on Azure blobs

FastRsyncNet might not work on Azure Storage emulator due to issues with stream seeking.

```csharp
using FastRsync.Signature;

...

var storageAccount = CloudStorageAccount.Parse("azure storage connectionstring");
var blobClient = storageAccount.CreateCloudBlobClient();
var blobsContainer = blobClient.GetContainerReference("containerName");
var basisBlob = blobsContainer.GetBlockBlobReference("blobName");

var signatureBlob = container.GetBlockBlobReference("blob_signature");

var signatureBuilder = new SignatureBuilder(SupportedAlgorithms.Hashing.XxHash3(), SupportedAlgorithms.Checksum.Adler32RollingV3());
using (var signatureStream = await signatureBlob.OpenWriteAsync())
using (var basisStream = await basisBlob.OpenReadAsync())
{
    await signatureBuilder.BuildAsync(basisStream, new SignatureWriter(signatureStream));
}
```

## Available algorithms and relative performance

Following signature hashing algorithms are available:

 * XxHash64 - default algorithm, signature size 6.96 MB, signature calculation time 5209 ms.
 * XxHash3 - signature size 6.96 MB, signature calculation time 5024 ms. For all new use cases, use this one.
 * SHA1 - signature size 12.9 MB, signature calculation time 6519 ms.
 * MD5 - originally used in Rsync program, signature size 10.9 MB, signature calculation time 6767 ms.

The signature sizes and calculation times are to provide some insights on relative perfomance. The real perfomance on your system will vary greatly. The benchmark had been run against 0.99 GB file.

Following rolling checksum algorithms are available:

 * Adler32RollingChecksum - default algorithm, it uses low level optimization that makes it faster but provides worse quality of checksum.
 * Adler32RollingChecksumV2 - Obsolete. It has a bug that - while does not make any data incorrect - results in unnecessary big deltas. Do not use it, unless you need to due to the backward compatibility. It is the original (but incorrectly implemented) Adler32 algorithm implementation (slower but better quality of checksum).
 * Adler32RollingChecksumV3 - for all new use cases, use this one. It is fast and has best quality of checksum.

## GZip compression that is rsync compatible [![NuGet](https://img.shields.io/nuget/v/FastRsyncNet.Compression.svg?style=flat)](https://www.nuget.org/packages/FastRsyncNet.Compression/)
If you synchronize a compressed file, a small change in a compressed file may force rsync algorithm to synchronize whole compressed file, instead of just the changed blocks. To fix this, a custom GZip compression method may be used that periodically reset the compressor state to make it block-sync friendly. Install [FastRsyncNet.Compression](https://www.nuget.org/packages/FastRsyncNet.Compression/) package and use following method:
```csharp
FastRsync.Compression.GZip.Compress(Stream sourceStream, Stream destStream)
```
To uncompress you may use any GZip method (e.g. System.IO.Compression.GZipStream).
