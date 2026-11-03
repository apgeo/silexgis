// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Infrastructure.Files;

namespace SilexGis.Api.Tests;

/// <summary>
/// The local file store on its own, for what a request cannot provoke on demand: a write
/// cancelled part way, a second writer arriving while the first holds the content, and a
/// storage path that tries to leave the store's root.
/// </summary>
public sealed class LocalFileStoreTests : IDisposable
{
    private readonly string parent = Path.Combine(TestScratch.Root, $"silexgis-test-filestore-{Guid.NewGuid():N}");
    private readonly LocalFileStore store;

    public LocalFileStoreTests()
    {
        store = new LocalFileStore(Options.Create(new FilesOptions { Root = Path.Combine(parent, "files") }));
    }

    public void Dispose()
    {
        if (Directory.Exists(parent))
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public async Task A_cancelled_piece_is_cut_back_out_of_the_content()
    {
        var first = RandomNumberGenerator.GetBytes(1000);
        await using (var writer = await store.OpenExclusiveAsync("2026/11/a.bin"))
        {
            (await writer.WriteAtAsync(0, new MemoryStream(first), first.Length)).ShouldBeTrue();

            using var cancel = new CancellationTokenSource();
            var second = new CancelAfterStream(RandomNumberGenerator.GetBytes(1000), deliver: 400, cancel);
            await Should.ThrowAsync<OperationCanceledException>(
                () => writer.WriteAtAsync(1000, second, 1000, cancel.Token));

            writer.Length.ShouldBe(1000);
        }

        (await File.ReadAllBytesAsync(store.GetAbsolutePath("2026/11/a.bin"))).ShouldBe(first);
    }

    [Fact]
    public async Task A_piece_that_ends_early_is_not_kept_and_a_write_before_the_end_replaces_the_rest()
    {
        var bytes = RandomNumberGenerator.GetBytes(1000);
        await using var writer = await store.OpenExclusiveAsync("2026/11/b.bin");
        (await writer.WriteAtAsync(0, new MemoryStream(bytes[..600]), 600)).ShouldBeTrue();

        // Announced as 400 bytes, delivered as 100.
        (await writer.WriteAtAsync(600, new MemoryStream(bytes[600..700]), 400)).ShouldBeFalse();
        writer.Length.ShouldBe(600);

        // Bytes past the offset are the remains of a piece nobody recorded; they are written over.
        (await writer.WriteAtAsync(500, new MemoryStream(bytes[500..]), 500)).ShouldBeTrue();
        writer.Length.ShouldBe(1000);

        // Writing past the end would leave a hole.
        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            () => writer.WriteAtAsync(1001, new MemoryStream([1]), 1));
    }

    [Fact]
    public async Task A_second_writer_is_refused_while_the_first_holds_the_content()
    {
        await using (await store.OpenExclusiveAsync("2026/11/c.bin"))
        {
            await Should.ThrowAsync<IOException>(() => store.OpenExclusiveAsync("2026/11/c.bin"));
        }

        // Released with the first writer.
        await using var again = await store.OpenExclusiveAsync("2026/11/c.bin");
        again.Length.ShouldBe(0);
    }

    [Fact]
    public void A_storage_path_may_not_leave_the_root()
    {
        Should.Throw<InvalidOperationException>(() => store.GetAbsolutePath("../outside.bin"));

        // A sibling directory whose name starts with the root's is still outside it.
        Should.Throw<InvalidOperationException>(() => store.GetAbsolutePath("../files-x/f.bin"));

        store.GetAbsolutePath("2026/11/d.bin")
            .ShouldBe(Path.Combine(parent, "files", "2026", "11", "d.bin"));
    }

    [Fact]
    public async Task A_body_that_fails_part_way_is_a_piece_not_received_and_is_cut_back_out()
    {
        var first = RandomNumberGenerator.GetBytes(1000);
        await using var writer = await store.OpenExclusiveAsync("2026/11/e.bin");
        (await writer.WriteAtAsync(0, new MemoryStream(first), first.Length)).ShouldBeTrue();

        // The sender's connection drops 400 bytes in: not the store's fault, so no exception.
        var dropped = new FailAfterStream(RandomNumberGenerator.GetBytes(1000), deliver: 400);
        (await writer.WriteAtAsync(1000, dropped, 1000)).ShouldBeFalse();
        writer.Length.ShouldBe(1000);
    }

    [Fact]
    public void A_store_rooted_at_a_volume_root_still_finds_its_own_paths()
    {
        // Path arithmetic only: nothing is written at the volume root.
        var volume = Path.GetPathRoot(parent)!;
        var atRoot = new LocalFileStore(Options.Create(new FilesOptions { Root = volume }));

        atRoot.GetAbsolutePath("2026/11/f.bin").ShouldBe(Path.Combine(volume, "2026", "11", "f.bin"));
    }

    [Theory]
    [InlineData(".PDF", ".pdf")]
    [InlineData("jpg", ".jpg")]
    [InlineData(".tar.gz", "")]
    [InlineData("./../x", "")]
    [InlineData(".averyverylongone", "")]
    [InlineData("", "")]
    public async Task A_stored_name_keeps_only_a_short_plain_extension(string extension, string kept)
    {
        using var bytes = new MemoryStream([1, 2, 3]);
        var stored = await store.SaveAsync(bytes, extension);

        // The rest of the name is the store's own, so the extension is the only part a caller
        // chooses — and it can neither carry a separator nor anything a file system reads twice.
        Path.GetExtension(stored).ShouldBe(kept);
        Path.GetFileNameWithoutExtension(stored).Length.ShouldBe(32);
        File.Exists(store.GetAbsolutePath(stored)).ShouldBeTrue();
    }

    /// <summary>Delivers part of a piece, then fails as a dropped connection does.</summary>
    private sealed class FailAfterStream(byte[] piece, int deliver) : Stream
    {
        private int position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => piece.Length;
        public override long Position { get => position; set => throw new NotSupportedException(); }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (position >= deliver)
            {
                throw new IOException("The connection dropped part way through the piece.");
            }

            var count = Math.Min(buffer.Length, deliver - position);
            piece.AsMemory(position, count).CopyTo(buffer);
            position += count;
            return ValueTask.FromResult(count);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Delivers part of a piece, then the request is cancelled.</summary>
    private sealed class CancelAfterStream(byte[] piece, int deliver, CancellationTokenSource cancel) : Stream
    {
        private int position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => piece.Length;
        public override long Position { get => position; set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (position >= deliver)
            {
                await cancel.CancelAsync();
                cancellationToken.ThrowIfCancellationRequested();
            }

            var count = Math.Min(buffer.Length, deliver - position);
            piece.AsMemory(position, count).CopyTo(buffer);
            position += count;
            return count;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
