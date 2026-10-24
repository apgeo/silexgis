// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using Shouldly;
using SilexGis.Api.Features.Sync;
using SilexGis.Domain.Entities;

namespace SilexGis.Api.Tests;

/// <summary>
/// The paging every download stream is walked by, exercised without a database: the resume
/// position's written form, which stream honours it, the pair comparison a resumed read starts
/// after, and the cut that merges a stream's halves into one page. The feature stream's own
/// behaviour over real rows — and its recorded bytes — is proved by the protocol suite; what is
/// pinned here is what every later stream inherits.
/// </summary>
public class SyncStreamPagingTests
{
    private static readonly DateTimeOffset T1 = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T2 = T1.AddMinutes(1);
    private static readonly DateTimeOffset T3 = T1.AddMinutes(2);

    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-00000000000c");
    private static readonly Guid D = Guid.Parse("00000000-0000-0000-0000-00000000000d");

    /// <summary>The same shape of key the feature stream orders on.</summary>
    private static readonly SyncStreamOrder<Row> ByChange = new(r => r.DeletedAt ?? r.UpdatedAt, r => r.Id);

    [Fact]
    public void A_feature_position_is_written_exactly_as_devices_already_hold_it()
    {
        var id = Guid.CreateVersion7();
        var token = new SyncCursor(42, T1, id).Encode(SyncStream.Features);

        // Devices in the field hold positions in this form; a second stream must not have moved it.
        Encoding.UTF8.GetString(Base64Url.DecodeFromChars(token))
            .ShouldBe($"2|42|{T1.UtcTicks}|{id:N}");

        SyncCursor.TryDecode(token, SyncStream.Features, out var decoded).ShouldBeTrue();
        decoded.ShouldBe(new SyncCursor(42, T1, id));
    }

    [Fact]
    public void A_position_is_honoured_only_by_the_stream_that_issued_it()
    {
        var position = new SyncCursor(3, T2, B);
        var features = position.Encode(SyncStream.Features);
        var documents = position.Encode(SyncStream.Documents);

        // Each stream reads its own position back unchanged...
        SyncCursor.TryDecode(features, SyncStream.Features, out var fromFeatures).ShouldBeTrue();
        fromFeatures.ShouldBe(position);
        SyncCursor.TryDecode(documents, SyncStream.Documents, out var fromDocuments).ShouldBeTrue();
        fromDocuments.ShouldBe(position);

        // ...and refuses the other's, which orders different rows on a different key.
        SyncCursor.TryDecode(documents, SyncStream.Features, out _).ShouldBeFalse();
        SyncCursor.TryDecode(features, SyncStream.Documents, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-cursor")]
    [InlineData("MXwxfDF8MDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMGE")] // version 1
    [InlineData("MnwxfDF8MDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMGF8cmFzdGVycw")] // a stream never issued
    public void A_position_this_server_did_not_write_is_refused_by_every_stream(string token)
    {
        // The same position written the way this server writes it is read: the refusals below are
        // about what the tokens say, not about how they were spelled.
        SyncCursor.TryDecode(
            "MnwxfDF8MDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMGE", SyncStream.Features, out _).ShouldBeTrue();

        SyncCursor.TryDecode(token, SyncStream.Features, out _).ShouldBeFalse();
        SyncCursor.TryDecode(token, SyncStream.Documents, out _).ShouldBeFalse();
    }

    [Fact]
    public void A_resumed_read_starts_strictly_after_the_pair_and_ties_sort_by_identifier()
    {
        Row[] rows =
        [
            new(C, null, T1),
            new(A, null, T1),
            new(D, T2, T1), // gone after it was written: sorts on when it went
            new(B, null, T1),
            new(A, null, T3),
        ];

        // Resumed at (T1, B): the row it stopped on and the one tied before it are behind it;
        // the one tied after it is not, nor is anything later.
        var after = SyncStreamPaging.After(rows.AsQueryable(), ByChange, new SyncCursor(1, T1, B));
        SyncStreamPaging.Probe(after, ByChange, 10).AsEnumerable()
            .Select(r => (r.Id, r.DeletedAt ?? r.UpdatedAt)).ToList()
            .ShouldBe([(C, T1), (D, T2), (A, T3)]);

        // From the beginning, the probe reads one more than the page so the cut can see past it.
        SyncStreamPaging.Probe(SyncStreamPaging.After(rows.AsQueryable(), ByChange, null), ByChange, 2)
            .Select(r => r.Id).ToList()
            .ShouldBe([A, B, C]);
    }

    [Fact]
    public void A_cut_interleaves_the_halves_on_one_key_and_resumes_from_the_last_row_it_emitted()
    {
        var opening = Opening(SyncStream.Documents, size: 3, cursor: null);
        Row[] live = [new(A, null, T1), new(C, null, T2), new(A, null, T3)];
        Gone[] gone = [new(B, T1), new(D, T2)];

        var cut = SyncStreamPaging.Cut(
            opening,
            live.Select(ByChange.Entry),
            gone.Select(g => new SyncStreamEntry(g.At, g.Id, g)));

        cut.Rows.ShouldBe([live[0], gone[0], live[1]]);
        cut.HasMore.ShouldBeTrue();
        SyncCursor.TryDecode(cut.NextCursor, SyncStream.Documents, out var next).ShouldBeTrue();
        next.ShouldBe(new SyncCursor(opening.Set.Revision, T2, C));
    }

    [Fact]
    public void A_page_that_fits_says_nothing_is_waiting()
    {
        var cut = SyncStreamPaging.Cut(
            Opening(SyncStream.Features, size: 2, cursor: null),
            new[] { new Row(A, null, T1), new Row(B, null, T2) }.Select(ByChange.Entry));

        cut.Rows.Count.ShouldBe(2);
        cut.HasMore.ShouldBeFalse();
        SyncCursor.TryDecode(cut.NextCursor, SyncStream.Features, out var next).ShouldBeTrue();
        next.Id.ShouldBe(B);
    }

    [Fact]
    public void A_page_that_emits_nothing_hands_back_the_position_it_was_asked_from()
    {
        var held = new SyncCursor(7, T2, C).Encode(SyncStream.Features);

        var caughtUp = SyncStreamPaging.Cut(Opening(SyncStream.Features, size: 5, cursor: held), []);
        caughtUp.Rows.ShouldBeEmpty();
        caughtUp.HasMore.ShouldBeFalse();
        caughtUp.NextCursor.ShouldBe(held);

        // A first read of an empty stream has no position to hand back, and invents none.
        SyncStreamPaging.Cut(Opening(SyncStream.Features, size: 5, cursor: null), []).NextCursor.ShouldBeNull();
    }

    private static SyncStreamOpening Opening(SyncStream stream, int size, string? cursor) =>
        new(
            stream,
            new SyncSet { Name = "paging", Revision = 7 },
            JsonDocument.Parse("{}").RootElement,
            [A],
            null,
            size,
            cursor);

    private sealed record Row(Guid Id, DateTimeOffset? DeletedAt, DateTimeOffset UpdatedAt);

    private sealed record Gone(Guid Id, DateTimeOffset At);
}
