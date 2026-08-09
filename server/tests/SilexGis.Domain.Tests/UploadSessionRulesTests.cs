// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Documents;
using static SilexGis.Domain.Documents.UploadSessionRules;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The arithmetic of a resumable upload. These are the cases that are awkward to provoke over
/// a real connection and are the whole reason the mechanism exists: a piece that arrives
/// twice, a client resuming from a stale offset, a declared size that turns out to be a lie.
/// </summary>
public class UploadSessionRulesTests
{
    [Fact]
    public void A_piece_that_continues_the_file_is_appended()
    {
        Decide(receivedBytes: 0, offset: 0, chunkLength: 10, declaredSizeBytes: 30)
            .ShouldBe(ChunkDisposition.Append);
        Decide(receivedBytes: 10, offset: 10, chunkLength: 20, declaredSizeBytes: 30)
            .ShouldBe(ChunkDisposition.Append);
    }

    [Fact]
    public void A_replayed_piece_is_a_no_op_rather_than_an_error()
    {
        // The case this exists for: the client sent it, never saw the answer, and sent it
        // again. Refusing the second copy would end the upload every time a response is lost.
        Decide(receivedBytes: 30, offset: 0, chunkLength: 10, declaredSizeBytes: 30)
            .ShouldBe(ChunkDisposition.AlreadyHeld);
        Decide(receivedBytes: 30, offset: 20, chunkLength: 10, declaredSizeBytes: 30)
            .ShouldBe(ChunkDisposition.AlreadyHeld);
    }

    [Fact]
    public void A_piece_overlapping_the_boundary_is_refused_rather_than_partly_believed()
    {
        // Accepting it would mean assuming the resent bytes match the stored ones. The client
        // is told where the file ends and sends from there, which costs one round trip and
        // assumes nothing.
        Decide(receivedBytes: 20, offset: 10, chunkLength: 20, declaredSizeBytes: 40)
            .ShouldBe(ChunkDisposition.OutOfOrder);
    }

    [Fact]
    public void A_gap_is_refused_because_bytes_are_appended_rather_than_placed()
    {
        Decide(receivedBytes: 10, offset: 20, chunkLength: 10, declaredSizeBytes: 40)
            .ShouldBe(ChunkDisposition.OutOfOrder);
        Decide(receivedBytes: 10, offset: -1, chunkLength: 10, declaredSizeBytes: 40)
            .ShouldBe(ChunkDisposition.OutOfOrder);
    }

    [Fact]
    public void Pieces_adding_up_past_the_declared_size_are_refused()
    {
        // The declaration was checked against the installation's limit before any bytes were
        // accepted, so a client that could exceed it here would have got past that check.
        Decide(receivedBytes: 20, offset: 20, chunkLength: 20, declaredSizeBytes: 30)
            .ShouldBe(ChunkDisposition.Overflow);
    }

    [Fact]
    public void An_empty_piece_at_the_end_of_the_file_is_held_and_one_anywhere_else_is_not()
    {
        // A zero-length piece says nothing; at the current end it is harmless, and elsewhere
        // it is a client that has lost track of where it is and should be told so.
        Decide(receivedBytes: 10, offset: 10, chunkLength: 0, declaredSizeBytes: 30)
            .ShouldBe(ChunkDisposition.AlreadyHeld);
        Decide(receivedBytes: 10, offset: 4, chunkLength: 0, declaredSizeBytes: 30)
            .ShouldBe(ChunkDisposition.OutOfOrder);
    }

    [Fact]
    public void Completion_is_exact_equality_in_both_directions()
    {
        IsComplete(30, 30).ShouldBeTrue();
        IsComplete(29, 30).ShouldBeFalse();
        IsComplete(31, 30).ShouldBeFalse();

        // A zero-byte file is never complete, because it was never a file worth storing.
        IsComplete(0, 0).ShouldBeFalse();
    }

    [Fact]
    public void Every_accepted_piece_pushes_the_expiry_out_so_a_slow_upload_is_not_collected()
    {
        var start = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

        ExpiryFrom(start).ShouldBe(start + IdleLifetime);
        ExpiryFrom(start + TimeSpan.FromHours(20)).ShouldBeGreaterThan(ExpiryFrom(start));
    }
}
