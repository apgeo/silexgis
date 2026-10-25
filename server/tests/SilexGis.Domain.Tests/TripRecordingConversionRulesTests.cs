// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Import;
using SilexGis.Domain.Trips;

namespace SilexGis.Domain.Tests;

public sealed class TripRecordingConversionRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_nearest_station_is_taken_only_when_nothing_else_is_as_near()
    {
        TripRecordingConversionRules.SoleStation([]).ShouldBeNull();
        TripRecordingConversionRules.SoleStation([Candidate("a", 0.4)]).ShouldBe("a");
        TripRecordingConversionRules.SoleStation([Candidate("a", 0.4), Candidate("b", 0.5)]).ShouldBe("a");

        // Two branches at the same horizon: a tie is not settled by which name sorts first.
        TripRecordingConversionRules.SoleStation([Candidate("a", 15.0), Candidate("b", 15.0)]).ShouldBeNull();
    }

    [Fact]
    public void A_scan_becomes_a_position_only_with_a_place_a_sole_station_and_a_person()
    {
        var caver = Guid.CreateVersion7();
        var clean = TripRecordingConversionRules.Judge(
            Resolution(SpeleolocPointState.Proposed, caver, Candidate("cave.upper.2", 0)), Now);
        clean.Recordable.ShouldBeTrue();
        clean.StationName.ShouldBe("cave.upper.2");
        clean.CaverId.ShouldBe(caver);

        TripRecordingConversionRules.Judge(Resolution(SpeleolocPointState.PlaceUnknown, caver), Now)
            .Reasons.ShouldBe([TripAttentionReasons.PlaceUnknown]);
        TripRecordingConversionRules.Judge(Resolution(SpeleolocPointState.NoPlace, caver), Now)
            .Reasons.ShouldBe([TripAttentionReasons.PlaceUnknown]);
        TripRecordingConversionRules.Judge(Resolution(SpeleolocPointState.DepthUnknown, caver), Now)
            .Reasons.ShouldBe([TripAttentionReasons.UnresolvedStation]);
        TripRecordingConversionRules.Judge(Resolution(SpeleolocPointState.ModelUnavailable, caver), Now)
            .Reasons.ShouldBe([TripAttentionReasons.UnresolvedStation]);
        TripRecordingConversionRules.Judge(
                Resolution(SpeleolocPointState.Proposed, caver, Candidate("a", 15), Candidate("b", 15)), Now)
            .Reasons.ShouldBe([TripAttentionReasons.UnresolvedStation]);

        // Every reason a scan has is named, not only the first.
        var both = TripRecordingConversionRules.Judge(
            Resolution(SpeleolocPointState.PlaceUnknown, caverId: null, scannedAt: Now.AddHours(1)), Now);
        both.Recordable.ShouldBeFalse();
        both.Reasons.ShouldBe(
            [TripAttentionReasons.PlaceUnknown, TripAttentionReasons.UnmappedPerson, TripAttentionReasons.RecordedInFuture]);
    }

    [Fact]
    public void Reasons_are_collected_once_each_in_a_fixed_order_with_their_scans()
    {
        var caver = Guid.CreateVersion7();
        var verdicts = new[]
        {
            TripRecordingConversionRules.Judge(Resolution(SpeleolocPointState.Proposed, null, Candidate("a", 0), id: "p3"), Now),
            TripRecordingConversionRules.Judge(Resolution(SpeleolocPointState.DepthUnknown, caver, id: "p2"), Now),
            TripRecordingConversionRules.Judge(Resolution(SpeleolocPointState.PlaceUnknown, null, id: "p1"), Now),
            TripRecordingConversionRules.Judge(Resolution(SpeleolocPointState.Proposed, caver, Candidate("a", 0), id: "p4"), Now),
        };

        var reasons = TripRecordingConversionRules.Collect(verdicts);

        reasons.Select(r => r.Code).ShouldBe(
            [TripAttentionReasons.PlaceUnknown, TripAttentionReasons.UnresolvedStation, TripAttentionReasons.UnmappedPerson]);
        reasons.Single(r => r.Code == TripAttentionReasons.UnmappedPerson).PointIds.ShouldBe(["p1", "p3"]);
        reasons.Single(r => r.Code == TripAttentionReasons.UnmappedPerson).Count.ShouldBe(2);
        TripRecordingConversionRules.Collect([verdicts[3]]).ShouldBeEmpty();
    }

    [Fact]
    public void A_change_after_conversion_adds_to_what_is_already_raised_and_counts_changes()
    {
        IReadOnlyList<TripAttentionReason> existing =
        [
            new(TripAttentionReasons.UnresolvedStation, 1, ["p1"]),
            TripRecordingConversionRules.ChangedAfterConversion(["p2"]),
        ];

        var merged = TripRecordingConversionRules.Merge(
            existing,
            [TripRecordingConversionRules.ChangedAfterConversion(["p2", "p5"]), new(TripAttentionReasons.UnresolvedStation, 1, ["p1"])]);

        merged.Single(r => r.Code == TripAttentionReasons.UnresolvedStation).Count.ShouldBe(1);
        var changed = merged.Single(r => r.Code == TripAttentionReasons.RecordingChangedAfterConversion);
        changed.Count.ShouldBe(2);
        changed.PointIds.ShouldBe(["p2", "p5"]);
    }

    [Fact]
    public void Stored_reasons_read_back_and_an_unreadable_value_reads_as_none()
    {
        IReadOnlyList<TripAttentionReason> reasons = [new(TripAttentionReasons.PlaceUnknown, 1, ["p1"])];
        var json = TripRecordingConversionRules.Serialize(reasons);
        json.ShouldContain("\"code\":\"place_unknown\"");

        var back = TripRecordingConversionRules.Parse(json);
        back.Single().Code.ShouldBe(TripAttentionReasons.PlaceUnknown);
        back.Single().PointIds.ShouldBe(["p1"]);
        TripRecordingConversionRules.Parse("{not json").ShouldBeEmpty();
        TripRecordingConversionRules.Parse(null).ShouldBeEmpty();
    }

    [Fact]
    public void A_long_note_keeps_its_beginning_on_the_position()
    {
        TripRecordingConversionRules.NoteOf("  ").ShouldBeNull();
        TripRecordingConversionRules.NoteOf(" sump ").ShouldBe("sump");
        TripRecordingConversionRules.NoteOf(new string('x', TripTrackingRules.MaxNoteLength + 50))!
            .Length.ShouldBe(TripTrackingRules.MaxNoteLength);
    }

    private static SpeleolocStationCandidate Candidate(string name, double delta) => new(name, "cave", 50, delta);

    private static SpeleolocPointResolution Resolution(
        SpeleolocPointState state,
        Guid? caverId,
        params SpeleolocStationCandidate[] candidates) =>
        Resolution(state, caverId, Now.AddHours(-2), "p", candidates);

    private static SpeleolocPointResolution Resolution(
        SpeleolocPointState state, Guid? caverId, DateTimeOffset scannedAt) =>
        Resolution(state, caverId, scannedAt, "p", []);

    private static SpeleolocPointResolution Resolution(
        SpeleolocPointState state, Guid? caverId, SpeleolocStationCandidate candidate, string id) =>
        Resolution(state, caverId, Now.AddHours(-2), id, [candidate]);

    private static SpeleolocPointResolution Resolution(SpeleolocPointState state, Guid? caverId, string id) =>
        Resolution(state, caverId, Now.AddHours(-2), id, []);

    private static SpeleolocPointResolution Resolution(
        SpeleolocPointState state,
        Guid? caverId,
        DateTimeOffset scannedAt,
        string id,
        IReadOnlyList<SpeleolocStationCandidate> candidates) =>
        new(id, scannedAt, null, null, null, null, caverId is null ? null : "device", caverId, state, candidates);
}
