// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json.Nodes;
using Shouldly;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Tests;

public class HistoryProtectionTests
{
    private static readonly Func<Guid, bool> NoLinkHidden = _ => false;

    private static JsonObject Changes(params (string Prop, string? Old, string? New)[] props)
    {
        var obj = new JsonObject();
        foreach (var (prop, oldValue, newValue) in props)
        {
            obj[prop] = new JsonObject { ["old"] = oldValue, ["new"] = newValue };
        }

        return obj;
    }

    [Fact]
    public void Cave_location_fields_removed_when_cave_hidden_others_kept()
    {
        var changes = Changes(
            ("Name", "Old", "New"),
            ("ClosestAddress", "Str. X 1", "Str. Y 2"),
            ("LandRegistryNumber", "123", "456"),
            ("LocationNotes", "near the spring", "moved"),
            ("MainGeom", "POINT (25 45)", "POINT (26 46)"),
            ("UpdatedAt", "t1", "t2"));

        var result = HistoryProtection.Redact("Cave", changes, governingCaveHidden: true, NoLinkHidden);

        result.Changes!.ContainsKey("Name").ShouldBeTrue();
        result.Changes.ContainsKey("ClosestAddress").ShouldBeFalse();
        result.Changes.ContainsKey("LandRegistryNumber").ShouldBeFalse();
        result.Changes.ContainsKey("LocationNotes").ShouldBeFalse();
        result.Changes.ContainsKey("MainGeom").ShouldBeFalse();  // noise-dropped (also location data)
        result.Changes.ContainsKey("UpdatedAt").ShouldBeFalse(); // noise-dropped
        result.Redacted.ShouldBe(["ClosestAddress", "LandRegistryNumber", "LocationNotes"], ignoreOrder: true);
        result.Changes.ToJsonString().ShouldNotContain("POINT"); // no WKT leaks
    }

    [Fact]
    public void Cave_location_fields_kept_when_caller_can_view_exact()
    {
        var changes = Changes(("ClosestAddress", "Str. X", "Str. Y"));

        var result = HistoryProtection.Redact("Cave", changes, governingCaveHidden: false, NoLinkHidden);

        result.Changes!.ContainsKey("ClosestAddress").ShouldBeTrue();
        result.Redacted.ShouldBeEmpty();
    }

    [Fact]
    public void Entrance_coordinate_fields_removed_when_cave_hidden_name_kept()
    {
        var changes = Changes(
            ("Name", "N1", "N2"),
            ("Geom", "POINT (25 45)", "POINT (26 46)"),
            ("Altitude", "100", "200"),
            ("PositionQuality", "Gps", "Estimated"));

        var result = HistoryProtection.Redact("CaveEntrance", changes, governingCaveHidden: true, NoLinkHidden);

        result.Changes!.ContainsKey("Name").ShouldBeTrue();
        result.Redacted.ShouldBe(["Geom", "Altitude", "PositionQuality"], ignoreOrder: true);
        result.Changes.ToJsonString().ShouldNotContain("POINT");
    }

    [Fact]
    public void Centerline_payload_dropped_entirely_when_cave_hidden()
    {
        var changes = Changes(
            ("Geom", null, "MULTILINESTRING ((25 45, 26 46))"),
            ("Name", null, "Survey A"));

        var result = HistoryProtection.Redact("CaveCenterline", changes, governingCaveHidden: true, NoLinkHidden);

        result.Changes.ShouldBeNull();
        result.Redacted.ShouldContain("Geom");
        result.Redacted.ShouldContain("Name");
    }

    [Fact]
    public void Surface_feature_cave_link_removed_only_when_referenced_cave_hidden()
    {
        var hiddenCave = Guid.NewGuid();
        var changes = Changes(("CaveId", null, hiddenCave.ToString()), ("Name", "a", "b"));

        var result = HistoryProtection.Redact("SurfaceFeature", changes, governingCaveHidden: false, id => id == hiddenCave);

        result.Changes!.ContainsKey("CaveId").ShouldBeFalse();
        result.Changes.ContainsKey("Name").ShouldBeTrue(); // feature's own data stays
        result.Redacted.ShouldBe(["CaveId"]);
    }

    [Fact]
    public void Surface_feature_cave_link_kept_when_referenced_cave_visible()
    {
        var changes = Changes(("CaveId", null, Guid.NewGuid().ToString()));

        var result = HistoryProtection.Redact("SurfaceFeature", changes, governingCaveHidden: false, _ => false);

        result.Changes!.ContainsKey("CaveId").ShouldBeTrue();
        result.Redacted.ShouldBeEmpty();
    }

    [Fact]
    public void Trip_cave_link_redacted_under_the_same_rule()
    {
        var hiddenCave = Guid.NewGuid();
        var changes = Changes(("CaveId", hiddenCave.ToString(), null));

        var result = HistoryProtection.Redact("TripLogCave", changes, governingCaveHidden: false, id => id == hiddenCave);

        result.Changes.ShouldBeNull(); // only prop, removed → empty → null
        result.Redacted.ShouldBe(["CaveId"]);
    }

    [Fact]
    public void Null_changes_pass_through()
    {
        var result = HistoryProtection.Redact("Cave", null, governingCaveHidden: true, NoLinkHidden);

        result.Changes.ShouldBeNull();
        result.Redacted.ShouldBeEmpty();
    }
}
