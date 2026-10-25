// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json.Nodes;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Features;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Tests;

/// <summary>
/// What a restore from the history may write, which of it is a position, and how a recorded value
/// reads back into its column's type — the inverse of how the history writes one.
/// </summary>
public sealed class FeatureHistoryRestoreTests
{
    [Theory]
    [InlineData(FeatureKind.Generic, "Name", true)]
    [InlineData(FeatureKind.Generic, "Geom", true)]
    [InlineData(FeatureKind.Generic, "Category", true)]
    [InlineData(FeatureKind.Generic, "Visibility", false)]
    [InlineData(FeatureKind.Generic, "LocationProtected", false)]
    [InlineData(FeatureKind.Generic, "CavingGroupId", false)]
    [InlineData(FeatureKind.Generic, "UpdatedAt", false)]
    [InlineData(FeatureKind.Cave, "ClosestAddress", true)]
    [InlineData(FeatureKind.Cave, "Geom", false)]
    [InlineData(FeatureKind.Cave, "EntranceCount", false)]
    [InlineData(FeatureKind.CaveEntrance, "Altitude", true)]
    [InlineData(FeatureKind.CaveEntrance, "IsMain", true)]
    [InlineData(FeatureKind.CaveEntrance, "CaveFeatureId", false)]
    [InlineData(FeatureKind.Centerline, "Name", false)]
    [InlineData(FeatureKind.Centerline, "Geom", false)]
    public void A_restore_writes_what_the_kinds_edit_form_writes(FeatureKind kind, string field, bool restorable) =>
        FeatureHistoryFields.IsRestorable(kind, field).ShouldBe(restorable);

    [Fact]
    public void Positions_and_promotion_to_main_need_exact_view_and_a_demotion_does_not()
    {
        FeatureHistoryFields.IsPositionWrite(FeatureKind.Generic, "Geom", null).ShouldBeTrue();
        FeatureHistoryFields.IsPositionWrite(FeatureKind.Generic, "Name", JsonValue.Create("x")).ShouldBeFalse();
        FeatureHistoryFields.IsPositionWrite(FeatureKind.CaveEntrance, "PositionQuality", JsonValue.Create("gps")).ShouldBeTrue();
        FeatureHistoryFields.IsPositionWrite(FeatureKind.CaveEntrance, "IsMain", JsonValue.Create(true)).ShouldBeTrue();
        FeatureHistoryFields.IsPositionWrite(FeatureKind.CaveEntrance, "IsMain", JsonValue.Create(false)).ShouldBeFalse();
        FeatureHistoryFields.IsPositionWrite(FeatureKind.Cave, "ClosestAddress", JsonValue.Create("x")).ShouldBeFalse();
    }

    [Fact]
    public void Recorded_values_read_back_into_their_columns_types()
    {
        HistoryValues.TryDecode(JsonValue.Create("gps"), typeof(PositionQuality), out var quality).ShouldBeTrue();
        quality.ShouldBe(PositionQuality.Gps);
        HistoryValues.TryDecode(JsonValue.Create(912.5m), typeof(decimal?), out var altitude).ShouldBeTrue();
        altitude.ShouldBe(912.5m);
        HistoryValues.TryDecode(JsonValue.Create(7), typeof(long), out var id).ShouldBeTrue();
        id.ShouldBe(7L);
        HistoryValues.TryDecode(JsonValue.Create("2026-07-01"), typeof(DateOnly?), out var date).ShouldBeTrue();
        date.ShouldBe(new DateOnly(2026, 7, 1));
        HistoryValues.TryDecode(null, typeof(string), out var none).ShouldBeTrue();
        none.ShouldBeNull();

        HistoryValues.TryDecode(JsonValue.Create("POLYGON ((0 0, 1 0, 1 1, 0 0))"), typeof(Geometry), out var shape).ShouldBeTrue();
        shape.ShouldBeOfType<Polygon>().SRID.ShouldBe(4326);
    }

    [Fact]
    public void A_value_that_no_longer_fits_its_column_is_refused_rather_than_guessed()
    {
        HistoryValues.TryDecode(null, typeof(long), out _).ShouldBeFalse();
        HistoryValues.TryDecode(JsonValue.Create("retired"), typeof(PositionQuality), out _).ShouldBeFalse();
        HistoryValues.TryDecode(JsonValue.Create("not wkt"), typeof(Geometry), out _).ShouldBeFalse();
        HistoryValues.TryDecode(new JsonObject { ["omitted"] = true }, typeof(Geometry), out _).ShouldBeFalse();
    }

    [Fact]
    public void A_kept_pre_image_reads_back_as_the_shape_its_summary_fingerprints()
    {
        var line = new LineString([.. Enumerable.Range(0, 2000).Select(i => new Coordinate(26 + (i * 1e-5), 46 + (i * 1e-5)))])
        {
            SRID = 4326,
        };
        var summary = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(AuditGeometry.Summary(line)));

        FeatureHistoryFields.IsGeometrySummary(summary).ShouldBeTrue();
        FeatureHistoryFields.IsGeometrySummary(JsonValue.Create("LINESTRING (0 0, 1 1)")).ShouldBeFalse();

        var back = AuditGeometry.ReadPreImage(AuditGeometry.PreImage(line));
        back.EqualsExact(line).ShouldBeTrue();
        back.SRID.ShouldBe(4326);
        AuditGeometry.Matches(back, FeatureHistoryFields.SummaryFingerprint(summary)).ShouldBeTrue();

        var moved = (LineString)line.Copy();
        moved.Coordinates[0].X += 1e-6;
        moved.GeometryChanged();
        AuditGeometry.Matches(moved, FeatureHistoryFields.SummaryFingerprint(summary)).ShouldBeFalse();
    }

    [Fact]
    public void A_pre_image_keeps_an_altitude_only_where_the_shape_has_one()
    {
        var flat = new Point(25.1, 45.2) { SRID = 4326 };
        var raised = new Point(new CoordinateZ(25.1, 45.2, 912)) { SRID = 4326 };

        double.IsNaN(AuditGeometry.ReadPreImage(AuditGeometry.PreImage(flat)).Coordinate.Z).ShouldBeTrue();
        AuditGeometry.ReadPreImage(AuditGeometry.PreImage(raised)).Coordinate.Z.ShouldBe(912);
    }

    [Fact]
    public void A_column_still_holds_what_the_history_recorded_when_the_values_agree_as_the_history_wrote_them()
    {
        // By value: an altitude read back off a numeric column with its trailing zero.
        HistoryValues.StillRecorded(JsonValue.Create(812.5m), 812.50m, typeof(decimal?), document: false).ShouldBeTrue();
        HistoryValues.StillRecorded(JsonValue.Create(812.5m), 813m, typeof(decimal?), document: false).ShouldBeFalse();

        // Enums by their recorded name, nulls only against nulls.
        HistoryValues.StillRecorded(JsonValue.Create("gps"), PositionQuality.Gps, typeof(PositionQuality), document: false)
            .ShouldBeTrue();
        HistoryValues.StillRecorded(null, null, typeof(string), document: false).ShouldBeTrue();
        HistoryValues.StillRecorded(null, "now named", typeof(string), document: false).ShouldBeFalse();
        HistoryValues.StillRecorded(JsonValue.Create("then"), null, typeof(string), document: false).ShouldBeFalse();

        // A document by its content: the database rewrites spacing and key order on the way in.
        HistoryValues.StillRecorded(JsonValue.Create("""{"b": 1, "a": "x"}"""), """{"a":"x","b":1}""", typeof(string), document: true)
            .ShouldBeTrue();
        HistoryValues.StillRecorded(JsonValue.Create("""{"a":"x"}"""), """{"a":"y"}""", typeof(string), document: true)
            .ShouldBeFalse();
    }

    [Fact]
    public void A_shape_still_holds_what_the_history_recorded_by_its_text_or_by_its_fingerprint()
    {
        var point = new Point(25.1, 45.2) { SRID = 4326 };
        var recorded = JsonValue.Create((string)AuditGeometry.Record(point));
        HistoryValues.StillRecorded(recorded, point.Copy(), typeof(Geometry), document: false).ShouldBeTrue();
        HistoryValues.StillRecorded(recorded, new Point(25.1, 45.3) { SRID = 4326 }, typeof(Geometry), document: false)
            .ShouldBeFalse();

        var line = new LineString([.. Enumerable.Range(0, 2000).Select(i => new Coordinate(26 + (i * 1e-5), 46 + (i * 1e-5)))])
        {
            SRID = 4326,
        };
        var summary = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(AuditGeometry.Summary(line)));
        HistoryValues.StillRecorded(summary, line.Copy(), typeof(Geometry), document: false).ShouldBeTrue();
        var moved = (LineString)line.Copy();
        moved.Coordinates[0].X += 1e-6;
        moved.GeometryChanged();
        HistoryValues.StillRecorded(summary, moved, typeof(Geometry), document: false).ShouldBeFalse();
    }
}
