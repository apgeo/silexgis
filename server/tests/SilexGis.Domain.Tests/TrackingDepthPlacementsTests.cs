// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The one ordering between what a cave declared a depth to be and what measuring it says.
/// </summary>
public class TrackingDepthPlacementsTests
{
    // An entrance at 350 m; a shaft with stations at 250 m and 230 m, and a gallery at 300 m.
    private static readonly TrackingDepthResolver.Station[] Stations =
    [
        TrackingDepthResolver.Station.Of(SurveyModelFormat.Survex3d, null, "cave.ent.0", "cave.ent", 350, true),
        TrackingDepthResolver.Station.Of(SurveyModelFormat.Survex3d, null, "cave.gallery.7", "cave.gallery", 300, false),
        TrackingDepthResolver.Station.Of(SurveyModelFormat.Survex3d, null, "cave.shaft.9", "cave.shaft", 250, false),
        TrackingDepthResolver.Station.Of(SurveyModelFormat.Survex3d, null, "cave.shaft.10", "cave.shaft", 230, false),
    ];

    [Fact]
    public void A_declared_depth_is_honoured_whichever_sign_the_report_wrote_it_with()
    {
        // 100 m down measures to the gallery at 300 m; the cave says 100 m is the shaft. The
        // declaration must win for −100 exactly as for 100, because −100 is how the report that
        // the declaration exists for is actually written.
        DeclaredDepthPlaces.Declared[] declared = [new(100m, "cave.shaft.9", "Puțul")];

        var signed = TrackingDepthPlacements.For(declared, Stations, null, [], -100m);
        signed.Outcome.ShouldBe(TrackingDepthPlacementOutcome.Declared);
        signed.ViewerStationName.ShouldBe("cave.shaft.9");

        var unsigned = TrackingDepthPlacements.For(declared, Stations, null, [], 100m);
        unsigned.ShouldBe(signed);

        // And with nothing declared the same depth measures, so the test above is about the
        // declaration and not about the shaft happening to be nearest.
        var measured = TrackingDepthPlacements.For([], Stations, null, [], -100m);
        measured.Outcome.ShouldBe(TrackingDepthPlacementOutcome.Measured);
        measured.ViewerStationName.ShouldBe("cave.shaft.9");
    }

    [Fact]
    public void A_declaration_naming_a_station_the_model_lacks_is_passed_over_and_the_answer_says_measured()
    {
        DeclaredDepthPlaces.Declared[] declared = [new(50m, "cave.typo.9", "Bivuac")];

        TrackingDepthPlacements.NamesAStationOf(Stations, declared[0]).ShouldBeFalse();
        TrackingDepthPlacements.NamesAStationOf(Stations, new(50m, "cave.gallery.7", null)).ShouldBeTrue();

        // Passed over rather than honoured — a marker at a station nothing resolves is one nobody
        // ever sees — and the outcome says so, which is what lets a surface warn that the place the
        // person picked is not what the log holds.
        var placed = TrackingDepthPlacements.For(declared, Stations, null, [], 50m);
        placed.Outcome.ShouldBe(TrackingDepthPlacementOutcome.Measured);
        placed.ViewerStationName.ShouldBe("cave.gallery.7");
    }

    [Fact]
    public void A_depth_is_placed_as_the_log_will_store_it_and_not_as_it_was_typed()
    {
        // Two stations a few centimetres apart in depth, either side of 120 m: 119.98 m and
        // 120.06 m below the entrance. Typed as 120.04 the nearer is the deeper one; stored, the
        // row reads 120.0 and the nearer is the shallower one. The row is placed on what it will
        // read, or two rows shown as "120.0 m" would stand at two stations.
        TrackingDepthResolver.Station[] stations =
        [
            TrackingDepthResolver.Station.Of(SurveyModelFormat.Survex3d, null, "cave.ent.0", "cave.ent", 350, true),
            TrackingDepthResolver.Station.Of(SurveyModelFormat.Survex3d, null, "cave.shaft.11", "cave.shaft", 230.02, false),
            TrackingDepthResolver.Station.Of(SurveyModelFormat.Survex3d, null, "cave.shaft.12", "cave.shaft", 229.94, false),
        ];

        var typed = TrackingDepthPlacements.For([], stations, null, [], 120.04m);
        typed.Outcome.ShouldBe(TrackingDepthPlacementOutcome.Measured);
        typed.ViewerStationName.ShouldBe("cave.shaft.11");

        // Asserted beside the depth that really is nearer the deeper station, so this cannot pass
        // by the shallower one winning every tie.
        TrackingDepthPlacements.For([], stations, null, [], 120.1m).ViewerStationName.ShouldBe("cave.shaft.12");
    }

    [Fact]
    public void The_stored_form_of_a_depth_is_one_decimal_half_away_from_zero_with_its_sign_kept()
    {
        TripTrackingRules.RecordedDepthM(120.04m).ShouldBe(120.0m);
        TripTrackingRules.RecordedDepthM(120.05m).ShouldBe(120.1m);
        TripTrackingRules.RecordedDepthM(-120.04m).ShouldBe(-120.0m);
        TripTrackingRules.RecordedDepthM(-120.05m).ShouldBe(-120.1m);
        TripTrackingRules.RecordedDepthM(50m).ShouldBe(50m);

        // A declaration's key is the magnitude of the same rounding — one rule, not two that
        // happen to agree today.
        foreach (var depth in new[] { 120.04m, -120.05m, 0.05m, -0.04m, 4999.95m })
        {
            DeclaredDepthPlaces.Key(depth).ShouldBe(Math.Abs(TripTrackingRules.RecordedDepthM(depth)));
        }
    }
}
