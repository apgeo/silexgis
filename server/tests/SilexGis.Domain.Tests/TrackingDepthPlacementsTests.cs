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

    // ---- stations the viewer cannot draw -------------------------------------------------------

    // The same cave read from a Survex file that also holds two stations it gives no name: one
    // exactly 100 m down, where the gallery is only near, and one above the entrance. The viewer
    // leaves both out of its drawing.
    private static readonly TrackingDepthResolver.Station[] WithNameless =
    [
        .. Stations,
        TrackingDepthResolver.Station.Of(SurveyModelFormat.Survex3d, null, "#4", null, 250.5, false, 4),
        TrackingDepthResolver.Station.Of(SurveyModelFormat.Survex3d, null, "#5", null, 360, false, 5),
    ];

    [Fact]
    public void A_depth_never_lands_on_a_station_the_viewer_cannot_draw()
    {
        // 99.5 m down is exactly the nameless station at 250.5 m; the shaft station at 250 m is
        // half a metre off. The report lands on the shaft station, because a report stamped with
        // the other would be drawn nowhere.
        WithNameless[4].NoViewerLabel.ShouldBeTrue();
        var placed = TrackingDepthPlacements.For([], WithNameless, null, [], 99.5m);
        placed.Outcome.ShouldBe(TrackingDepthPlacementOutcome.Measured);
        placed.ViewerStationName.ShouldBe("cave.shaft.9");

        TrackingDepthResolver.Resolve(WithNameless, 350, 99.5, [], take: 10)
            .ShouldAllBe(c => !c.Name.StartsWith('#'));

        // The positive twin: the very same row, were it a station somebody named "#4" — the file
        // wrote it at some other number — is an ordinary station and the nearest one, so it wins.
        // What is passed over is the nameless row, not the look of its name.
        TrackingDepthResolver.Station[] named =
        [
            .. Stations,
            TrackingDepthResolver.Station.Of(SurveyModelFormat.Survex3d, null, "#4", null, 250.5, false, 11),
        ];
        named[4].NoViewerLabel.ShouldBeFalse();
        TrackingDepthPlacements.For([], named, null, [], 99.5m).ViewerStationName.ShouldBe("#4");
    }

    [Fact]
    public void A_nameless_station_of_a_Therion_model_is_a_place_like_any_other()
    {
        // The viewer labels these itself and the rows carry that label, so a depth may land on one
        // and it will be drawn. Until the survey is read again an earlier reading holds the same
        // station under a spelling the viewer does not use, and that one is passed over.
        TrackingDepthResolver.Station[] read =
        [
            .. Stations,
            TrackingDepthResolver.Station.Of(SurveyModelFormat.Lox, "cave", "cave.shaft.[4]", "cave.shaft", 250.5, false, 4),
        ];
        read[4].NoViewerLabel.ShouldBeFalse();
        TrackingDepthPlacements.For([], read, null, [], 99.5m).ViewerStationName.ShouldBe("shaft.[4]");

        TrackingDepthResolver.Station[] readBefore =
        [
            .. Stations,
            TrackingDepthResolver.Station.Of(SurveyModelFormat.Lox, "cave", "cave.shaft.#4", "cave.shaft", 250.5, false, 4),
        ];
        readBefore[4].NoViewerLabel.ShouldBeTrue();
        TrackingDepthPlacements.For([], readBefore, null, [], 99.5m).ViewerStationName.ShouldBe("cave.shaft.9");
    }

    [Fact]
    public void A_declaration_naming_a_station_the_viewer_cannot_draw_is_passed_over()
    {
        // Declared against the nameless row, the place is not one a report can land on: the
        // declaration is listed as naming no station of the model, and the depth is measured.
        DeclaredDepthPlaces.Declared onNameless = new(100m, "#4", "Sala");
        TrackingDepthPlacements.NamesAStationOf(WithNameless, onNameless).ShouldBeFalse();
        var placed = TrackingDepthPlacements.For([onNameless], WithNameless, null, [], 100m);
        placed.Outcome.ShouldBe(TrackingDepthPlacementOutcome.Measured);
        placed.ViewerStationName.ShouldBe("cave.shaft.9");

        // Beside it, a declaration on a station that has a name is honoured from the same list.
        DeclaredDepthPlaces.Declared onNamed = new(100m, "cave.gallery.7", "Galeria");
        TrackingDepthPlacements.NamesAStationOf(WithNameless, onNamed).ShouldBeTrue();
        TrackingDepthPlacements.For([onNamed], WithNameless, null, [], 100m).Outcome
            .ShouldBe(TrackingDepthPlacementOutcome.Declared);
    }

    [Fact]
    public void A_nameless_station_still_measures_as_the_depth_datum()
    {
        // The datum is an altitude, not a place anybody is drawn at, so a watch whose depths are
        // measured from a nameless station keeps working: 110 m below 360 m is the shaft at 250 m.
        TrackingDepthResolver.ReferenceZ(WithNameless, "#5").ShouldBe(360);
        TrackingDepthPlacements.For([], WithNameless, "#5", [], 110m).ViewerStationName.ShouldBe("cave.shaft.9");
    }

    // ---- which named places a visitor may be told -------------------------------------------

    private static readonly DateTimeOffset Morning = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>A place with a depth, written at <paramref name="minute"/> past eight.</summary>
    private static DeclaredDepthPlaces.Place AtDepth(
        decimal depth, string station, string? label, int minute = 0, string? note = null, bool landmark = false) =>
        new(depth, station, label, note, landmark, Morning.AddMinutes(minute), Guid.CreateVersion7());

    /// <summary>A place named without a depth, written at <paramref name="minute"/> past eight.</summary>
    private static DeclaredDepthPlaces.Place Depthless(
        string station, string label, int minute, string? note = null, bool landmark = false) =>
        new(null, station, label, note, landmark, Morning.AddMinutes(minute), Guid.CreateVersion7());

    private static List<(decimal?, string, string?)> Shown(IEnumerable<DeclaredDepthPlaces.Place> places) =>
        [.. places.Select(p => (p.DepthM, p.ViewerStationName, p.PlaceLabel))];

    [Fact]
    public void Only_named_declarations_on_a_station_of_the_survey_are_published_shallowest_first()
    {
        DeclaredDepthPlaces.Place[] declared =
        [
            AtDepth(120m, "cave.shaft.10", "Sifonul"),
            // Named, and on a station this survey does not have: it belonged to an earlier one.
            AtDepth(80m, "cave.old.4", "Tabăra veche"),
            // On a station of the survey, and nobody gave it a name — nor a name that is only spaces.
            AtDepth(60m, "cave.gallery.7", null),
            AtDepth(70m, "cave.shaft.9", "   "),
            AtDepth(50m, "cave.gallery.7", "Galeria"),
            AtDepth(100m, "cave.shaft.9", "Puțul"),
        ];

        var published = TrackingDepthPlacements.PublishedPlaces(
            declared, Stations, TrackingDepthPlacements.MaxPublishedPlaces);

        Shown(published).ShouldBe(
        [
            (50m, "cave.gallery.7", "Galeria"),
            (100m, "cave.shaft.9", "Puțul"),
            (120m, "cave.shaft.10", "Sifonul"),
        ]);

        // The one left out for its station is left out by the test a report passes, and is a
        // declaration that would otherwise have been published: it has a name.
        TrackingDepthPlacements.HasDrawableStation(Stations, declared[1].ViewerStationName).ShouldBeFalse();
    }

    [Fact]
    public void Places_named_without_a_depth_are_published_after_every_depth_in_the_order_they_were_declared()
    {
        // Written in an order that is neither the depth order nor the order of the stations, so
        // the answer can only be the rule's: depths shallowest first, then the depthless ones as
        // they were written — the junction at a quarter past, the hall at ten past, the entrance
        // series at five past.
        DeclaredDepthPlaces.Place[] declared =
        [
            Depthless("cave.shaft.9", "Joncțiunea", minute: 15, note: "601/0 - 619"),
            AtDepth(120m, "cave.shaft.10", "Sifonul", minute: 20, landmark: true),
            Depthless("cave.gallery.7", "Sala Mare", minute: 10, landmark: true),
            AtDepth(0m, "cave.ent.0", "Intrarea", minute: 30, landmark: true),
            Depthless("cave.ent.0", "Seria de intrare", minute: 5),
            // A depthless name for a station this survey does not have is left out like any other.
            Depthless("cave.old.4", "Galeria veche", minute: 1),
        ];

        var published = TrackingDepthPlacements.PublishedPlaces(declared, Stations, 10);

        Shown(published).ShouldBe(
        [
            (0m, "cave.ent.0", "Intrarea"),
            (120m, "cave.shaft.10", "Sifonul"),
            (null, "cave.ent.0", "Seria de intrare"),
            (null, "cave.gallery.7", "Sala Mare"),
            (null, "cave.shaft.9", "Joncțiunea"),
        ]);

        // The note and the flag travel with the place they were written for, untouched.
        published.Single(p => p.PlaceLabel == "Joncțiunea").Note.ShouldBe("601/0 - 619");
        published.Single(p => p.PlaceLabel == "Joncțiunea").IsLandmark.ShouldBeFalse();
        published.Single(p => p.PlaceLabel == "Sala Mare").IsLandmark.ShouldBeTrue();
        published.Single(p => p.PlaceLabel == "Sala Mare").Note.ShouldBeNull();

        // Whatever order they are handed over in, the answer is the same — a published answer is
        // compared with the one a reader holds by its bytes.
        Shown(TrackingDepthPlacements.PublishedPlaces([.. declared.Reverse()], Stations, 10))
            .ShouldBe(Shown(published));
    }

    [Fact]
    public void Two_places_declared_in_the_same_instant_keep_the_order_of_their_identities()
    {
        // Written by one save, so the clock says nothing between them; the identity is
        // time-ordered and settles it, the same way the database settles it.
        var first = new DeclaredDepthPlaces.Place(null, "cave.shaft.9", "Unu", null, false, Morning, Guid.CreateVersion7());
        Thread.Sleep(2);
        var second = new DeclaredDepthPlaces.Place(null, "cave.gallery.7", "Doi", null, false, Morning, Guid.CreateVersion7());

        TrackingDepthPlacements.PublishedPlaces([second, first], Stations, 10)
            .Select(p => p.PlaceLabel).ShouldBe(["Unu", "Doi"]);
    }

    [Fact]
    public void The_published_places_are_capped_and_the_deepest_are_the_ones_left_out()
    {
        DeclaredDepthPlaces.Place[] declared =
        [
            AtDepth(120m, "cave.shaft.10", "Sifonul"),
            AtDepth(50m, "cave.gallery.7", "Galeria"),
            AtDepth(100m, "cave.shaft.9", "Puțul"),
        ];

        TrackingDepthPlacements.PublishedPlaces(declared, Stations, 3).Count.ShouldBe(3);
        TrackingDepthPlacements.PublishedPlaces(declared, Stations, 2)
            .Select(p => p.PlaceLabel).ShouldBe(["Galeria", "Puțul"]);
        TrackingDepthPlacements.PublishedPlaces(declared, Stations, 0).ShouldBeEmpty();
        TrackingDepthPlacements.PublishedPlaces(declared, Stations, -1).ShouldBeEmpty();

        // Past the cap, the last-written of the places without a depth go before any depth does.
        DeclaredDepthPlaces.Place[] mixed = [.. declared, Depthless("cave.ent.0", "Intrarea", minute: 1)];
        TrackingDepthPlacements.PublishedPlaces(mixed, Stations, 3)
            .Select(p => p.PlaceLabel).ShouldBe(["Galeria", "Puțul", "Sifonul"]);

        // The cap a published read uses is a real bound and not "everything".
        TrackingDepthPlacements.MaxPublishedPlaces.ShouldBeInRange(1, 1000);
    }

    [Fact]
    public void A_named_place_on_a_station_the_viewer_cannot_draw_is_not_published_and_nothing_is_without_stations()
    {
        var onNameless = AtDepth(100m, "#4", "Sala");
        var onNamed = AtDepth(50m, "cave.gallery.7", "Galeria");
        var depthlessOnNameless = Depthless("#4", "Sala fără nume", minute: 3);

        TrackingDepthPlacements.PublishedPlaces([onNameless, onNamed, depthlessOnNameless], WithNameless, 10)
            .ShouldBe([onNamed]);

        // A survey whose stations were never read holds none of them.
        TrackingDepthPlacements.PublishedPlaces([onNameless, onNamed], [], 10).ShouldBeEmpty();
        TrackingDepthPlacements.PublishedPlaces([], Stations, 10).ShouldBeEmpty();
    }

    // ---- the one list order, as a query and as rows ------------------------------------------

    [Fact]
    public void The_list_order_is_the_same_asked_of_a_query_and_of_rows_already_read()
    {
        var cave = Guid.CreateVersion7();
        var rows = new List<CaveDepthPlace>();
        void Add(decimal? depth, string station, int minute)
        {
            rows.Add(new CaveDepthPlace
            {
                CaveFeatureId = cave,
                DepthM = depth,
                ViewerStationName = station,
                PlaceLabel = station,
                CreatedAt = Morning.AddMinutes(minute),
            });
            // Separate writes are separate milliseconds, and the identity is time-ordered.
            Thread.Sleep(2);
        }

        Add(null, "d.3", 9);
        Add(210m, "a.3", 1);
        Add(null, "d.1", 2);
        Add(0m, "a.1", 8);
        Add(null, "d.2", 2);
        Add(45m, "a.2", 4);

        var asQuery = rows.AsQueryable().InListOrder().Select(r => r.ViewerStationName).ToList();
        var asRows = rows
            .Select(r => new DeclaredDepthPlaces.Place(
                r.DepthM, r.ViewerStationName, r.PlaceLabel, r.Note, r.IsLandmark, r.CreatedAt, r.Id))
            .InListOrder()
            .Select(p => p.ViewerStationName)
            .ToList();

        asQuery.ShouldBe(["a.1", "a.2", "a.3", "d.1", "d.2", "d.3"]);
        asRows.ShouldBe(asQuery);
    }

    [Fact]
    public void Only_a_place_with_a_depth_answers_for_one()
    {
        var cave = Guid.CreateVersion7();
        CaveDepthPlace[] rows =
        [
            new() { CaveFeatureId = cave, DepthM = 100m, ViewerStationName = "cave.shaft.9", PlaceLabel = "Puțul" },
            new() { CaveFeatureId = cave, DepthM = null, ViewerStationName = "cave.gallery.7", PlaceLabel = "Galeria" },
        ];

        rows.AsQueryable().WithDepth().Select(r => r.ViewerStationName).ShouldBe(["cave.shaft.9"]);
    }
}
