// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.ResLinks;

/// <summary>One shipped relation-type row. <paramref name="Name"/> reads from the main
/// member towards the rest; <paramref name="InverseName"/> reads back from the non-main
/// side and only directed relations carry one.</summary>
public readonly record struct ResLinkRelationTypeSeed(
    string Code, string Name, bool Directed, string? InverseName);

/// <summary>
/// The shipped relation vocabulary — the single home for which codes are the exchange
/// vocabulary. The startup seeder inserts these rows, and the admin surface consults the
/// same list to refuse code changes and deletion on them: seeded codes are what clients
/// translate labels by and what installations exchange, so a renamed or deleted code
/// would silently break both. Custom rows are installation-local and fully editable.
/// </summary>
public static class ResLinkRelationTypeSeeds
{
    /// <summary>
    /// Append only. The seeder derives a row's sort order from its position here and never
    /// re-sorts a row that already exists, so a code inserted in the middle takes a different
    /// sort order on a fresh installation than on one being upgraded — silently, and only the
    /// vocabulary's display order gives it away.
    /// </summary>
    public static readonly IReadOnlyList<ResLinkRelationTypeSeed> All =
    [
        new("same-object", "Same object as", false, null),
        new("related-to", "Related to", false, null),
        new("contains", "Contains", true, "Contained in"),
        new("documents", "Documented by", true, "Documents"),
        new("derived-from", "Source of", true, "Derived from"),
        new("adjacent-to", "Adjacent to", false, null),
        new("duplicate-of", "Original of", true, "Duplicate of"),
        new("needs-clarification", "Needs clarification", false, null),

        // What a trip did to what it names. Directed with the trip as the main member, so the
        // forward name reads out of the trip ("Surveyed") and the inverse reads back from what
        // the trip named ("Surveyed on trip"); undirected roles would be ambiguous the first
        // time two trips ended up in one link. The codes are prefixed because this vocabulary
        // is shared and administrator-extensible — a bare "visited" would read as a general
        // relation between any two things, which it is not.
        new("trip-work-area", "Worked in", true, "Work area of trip"),
        new("trip-objective", "Aimed at", true, "Objective of trip"),
        new("trip-visited", "Visited", true, "Visited on trip"),
        new("trip-surveyed", "Surveyed", true, "Surveyed on trip"),
        new("trip-discovered", "Discovered", true, "Discovered on trip"),
        new("trip-dug", "Dug at", true, "Dug on trip"),
        new("trip-photographed", "Photographed", true, "Photographed on trip"),
        new("trip-searched-not-found", "Searched, not found", true, "Searched for on trip, not found"),
        new("trip-lead", "Left lead", true, "Lead left on trip"),
        new("trip-follows-on-from", "Follows on from", true, "Followed up by"),

        // A link-annotated text and the scanned or word-processed document it is the reading
        // of. Directed with the annotated text as the main member, so the forward name reads
        // out of it ("Text of") and the inverse reads back from the file it transcribes ("Has
        // text"). Distinct from "documents", which relates a document to the thing in the
        // world it is about; this relates two representations of the same words.
        //
        // At the end, rather than beside the other document relations where it would read
        // better, because the list says append only and means it: a row's sort order is its
        // position here, so inserting it in the middle silently renumbered every trip role
        // below it — and only on a fresh installation, so a new database and an upgraded one
        // would disagree about the vocabulary's order with nothing on screen to say why.
        new("text-of", "Text of", true, "Has text"),

        // A raster map of a survey model, by view. Directed with the map document as the main
        // member, so edit/delete rights follow document write access and the forward name reads
        // out of the map. The code carries the view kind: a designed vocabulary with zero
        // behavior attached, exactly the trip-role pattern.
        new("map-plan-of", "Plan map of", true, "Has plan map"),
        new("map-profile-of", "Profile map of", true, "Has profile map"),
        new("map-other-of", "Map of (other view)", true, "Has map (other view)"),

        // A point on a raster map that IS a survey station — a calibration-grade claim, kept
        // distinct from casual "this region shows the sump" links so the map surfaces never
        // promote an annotation into a position. Directed with the document member as main, so
        // whoever may edit the map document may correct its pins.
        new("map-station-point", "Marks station", true, "Marked on map"),
    ];

    /// <summary>
    /// The shipped codes that say what a trip did to what it named, derived from the list
    /// above by the prefix the trip vocabulary is built on. This is the single answer to
    /// "which features is this trip about": the roles record what was done there, and every
    /// reader that only wants the association itself asks over all of them at once. Narrowing
    /// such a read to one role would answer a different, smaller question without saying so.
    ///
    /// Seeded rows only. An installation that adds its own <c>trip-…</c> code gets it in the
    /// general link panel, not in the set the trip surfaces are built from — a field is a
    /// designed surface, and an extensible vocabulary does not make one.
    /// </summary>
    public static readonly string[] TripRoleCodes =
        [.. All.Select(s => s.Code).Where(c => c.StartsWith("trip-", StringComparison.Ordinal))];

    /// <summary>
    /// The shipped codes that declare a document to be a raster map of a survey model, one
    /// per view kind. Explicit list, NOT prefix-derived: a "map-" prefix rule would swallow
    /// <see cref="MapStationPointCode"/>, which names a pin rather than a map.
    ///
    /// Seeded rows only, as with <see cref="TripRoleCodes"/>: an installation's custom
    /// <c>map-*</c> code joins the generic link panel, not the map tabs — a designed surface
    /// is built from designed vocabulary.
    /// </summary>
    public static readonly string[] MapViewCodes = [MapPlanOfCode, MapProfileOfCode, MapOtherOfCode];

    /// <summary>The code declaring a document the plan view of a survey model.</summary>
    public const string MapPlanOfCode = "map-plan-of";

    /// <summary>The code declaring a document the profile view of a survey model.</summary>
    public const string MapProfileOfCode = "map-profile-of";

    /// <summary>The code declaring a document some other view of a survey model.</summary>
    public const string MapOtherOfCode = "map-other-of";

    /// <summary>
    /// The shipped code claiming that a point on a raster map is a survey station. Kept out
    /// of <see cref="MapViewCodes"/> because it names a pin on a map, not a map of a model,
    /// and the two are consumed by different folds.
    /// </summary>
    public const string MapStationPointCode = "map-station-point";

    private static readonly HashSet<string> Codes = [.. All.Select(s => s.Code)];

    /// <summary>Whether a code names a shipped row (code and directedness immutable,
    /// row undeletable).</summary>
    public static bool IsSeeded(string code) => Codes.Contains(code);
}
