// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json.Nodes;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;

namespace SilexGis.Domain.Geo;

/// <summary>
/// Protection-of-history: the audit trail records precise coordinates (geometry as WKT) and
/// other location-revealing fields in its change diffs. This mirrors the live-DTO masking
/// (see <see cref="LocationProtection"/>) for historical values, so a caller who may not
/// view a protected feature's exact location cannot recover it from the timeline either.
/// <para>
/// Feature-world rows arrive with kind-qualified types ("Feature:Cave", …, see
/// <see cref="FeatureAudit"/>) and carry the merged supertype+subtype diff, so each case
/// below covers both parts of the aggregate.
/// </para>
/// <para>
/// Redacted properties are <em>removed</em> from the payload (no masked placeholder that could
/// leak shape or length) and named so the UI can show an honest "hidden (protected location)"
/// row — the event itself (who, when, "Geom changed") is activity metadata, not location data,
/// and stays visible.
/// </para>
/// <para>
/// <b>Current protection state governs.</b> Redaction follows the present protected-ancestor
/// set and the caller's present grants, not the state at each row's timestamp (which the
/// diffs cannot reconstruct, and which would leak during the pre-protection window anyway —
/// and which the owner confirmed for re-parenting: moving a subtree under a protected root
/// retroactively hides its coordinate history).
/// </para>
/// </summary>
public static class HistoryProtection
{
    // Derived/bookkeeping props that are never interesting in a timeline; dropped silently
    // (not counted as protection-redacted). The cave feature's Geom is the derived
    // main-entrance cache — location data, removed for everyone as defence in depth (the
    // entrance's own row carries the governed original). AncestorIds/IsProtectedEffective
    // are write-service bookkeeping. The schema-version stamps record which version of a
    // kind's schema a row was validated against — a number the write path moves on its own,
    // so a reader would see it change without anyone having changed anything.
    private static readonly string[] AlwaysNoise =
        ["CreatedAt", "UpdatedAt", nameof(Feature.AncestorIds), nameof(Feature.IsProtectedEffective),
         nameof(Feature.PropertiesSchemaVersion), nameof(Document.MetadataSchemaVersion),
         nameof(TripLog.FieldDataSchemaVersion), nameof(TripLog.LogisticsSchemaVersion),
         nameof(TripLog.SafetySchemaVersion)];
    private static readonly string[] CaveNoise = [nameof(Cave.EntranceCount), nameof(Feature.Geom)];

    // A stored file's created/deleted snapshot carries its EXIF capture point (Geom), the raw
    // metadata jsonb (which may itself hold GPS EXIF tags) and the internal storage path. A file
    // is a polymorphic child with no governing feature resolvable here, and its geotag IS
    // location data — so drop all three for everyone (defence in depth), never emitted in any
    // timeline. The page count joins them as bookkeeping rather than as protection: it is
    // filled in by whatever reads the file, so it moves without a person having done anything.
    // The text-extraction state and its error are there for the same reason: they are a
    // background reader's progress notes, and a timeline of them would record the queue's
    // scheduling rather than anybody's decision.
    private static readonly string[] FileNoise =
        [nameof(StoredFile.Geom), nameof(StoredFile.Metadata), nameof(StoredFile.StoragePath),
         nameof(StoredFile.PageCount), nameof(StoredFile.TextExtraction),
         nameof(StoredFile.TextExtractionError)];

    // Coordinate-bearing fields, mirroring the live DTO masking exactly. Named via nameof so a
    // property rename is a compile error here rather than a silent redaction (location) leak.
    private static readonly string[] CaveSensitive =
        [nameof(Cave.ClosestAddress), nameof(Cave.LandRegistryNumber), nameof(Cave.LocationNotes)];
    private static readonly string[] EntranceSensitive =
        [nameof(Feature.Geom), nameof(CaveEntrance.Altitude), nameof(CaveEntrance.PositionQuality)];

    // Which document is attached to a feature is the association, and whether a caller is
    // told it is decided by one rule that lives beside this one. The timeline does not
    // re-derive that decision — a second copy of it is how one surface comes to disagree
    // with the others — it is handed the answer and removes the pairing when the answer is
    // that the pairing is withheld.
    private static readonly string[] AttachmentSensitive =
        [nameof(Attachment.FileId), nameof(Attachment.Caption)];

    // A resource-link membership rooted at a feature is the same association stated the
    // other way around: which links name a guarded feature is what the live link reads
    // withhold from callers without exact view. Those reads can weigh the installation's
    // reveal setting and the link's sibling members; an audit row carries neither, so a
    // hidden timeline keeps the event — a membership changed — and names no link at all.
    // The note and the adder travel with the membership they describe, and the anchor
    // fields join them as defence in depth (a feature membership is whole-only today, but
    // a payload can quote and a pin can route, so neither may outlive that rule here).
    private static readonly string[] ResLinkMemberSensitive =
        [nameof(ResLinkMember.ResLinkId), nameof(ResLinkMember.Note), nameof(ResLinkMember.AddedBy),
         nameof(ResLinkMember.Anchor), nameof(ResLinkMember.AnchorFileId)];

    private static readonly string FeatureCave = FeatureAudit.TypeName(FeatureKind.Cave);
    private static readonly string FeatureEntrance = FeatureAudit.TypeName(FeatureKind.CaveEntrance);
    private static readonly string FeatureCenterline = FeatureAudit.TypeName(FeatureKind.Centerline);
    private static readonly string FeatureGeneric = FeatureAudit.TypeName(FeatureKind.Generic);

    /// <summary>Property names dropped as noise for the given entity type (never shown).</summary>
    public static IReadOnlyList<string> NoiseFor(string entityType) =>
        entityType == FeatureCave ? [.. AlwaysNoise, .. CaveNoise]
        : entityType == nameof(StoredFile) ? [.. AlwaysNoise, .. FileNoise]
        : AlwaysNoise;

    /// <summary>
    /// Strips noise and — when the row's protected ancestry denies the caller exact view —
    /// the location-revealing properties from one history row's change set.
    /// </summary>
    /// <param name="entityType">Audit type name of the row ("Feature:Cave", "SurveyModel", …).</param>
    /// <param name="changes">Parsed change JSON (mutated in place); null when the row has none.</param>
    /// <param name="governingHidden">
    /// The row's protected ancestry (its own protection root or any protected ancestor)
    /// denies the caller exact view.
    /// </param>
    /// <param name="linkTargetHidden">
    /// Predicate over a referenced feature id: whether a locating link to it must be hidden
    /// (for rows that merely reference a protected feature — feature links, trip cave-links).
    /// </param>
    /// <param name="associationHidden">
    /// Whether the caller is kept from being told what this row's attachment points at, as
    /// the association rule decides it — which weighs the installation's reveal setting and
    /// whether the document behind the attachment carries coordinates of its own. Passed in
    /// rather than derived here, so the timeline and the live surfaces cannot come to
    /// different answers about the same pairing. Ignored for every other entity type.
    /// </param>
    /// <param name="mayWriteSubject">
    /// Whether the caller may change the entity this timeline is about. Some rows hold a part
    /// that is told to a narrower audience than the row itself — a trip's account of what went
    /// wrong names identifiable people making mistakes — and reading it out of a diff would be
    /// a way around the live rule. Passed in for the same reason the association answer is:
    /// the timeline does not re-derive a disclosure decision, it is handed the answer.
    /// Ignored for every entity type that has no such part.
    /// </param>
    /// <param name="peopleHidden">
    /// Whether this caller may not read people at all. Asked as one flag rather than per id
    /// because that is the shape of the right: reading people is held across the board or not
    /// held, which is exactly the question the live roster asks before it will answer at all. A
    /// caller who fails it is refused the whole roster listing — and a roster row on the camp's
    /// timeline carries the same stay, so keeping its dates, its note and its role while striking
    /// out the person would still say how many people were at the camp and when, and a per-person
    /// note frequently names the person the redaction just removed. The row's whole change set
    /// goes; the event stays, so the page can show an honest hidden row.
    /// </param>
    /// <param name="memberHidden">
    /// Predicate over an id a camp's membership row names — the trip it joined — answering whether
    /// this caller may not read that trip. Those rows are rooted at the camp, so they reach
    /// everybody who may read the camp, an audience wider than the trips gathered into it, each
    /// governed in its own right. The camp's own listing withholds what the caller may not read,
    /// and the timeline says the same thing rather than handing the id over by a side door.
    /// </param>
    public static RedactionResult Redact(
        string entityType,
        JsonObject? changes,
        bool governingHidden,
        Func<Guid, bool> linkTargetHidden,
        bool associationHidden,
        bool mayWriteSubject,
        bool peopleHidden,
        Func<Guid, bool> memberHidden)
    {
        if (changes is null)
        {
            return new RedactionResult(null, []);
        }

        foreach (var key in NoiseFor(entityType))
        {
            changes.Remove(key);
        }

        var redacted = new List<string>();
        if (entityType == FeatureCave)
        {
            if (governingHidden)
            {
                RemoveNamed(changes, CaveSensitive, redacted);
            }
        }
        else if (entityType == FeatureEntrance)
        {
            if (governingHidden)
            {
                RemoveNamed(changes, EntranceSensitive, redacted);
            }
        }
        else if (entityType == FeatureCenterline || entityType == nameof(SurveyModel))
        {
            // Coordinate-bearing rows in a protected timeline: when hidden, drop the whole
            // payload but keep the event. These rows only reach a caller who already failed
            // the exact-location check.
            if (governingHidden && changes.Count > 0)
            {
                redacted.AddRange(changes.Select(kv => kv.Key));
                return new RedactionResult(null, redacted);
            }
        }
        else if (entityType == FeatureGeneric)
        {
            // A generic feature under a protected root: its geometry is governed by the
            // ancestry like any descendant (snap/withhold live; here: removed when hidden).
            if (governingHidden)
            {
                RemoveNamed(changes, [nameof(Feature.Geom)], redacted);
            }
        }
        else if (entityType == nameof(FeatureLink))
        {
            // A locating link's endpoints disclose a protected feature's position by
            // proximity. Either endpoint hidden → the row's ids are removed.
            RemoveHiddenReference(changes, nameof(FeatureLink.FromId), linkTargetHidden, redacted);
            RemoveHiddenReference(changes, nameof(FeatureLink.ToId), linkTargetHidden, redacted);
        }
        else if (entityType == nameof(Attachment))
        {
            if (associationHidden)
            {
                RemoveNamed(changes, AttachmentSensitive, redacted);
            }
        }
        else if (entityType == nameof(ResLinkMember))
        {
            if (governingHidden)
            {
                RemoveNamed(changes, ResLinkMemberSensitive, redacted);
            }
        }
        else if (entityType == nameof(TripLog))
        {
            // The trip's account of what went wrong is told only to whoever may change the
            // trip, so a diff of it is too — otherwise the timeline would hand a read-only
            // caller the very text the record withholds from them. Named rather than dropped
            // as noise, so the page can show an honest "hidden" row instead of pretending the
            // edit never happened.
            if (!mayWriteSubject)
            {
                RemoveNamed(changes, TripDisclosure.WriterOnly, redacted);
            }
        }
        else if (entityType == nameof(ExpeditionTrip))
        {
            // Which trip joined or left, named only to somebody who may read that trip. The row
            // hangs on the camp, and a camp is routinely readable by a wider audience than the
            // trips in it; the event — a membership changed, and when — is activity metadata and
            // stays, exactly as it does for a hidden resource-link membership.
            RemoveHiddenReference(changes, nameof(ExpeditionTrip.TripLogId), memberHidden, redacted);
        }
        else if (entityType == nameof(ExpeditionRosterEntry))
        {
            // Who was at the camp, and every other thing the stay records, told only to somebody
            // who may read people at all. The live roster is stricter than the camp itself on
            // purpose — it answers a caller who holds the read over the camp and the read over
            // people, and refuses the one who holds only the first, giving as its reason that rows
            // with the names struck out would still say how many people were there and when. The
            // timeline is the same rows, so it withholds the same thing: the whole change set
            // goes, not just the id. Anything less and the free-text note beside the id — "half
            // days only, drove the van" — hands back the identity the redaction just removed.
            if (peopleHidden && changes.Count > 0)
            {
                redacted.AddRange(changes.Select(kv => kv.Key));
                return new RedactionResult(null, redacted);
            }
        }

        return new RedactionResult(changes.Count == 0 ? null : changes, redacted);
    }

    private static void RemoveNamed(JsonObject changes, string[] names, List<string> redacted)
    {
        foreach (var name in names)
        {
            if (changes.Remove(name))
            {
                redacted.Add(name);
            }
        }
    }

    // Removes an id-bearing property when either the old or new referenced feature must be
    // hidden — a locating reference on a record with exact coordinates would disclose the
    // target by proximity.
    private static void RemoveHiddenReference(
        JsonObject changes, string property, Func<Guid, bool> linkTargetHidden, List<string> redacted)
    {
        if (changes[property] is not JsonObject pair)
        {
            return;
        }

        if ((ReferencedHidden(pair["old"], linkTargetHidden) || ReferencedHidden(pair["new"], linkTargetHidden))
            && changes.Remove(property))
        {
            redacted.Add(property);
        }
    }

    private static bool ReferencedHidden(JsonNode? side, Func<Guid, bool> linkTargetHidden) =>
        side is JsonValue value && value.TryGetValue<string>(out var text)
        && Guid.TryParse(text, out var id) && linkTargetHidden(id);
}

/// <summary>Outcome of <see cref="HistoryProtection.Redact"/>: the surviving change set and the removed property names.</summary>
public sealed record RedactionResult(JsonObject? Changes, IReadOnlyList<string> Redacted);
