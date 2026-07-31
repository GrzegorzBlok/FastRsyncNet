using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Sas;
using FastRsync.Core;
using FastRsync.Delta;
using FastRsync.Signature;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace FastRsync.Tests;

// Validates the delta-plan (ReadCommands) against real Azure Blob storage, and proves the
// server-side "block assembly" design: reconstruct the target blob directly from the basis and
// delta blobs using Put Block From URL, driven by the plan's offsets - no file bytes flow through
// the client. Uses Azurite by default (UseDevelopmentStorage=true); set FASTRSYNC_BENCH_STORAGE
// to target a real account. Requires storage to be running, hence [Category("Integration")].
[TestFixture]
[Category("Integration")]
public class DeltaCommandReaderAzureBlobTests
{
    private const string ContainerName = "fastrsync-deltaplan-tests";
    private BlobContainerClient container;

    [OneTimeSetUp]
    public void SetUp()
    {
        var connectionString = Environment.GetEnvironmentVariable("FASTRSYNC_BENCH_STORAGE") ?? "UseDevelopmentStorage=true";
        container = new BlobServiceClient(connectionString).GetBlobContainerClient(ContainerName);
        container.CreateIfNotExists();
    }

    [Test]
    public async Task ReadCommands_OverBlobStream_ReconstructViaRangeReads()
    {
        var basis = CreateBytes(2 * 1024 * 1024, 1);
        var newFile = MakeRelatedFile(basis);
        var delta = BuildDelta(basis, newFile);

        var basisBlob = await UploadAsync(NewName(), basis, Md5(basis));
        var deltaBlob = await UploadAsync(NewName(), delta, contentMd5: null);

        // Read the plan straight off the delta blob's read stream.
        IReadOnlyList<DeltaCommand> plan;
        using (var deltaStream = await deltaBlob.OpenReadAsync())
        {
            plan = new BinaryDeltaReader(deltaStream, null).ReadCommands();
        }

        // Reconstruct via range downloads: copies from the basis blob, data from the delta blob.
        var output = new MemoryStream();
        foreach (var cmd in plan)
        {
            var source = cmd.Type == DeltaCommandType.CopyCommand ? basisBlob : deltaBlob;
            var range = await source.DownloadStreamingAsync(new BlobDownloadOptions { Range = new Azure.HttpRange(cmd.Offset, cmd.Length) });
            await range.Value.Content.CopyToAsync(output);
        }

        CollectionAssert.AreEqual(newFile, output.ToArray());
    }

    [Test]
    public async Task BlockAssembly_ViaPutBlockFromUrl_ProducesCorrectBlobWithMatchingMd5()
    {
        var basis = CreateBytes(3 * 1024 * 1024, 5);
        var newFile = MakeRelatedFile(basis);
        var delta = BuildDelta(basis, newFile);

        // Basis blob is uploaded with its Content-MD5 set (the server relies on this to verify the
        // basis identity before assembling).
        var basisBlob = await UploadAsync(NewName(), basis, Md5(basis));
        var deltaBlob = await UploadAsync(NewName(), delta, contentMd5: null);

        // Read plan + metadata off the delta blob.
        BinaryDeltaReader reader;
        IReadOnlyList<DeltaCommand> plan;
        using (var deltaStream = await deltaBlob.OpenReadAsync())
        {
            reader = new BinaryDeltaReader(deltaStream, null);
            plan = reader.ReadCommands();
        }

        // Integrity gate: the basis blob must be the one the delta was built against. This is a
        // pure metadata comparison (no content read).
        var basisProps = (await basisBlob.GetPropertiesAsync()).Value;
        Assert.That(Convert.ToBase64String(basisProps.ContentHash), Is.EqualTo(reader.Metadata.BaseFileHash), "basis blob MD5 must match the delta's BaseFileHash");
        Assert.That(basisProps.ContentLength, Is.EqualTo(reader.Metadata.BaseFileLength), "basis blob length must match the delta's BaseFileLength");

        // Assemble the target blob from the basis and delta blobs using Put Block From URL.
        var basisSas = basisBlob.GenerateSasUri(BlobSasPermissions.Read, DateTimeOffset.UtcNow.AddHours(1));
        var deltaSas = deltaBlob.GenerateSasUri(BlobSasPermissions.Read, DateTimeOffset.UtcNow.AddHours(1));
        var targetBlob = container.GetBlockBlobClient(NewName());

        var blockIds = new List<string>();
        var index = 0;
        foreach (var cmd in plan)
        {
            var blockId = Convert.ToBase64String(Encoding.UTF8.GetBytes(index.ToString("D6")));
            var sourceUri = cmd.Type == DeltaCommandType.CopyCommand ? basisSas : deltaSas;
            try
            {
                await targetBlob.StageBlockFromUriAsync(sourceUri, blockId,
                    new StageBlockFromUriOptions { SourceRange = new Azure.HttpRange(cmd.Offset, cmd.Length) });
            }
            catch (Azure.RequestFailedException ex) when (ex.Message.Contains("not implemented"))
            {
                // Azurite (and some emulators) do not implement Put Block From URL. This test is
                // meaningful only against a backend that supports it (real Azure). Skip cleanly.
                Assert.Ignore("Put Block From URL is not supported by this storage backend (e.g. Azurite). Set FASTRSYNC_BENCH_STORAGE to a real Azure account to run this test.");
            }
            blockIds.Add(blockId);
            index++;
        }

        // Commit, stamping the expected whole-file MD5 as the blob's Content-MD5 so downstream
        // property-based integrity checks keep working.
        await targetBlob.CommitBlockListAsync(blockIds, new CommitBlockListOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentHash = Convert.FromBase64String(reader.Metadata.ExpectedFileHash) }
        });

        // The assembled blob equals the target file exactly.
        var assembled = new MemoryStream();
        await (await targetBlob.DownloadStreamingAsync()).Value.Content.CopyToAsync(assembled);
        CollectionAssert.AreEqual(newFile, assembled.ToArray());

        // And its Content-MD5 property is the delta's expected hash (= MD5 of the new file).
        var targetProps = (await targetBlob.GetPropertiesAsync()).Value;
        Assert.That(Convert.ToBase64String(targetProps.ContentHash), Is.EqualTo(reader.Metadata.ExpectedFileHash));
        Assert.That(reader.Metadata.ExpectedFileHash, Is.EqualTo(Md5(newFile)));
    }

    private static string NewName() => Guid.NewGuid().ToString("N");

    private async Task<BlockBlobClient> UploadAsync(string name, byte[] content, string contentMd5)
    {
        var blob = container.GetBlockBlobClient(name);
        var options = new BlobUploadOptions();
        if (contentMd5 != null)
            options.HttpHeaders = new BlobHttpHeaders { ContentHash = Convert.FromBase64String(contentMd5) };
        using var ms = new MemoryStream(content);
        await blob.UploadAsync(ms, options);
        return blob;
    }

    private static byte[] MakeRelatedFile(byte[] basis)
    {
        var newFile = (byte[])basis.Clone();
        var patch = CreateBytes(64 * 1024, 99);
        Array.Copy(patch, 0, newFile, basis.Length / 2, patch.Length);
        return newFile;
    }

    private static byte[] BuildDelta(byte[] basis, byte[] newFile)
    {
        var signature = new MemoryStream();
        new SignatureBuilder().Build(new MemoryStream(basis), new SignatureWriter(signature));
        signature.Seek(0, SeekOrigin.Begin);

        var deltaStream = new MemoryStream();
        new DeltaBuilder().BuildDelta(new MemoryStream(newFile), new SignatureReader(signature, null),
            new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream)));
        return deltaStream.ToArray();
    }

    private static string Md5(byte[] data)
    {
        using var md5 = MD5.Create();
        return Convert.ToBase64String(md5.ComputeHash(data));
    }

    private static byte[] CreateBytes(int length, int seed)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }
}
