// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json.Nodes;
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Geo;

/// <summary>
/// Protection-of-history: the audit trail records precise coordinates (geometry as WKT) and
/// other location-revealing fields in its change diffs. This mirrors the live-DTO masking
/// (see <see cref="LocationProtection"/>) for historical values, so a caller who may not
/// view a protected cave's exact location cannot recover it from the timeline either.
/// <para>
/// Redacted properties are <em>removed</em> from the payload (no masked placeholder that could
/// leak shape or length) and named so the UI can show an honest "hidden (protected location)"
/// row — the event itself (who, when, "Geom changed") is activity metadata, not location data,
/// and stays visible.
/// </para>
/// <para>
/// <b>Current protection state governs.</b> Redaction follows the cave's present
/// <c>LocationProtected</c> flag and the caller's present grants, not the state at each row's
/// timestamp (which the diffs cannot reconstruct, and which would leak during the
/// pre-protection window anyway).
/// </para>
/// </summary>
public static class HistoryProtection
{
    // Derived/bookkeeping props that are never interesting in a timeline; dropped silently
    // (not counted as protection-redacted). MainGeom is also location data, hence removed for
    // everyone as defence in depth.
    private static readonly string[] AlwaysNoise = ["CreatedAt", "UpdatedAt"];
    private static readonly string[] CaveNoise = [nameof(Cave.EntranceCount), nameof(Cave.MainGeom)];

    // A stored file's created/deleted snapshot carries its EXIF capture point (Geom), the raw
    // metadata jsonb (which may itself hold GPS EXIF tags) and the internal storage path. A file
    // is a polymorphic child with no governing cave resolvable here, and its geotag IS location
    // data — so drop all three for everyone (defence in depth), never emitted in any timeline.
    private static readonly string[] FileNoise =
        [nameof(StoredFile.Geom), nameof(StoredFile.Metadata), nameof(StoredFile.StoragePath)];

    // Coordinate-bearing fields, mirroring the live DTO masking exactly. Named via nameof so a
    // property rename is a compile error here rather than a silent redaction (location) leak.
    private static readonly string[] CaveSensitive =
        [nameof(Cave.ClosestAddress), nameof(Cave.LandRegistryNumber), nameof(Cave.LocationNotes)];
    private static readonly string[] EntranceSensitive =
        [nameof(CaveEntrance.Geom), nameof(CaveEntrance.Altitude), nameof(CaveEntrance.PositionQuality)];

    /// <summary>Property names dropped as noise for the given entity type (never shown).</summary>
    public static IReadOnlyList<string> NoiseFor(string entityType) => entityType switch
    {
        nameof(Cave) => [.. AlwaysNoise, .. CaveNoise],
        nameof(StoredFile) => [.. AlwaysNoise, .. FileNoise],
        _ => AlwaysNoise,
    };

    /// <summary>
    /// Strips noise and — when the governing cave's location is hidden from the caller —
    /// the location-revealing properties from one history row's change set.
    /// </summary>
    /// <param name="entityType">CLR type name the audit row belongs to.</param>
    /// <param name="changes">Parsed change JSON (mutated in place); null when the row has none.</param>
    /// <param name="governingCaveHidden">
    /// The row's own cave (a Cave row) or parent cave (entrance/centerline/survey child rows)
    /// is protected and the caller may not view its exact location.
    /// </param>
    /// <param name="caveLinkHidden">
    /// Predicate over a referenced cave id: whether that cave link must be hidden (for rows
    /// that merely reference a cave — surface features, trip cave-links).
    /// </param>
    public static RedactionResult Redact(
        string entityType, JsonObject? changes, bool governingCaveHidden, Func<Guid, bool> caveLinkHidden)
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
        switch (entityType)
        {
            case nameof(Cave):
                if (governingCaveHidden)
                {
                    RemoveNamed(changes, CaveSensitive, redacted);
                }

                break;

            case nameof(CaveEntrance):
                if (governingCaveHidden)
                {
                    RemoveNamed(changes, EntranceSensitive, redacted);
                }

                break;

            case nameof(CaveCenterline):
            case nameof(SurveyModel):
                // Coordinate-bearing rows in a cave timeline: when the cave is hidden, drop the
                // whole payload but keep the event. These rows only reach a caller who already
                // failed the exact-location check.
                if (governingCaveHidden && changes.Count > 0)
                {
                    redacted.AddRange(changes.Select(kv => kv.Key));
                    return new RedactionResult(null, redacted);
                }

                break;

            case nameof(SurfaceFeature):
            case nameof(TripLogCave):
                // Own geometry stays exact (matches live policy); only the cave link is hidden.
                RemoveCaveLink(changes, caveLinkHidden, redacted);
                break;
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

    // Removes CaveId when either the old or new referenced cave must be link-hidden — a cave
    // link on a record with exact coordinates would disclose the cave by proximity.
    private static void RemoveCaveLink(JsonObject changes, Func<Guid, bool> caveLinkHidden, List<string> redacted)
    {
        if (changes["CaveId"] is not JsonObject pair)
        {
            return;
        }

        if ((ReferencedCaveHidden(pair["old"], caveLinkHidden) || ReferencedCaveHidden(pair["new"], caveLinkHidden))
            && changes.Remove("CaveId"))
        {
            redacted.Add("CaveId");
        }
    }

    private static bool ReferencedCaveHidden(JsonNode? side, Func<Guid, bool> caveLinkHidden) =>
        side is JsonValue value && value.TryGetValue<string>(out var text)
        && Guid.TryParse(text, out var id) && caveLinkHidden(id);
}

/// <summary>Outcome of <see cref="HistoryProtection.Redact"/>: the surviving change set and the removed property names.</summary>
public sealed record RedactionResult(JsonObject? Changes, IReadOnlyList<string> Redacted);
