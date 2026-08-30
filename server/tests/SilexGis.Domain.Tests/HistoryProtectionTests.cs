// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json.Nodes;
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Tests;

public class HistoryProtectionTests
{
    private static readonly Func<Guid, bool> NoLinkHidden = _ => false;

    private static readonly Func<Guid, bool> NoMemberHidden = _ => false;

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

        var result = HistoryProtection.Redact("Feature:Cave", changes, governingHidden: true, NoLinkHidden, associationHidden: false, mayWriteSubject: false, peopleHidden: false, memberHidden: NoMemberHidden);

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

        var result = HistoryProtection.Redact("Feature:Cave", changes, governingHidden: false, NoLinkHidden, associationHidden: false, mayWriteSubject: false, peopleHidden: false, memberHidden: NoMemberHidden);

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

        var result = HistoryProtection.Redact("Feature:CaveEntrance", changes, governingHidden: true, NoLinkHidden, associationHidden: false, mayWriteSubject: false, peopleHidden: false, memberHidden: NoMemberHidden);

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

        var result = HistoryProtection.Redact("Feature:Centerline", changes, governingHidden: true, NoLinkHidden, associationHidden: false, mayWriteSubject: false, peopleHidden: false, memberHidden: NoMemberHidden);

        result.Changes.ShouldBeNull();
        result.Redacted.ShouldContain("Geom");
        result.Redacted.ShouldContain("Name");
    }

    [Fact]
    public void Generic_feature_geometry_removed_when_under_a_protected_root()
    {
        var changes = Changes(("Geom", "POINT (25 45)", "POINT (26 46)"), ("Name", "a", "b"));

        var result = HistoryProtection.Redact("Feature:Generic", changes, governingHidden: true, NoLinkHidden, associationHidden: false, mayWriteSubject: false, peopleHidden: false, memberHidden: NoMemberHidden);

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

        var result = HistoryProtection.Redact("FeatureLink", changes, governingHidden: false, id => id == hidden, associationHidden: false, mayWriteSubject: false, peopleHidden: false, memberHidden: NoMemberHidden);

        result.Changes!.ContainsKey("ToId").ShouldBeFalse();
        result.Changes.ContainsKey("FromId").ShouldBeTrue(); // that endpoint is not hidden
        result.Changes.ContainsKey("Note").ShouldBeTrue();
        result.Redacted.ShouldBe(["ToId"]);
    }

    [Fact]
    public void Feature_link_kept_when_both_sides_visible()
    {
        var changes = Changes(("FromId", null, Guid.NewGuid().ToString()), ("ToId", null, Guid.NewGuid().ToString()));

        var result = HistoryProtection.Redact("FeatureLink", changes, governingHidden: false, _ => false, associationHidden: false, mayWriteSubject: false, peopleHidden: false, memberHidden: NoMemberHidden);

        result.Changes!.Count.ShouldBe(2);
        result.Redacted.ShouldBeEmpty();
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
            "Attachment", Row(), governingHidden: true, NoLinkHidden, associationHidden: true, mayWriteSubject: false, peopleHidden: false, memberHidden: NoMemberHidden);
        hidden.Redacted.Order().ShouldBe(["Caption", "FileId"]);
        hidden.Changes!.ContainsKey("FileId").ShouldBeFalse();
        hidden.Changes.ContainsKey("SortOrder").ShouldBeTrue();

        // Disclosed, with the location still protected: the pairing is a name, and naming it
        // is a decision the association rule takes, not this one.
        var shown = HistoryProtection.Redact(
            "Attachment", Row(), governingHidden: true, NoLinkHidden, associationHidden: false, mayWriteSubject: false, peopleHidden: false, memberHidden: NoMemberHidden);
        shown.Redacted.ShouldBeEmpty();
        shown.Changes!.Count.ShouldBe(3);
    }

    [Fact]
    public void ResLink_membership_names_no_link_in_a_protected_features_timeline()
    {
        var linkId = Guid.NewGuid().ToString();
        (string, string?, string?)[] props =
        [
            ("ResLinkId", null, linkId),
            ("FeatureId", null, Guid.NewGuid().ToString()),
            ("Note", null, "seen from the ridge"),
            ("AddedBy", null, Guid.NewGuid().ToString()),
            ("IsMain", null, "false"),
            ("SortOrder", null, "0"),
        ];

        // Hidden: the event stays — a membership changed — while which link it joined does
        // not, because that association is exactly what the live link reads withhold. The
        // timeline can consult neither the reveal setting nor the link's siblings, so it is
        // strictly more restrictive than the live answer, the only direction it may differ in.
        var hidden = HistoryProtection.Redact(
            "ResLinkMember", Changes(props), governingHidden: true, NoLinkHidden, associationHidden: false, mayWriteSubject: false, peopleHidden: false, memberHidden: NoMemberHidden);
        hidden.Redacted.Order().ShouldBe(["AddedBy", "Note", "ResLinkId"]);
        hidden.Changes!.ContainsKey("ResLinkId").ShouldBeFalse();
        hidden.Changes.ContainsKey("SortOrder").ShouldBeTrue();
        hidden.Changes.ToJsonString().ShouldNotContain(linkId);

        // And a caller who may place the feature exactly reads the whole row.
        var shown = HistoryProtection.Redact(
            "ResLinkMember", Changes(props), governingHidden: false, NoLinkHidden, associationHidden: false, mayWriteSubject: false, peopleHidden: false, memberHidden: NoMemberHidden);
        shown.Redacted.ShouldBeEmpty();
        shown.Changes!.ContainsKey("ResLinkId").ShouldBeTrue();
        shown.Changes.ContainsKey("Note").ShouldBeTrue();
    }

    [Fact]
    public void A_trips_account_of_what_went_wrong_is_only_in_the_timeline_of_somebody_who_may_change_it()
    {
        var incident = "Wrong turn at the sump; Ana out of light on the way back.";
        (string, string?, string?)[] props =
        [
            ("Title", "Old title", "New title"),
            ("HadIncident", "false", "true"),
            ("Safety", null, $$"""{"incident_account":"{{incident}}"}"""),
            ("SafetySchemaVersion", null, "1"),
        ];

        // Withheld: the trip says that something went wrong — that is the fact a club counts,
        // and it stays — while what went wrong does not, because the live record withholds it
        // from the same caller and a diff would be the way around that.
        var reader = HistoryProtection.Redact(
            nameof(TripLog), Changes(props), governingHidden: false, NoLinkHidden,
            associationHidden: false, mayWriteSubject: false, peopleHidden: false, memberHidden: NoMemberHidden);
        reader.Redacted.ShouldBe(["Safety"]);
        reader.Changes!.ContainsKey("Safety").ShouldBeFalse();
        reader.Changes.ContainsKey("HadIncident").ShouldBeTrue();
        reader.Changes.ContainsKey("Title").ShouldBeTrue();
        reader.Changes.ToJsonString().ShouldNotContain(incident);

        // And whoever may change the trip reads the account, which is the half that proves the
        // rule is a rule about the audience and not about the field always being dropped.
        var writer = HistoryProtection.Redact(
            nameof(TripLog), Changes(props), governingHidden: false, NoLinkHidden,
            associationHidden: false, mayWriteSubject: true, peopleHidden: false, memberHidden: NoMemberHidden);
        writer.Redacted.ShouldBeEmpty();
        writer.Changes!.ToJsonString().ShouldContain(incident);
    }

    /// <summary>
    /// A camp's timeline is read by everybody who may read the camp, which is routinely a wider
    /// audience than the trips gathered into it — so a membership row names the trip only to
    /// somebody who may read that trip. Without this, an id the camp's trip listing and its
    /// roll-up both withhold arrives by a side door, which is the enumeration those two are
    /// composed to prevent.
    /// </summary>
    [Fact]
    public void Camp_membership_names_a_trip_only_to_somebody_who_may_read_it()
    {
        var closed = Guid.CreateVersion7();
        var open = Guid.CreateVersion7();
        var campId = Guid.CreateVersion7().ToString();

        JsonObject Joining(Guid tripId) => Changes(
            ("ExpeditionId", null, campId),
            ("TripLogId", null, tripId.ToString()),
            ("JoinedAt", null, "2026-07-18T09:00:00+00:00"));

        // Withheld: the event stays — a trip joined the camp, and when — while which trip it
        // was does not, and the camp it joined is the timeline's own subject.
        var hidden = HistoryProtection.Redact(
            nameof(ExpeditionTrip), Joining(closed), governingHidden: false, NoLinkHidden,
            associationHidden: false, mayWriteSubject: false, peopleHidden: false, memberHidden: id => id == closed);
        hidden.Redacted.ShouldBe([nameof(ExpeditionTrip.TripLogId)]);
        hidden.Changes!.ContainsKey(nameof(ExpeditionTrip.TripLogId)).ShouldBeFalse();
        hidden.Changes.ToJsonString().ShouldNotContain(closed.ToString());
        hidden.Changes.ContainsKey("JoinedAt").ShouldBeTrue();
        hidden.Changes.ContainsKey("ExpeditionId").ShouldBeTrue();

        // And a trip the caller may read is named, which is what proves the removal is driven by
        // the predicate rather than by the property name.
        var shown = HistoryProtection.Redact(
            nameof(ExpeditionTrip), Joining(open), governingHidden: false, NoLinkHidden,
            associationHidden: false, mayWriteSubject: false, peopleHidden: false, memberHidden: id => id == closed);
        shown.Redacted.ShouldBeEmpty();
        shown.Changes!.ToJsonString().ShouldContain(open.ToString());

        // A trip leaving discloses exactly as much as one joining, so the departure row is read
        // on the same side of the diff.
        var left = HistoryProtection.Redact(
            nameof(ExpeditionTrip),
            Changes(("ExpeditionId", campId, null), ("TripLogId", closed.ToString(), null)),
            governingHidden: false, NoLinkHidden, associationHidden: false, mayWriteSubject: false,
            peopleHidden: false, memberHidden: id => id == closed);
        left.Redacted.ShouldBe([nameof(ExpeditionTrip.TripLogId)]);
        left.Changes?.ToJsonString().ShouldNotContain(closed.ToString());
    }

    /// <summary>
    /// The live roster is stricter than the camp that holds it: it answers a caller who may read
    /// the camp and may read people, and refuses the whole listing to the one who holds only the
    /// first. A roster row on the camp's timeline is read by everybody who may read the camp, so
    /// it says exactly what the live route says under the same condition — nothing — otherwise
    /// the timeline is the way around a refusal the roster route makes on purpose.
    /// </summary>
    [Fact]
    public void Camp_roster_names_a_person_only_to_somebody_who_may_read_people()
    {
        var caver = Guid.CreateVersion7();
        var campId = Guid.CreateVersion7().ToString();

        JsonObject Stay() => Changes(
            ("ExpeditionId", null, campId),
            ("CaverId", null, caver.ToString()),
            ("FromDate", null, "2026-07-18"),
            ("ToDate", null, "2026-07-25"));

        // Withheld: the whole stay goes, not only the name on it. The live route refuses the
        // listing outright and says why — rows with the names struck out would still say how many
        // people were there and when — so keeping the dates here would be the softer answer the
        // route deliberately does not give. The event that a stay was recorded remains.
        var hidden = HistoryProtection.Redact(
            nameof(ExpeditionRosterEntry), Stay(), governingHidden: false, NoLinkHidden,
            associationHidden: false, mayWriteSubject: false, peopleHidden: true, memberHidden: NoMemberHidden);
        hidden.Changes.ShouldBeNull();
        hidden.Redacted.ShouldBe(
            ["ExpeditionId", nameof(ExpeditionRosterEntry.CaverId), "FromDate", "ToDate"],
            ignoreOrder: true);

        // A per-person note is free text on the row beside the name, so it is the property that
        // most often gives back the identity the redaction removed. It goes with the rest.
        var noteOnly = HistoryProtection.Redact(
            nameof(ExpeditionRosterEntry),
            Changes(("Note", "half days only", "drove the van")),
            governingHidden: false, NoLinkHidden, associationHidden: false, mayWriteSubject: false,
            peopleHidden: true, memberHidden: NoMemberHidden);
        noteOnly.Changes.ShouldBeNull();
        noteOnly.Redacted.ShouldBe(["Note"]);

        // And a caller who may read people is told everything, which is what proves the removal
        // is driven by the right rather than by the entity type.
        var shown = HistoryProtection.Redact(
            nameof(ExpeditionRosterEntry), Stay(), governingHidden: false, NoLinkHidden,
            associationHidden: false, mayWriteSubject: false, peopleHidden: false, memberHidden: NoMemberHidden);
        shown.Redacted.ShouldBeEmpty();
        shown.Changes!.ToJsonString().ShouldContain(caver.ToString());
        shown.Changes.ContainsKey("FromDate").ShouldBeTrue();

        // Somebody removed from the roster discloses exactly as much as somebody added, so the
        // departure row is read on the same side of the diff.
        var removed = HistoryProtection.Redact(
            nameof(ExpeditionRosterEntry),
            Changes(("ExpeditionId", campId, null), ("CaverId", caver.ToString(), null)),
            governingHidden: false, NoLinkHidden, associationHidden: false, mayWriteSubject: false,
            peopleHidden: true, memberHidden: NoMemberHidden);
        removed.Changes.ShouldBeNull();
        removed.Redacted.ShouldContain(nameof(ExpeditionRosterEntry.CaverId));
    }

    [Fact]
    public void Null_changes_pass_through()
    {
        var result = HistoryProtection.Redact("Feature:Cave", null, governingHidden: true, NoLinkHidden, associationHidden: false, mayWriteSubject: false, peopleHidden: false, memberHidden: NoMemberHidden);

        result.Changes.ShouldBeNull();
        result.Redacted.ShouldBeEmpty();
    }
}
