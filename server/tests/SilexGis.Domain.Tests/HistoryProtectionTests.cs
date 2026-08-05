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
    public void Cave_location_fields_removed_when_hidden_others_kept()
    {
        // A merged Feature:Cave row: supertype props (Name, Geom) + subtype props.
        var changes = Changes(
            ("Name", "Old", "New"),
            ("ClosestAddress", "Str. X 1", "Str. Y 2"),
            ("LandRegistryNumber", "123", "456"),
            ("LocationNotes", "near the spring", "moved"),
            ("Geom", "POINT (25 45)", "POINT (26 46)"),
            ("UpdatedAt", "t1", "t2"));

        var result = HistoryProtection.Redact("Feature:Cave", changes, governingHidden: true, NoLinkHidden, associationHidden: false);

        result.Changes!.ContainsKey("Name").ShouldBeTrue();
        result.Changes.ContainsKey("ClosestAddress").ShouldBeFalse();
        result.Changes.ContainsKey("LandRegistryNumber").ShouldBeFalse();
        result.Changes.ContainsKey("LocationNotes").ShouldBeFalse();
        result.Changes.ContainsKey("Geom").ShouldBeFalse();      // noise-dropped (main-entrance cache)
        result.Changes.ContainsKey("UpdatedAt").ShouldBeFalse(); // noise-dropped
        result.Redacted.ShouldBe(["ClosestAddress", "LandRegistryNumber", "LocationNotes"], ignoreOrder: true);
        result.Changes.ToJsonString().ShouldNotContain("POINT"); // no WKT leaks
    }

    [Fact]
    public void Cave_location_fields_kept_when_caller_can_view_exact()
    {
        var changes = Changes(("ClosestAddress", "Str. X", "Str. Y"));

        var result = HistoryProtection.Redact("Feature:Cave", changes, governingHidden: false, NoLinkHidden, associationHidden: false);

        result.Changes!.ContainsKey("ClosestAddress").ShouldBeTrue();
        result.Redacted.ShouldBeEmpty();
    }

    [Fact]
    public void Entrance_coordinate_fields_removed_when_hidden_name_kept()
    {
        var changes = Changes(
            ("Name", "N1", "N2"),
            ("Geom", "POINT (25 45)", "POINT (26 46)"),
            ("Altitude", "100", "200"),
            ("PositionQuality", "Gps", "Estimated"));

        var result = HistoryProtection.Redact("Feature:CaveEntrance", changes, governingHidden: true, NoLinkHidden, associationHidden: false);

        result.Changes!.ContainsKey("Name").ShouldBeTrue();
        result.Redacted.ShouldBe(["Geom", "Altitude", "PositionQuality"], ignoreOrder: true);
        result.Changes.ToJsonString().ShouldNotContain("POINT");
    }

    [Fact]
    public void Centerline_payload_dropped_entirely_when_hidden()
    {
        var changes = Changes(
            ("Geom", null, "MULTILINESTRING ((25 45, 26 46))"),
            ("Name", null, "Survey A"));

        var result = HistoryProtection.Redact("Feature:Centerline", changes, governingHidden: true, NoLinkHidden, associationHidden: false);

        result.Changes.ShouldBeNull();
        result.Redacted.ShouldContain("Geom");
        result.Redacted.ShouldContain("Name");
    }

    [Fact]
    public void Generic_feature_geometry_removed_when_under_a_protected_root()
    {
        var changes = Changes(("Geom", "POINT (25 45)", "POINT (26 46)"), ("Name", "a", "b"));

        var result = HistoryProtection.Redact("Feature:Generic", changes, governingHidden: true, NoLinkHidden, associationHidden: false);

        result.Changes!.ContainsKey("Name").ShouldBeTrue();
        result.Redacted.ShouldBe(["Geom"]);
        result.Changes.ToJsonString().ShouldNotContain("POINT");
    }

    [Fact]
    public void Feature_link_endpoints_removed_when_either_side_hidden()
    {
        var hidden = Guid.NewGuid();
        var changes = Changes(
            ("FromId", null, Guid.NewGuid().ToString()),
            ("ToId", null, hidden.ToString()),
            ("Note", null, "spring connection"));

        var result = HistoryProtection.Redact("FeatureLink", changes, governingHidden: false, id => id == hidden, associationHidden: false);

        result.Changes!.ContainsKey("ToId").ShouldBeFalse();
        result.Changes.ContainsKey("FromId").ShouldBeTrue(); // that endpoint is not hidden
        result.Changes.ContainsKey("Note").ShouldBeTrue();
        result.Redacted.ShouldBe(["ToId"]);
    }

    [Fact]
    public void Feature_link_kept_when_both_sides_visible()
    {
        var changes = Changes(("FromId", null, Guid.NewGuid().ToString()), ("ToId", null, Guid.NewGuid().ToString()));

        var result = HistoryProtection.Redact("FeatureLink", changes, governingHidden: false, _ => false, associationHidden: false);

        result.Changes!.Count.ShouldBe(2);
        result.Redacted.ShouldBeEmpty();
    }

    [Fact]
    public void Trip_cave_link_redacted_under_the_same_rule()
    {
        var hiddenCave = Guid.NewGuid();
        var changes = Changes(("CaveId", hiddenCave.ToString(), null));

        var result = HistoryProtection.Redact("TripLogCave", changes, governingHidden: false, id => id == hiddenCave, associationHidden: false);

        result.Changes.ShouldBeNull(); // only prop, removed → empty → null
        result.Redacted.ShouldBe(["CaveId"]);
    }

    /// <summary>
    /// An attachment row names the document it pairs with exactly when the association rule
    /// says the pairing may be disclosed — and that is the only thing that decides it here.
    ///
    /// Both halves are asserted over the same row with the same protected ancestry, so the
    /// withheld case cannot pass on a build that had simply stopped looking at the flag, and
    /// the disclosed case cannot pass on one that had stopped redacting. This is where the
    /// timeline stopped having an opinion of its own: it used to withhold whenever the
    /// feature's location was hidden, whatever the installation had chosen and whatever the
    /// document behind the attachment was.
    /// </summary>
    [Fact]
    public void Attachment_names_its_document_exactly_when_the_association_may_be_disclosed()
    {
        var fileId = Guid.NewGuid().ToString();

        JsonObject Row() => Changes(
            ("FileId", null, fileId), ("Caption", null, "Entrance from the north"), ("SortOrder", null, "0"));

        // Withheld: the event stays — something was attached — while which document it was
        // does not.
        var hidden = HistoryProtection.Redact(
            "Attachment", Row(), governingHidden: true, NoLinkHidden, associationHidden: true);
        hidden.Redacted.Order().ShouldBe(["Caption", "FileId"]);
        hidden.Changes!.ContainsKey("FileId").ShouldBeFalse();
        hidden.Changes.ContainsKey("SortOrder").ShouldBeTrue();

        // Disclosed, with the location still protected: the pairing is a name, and naming it
        // is a decision the association rule takes, not this one.
        var shown = HistoryProtection.Redact(
            "Attachment", Row(), governingHidden: true, NoLinkHidden, associationHidden: false);
        shown.Redacted.ShouldBeEmpty();
        shown.Changes!.Count.ShouldBe(3);
    }

    [Fact]
    public void Null_changes_pass_through()
    {
        var result = HistoryProtection.Redact("Feature:Cave", null, governingHidden: true, NoLinkHidden, associationHidden: false);

        result.Changes.ShouldBeNull();
        result.Redacted.ShouldBeEmpty();
    }
}
