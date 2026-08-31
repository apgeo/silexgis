// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SilexGis.Domain.Import;

/// <summary>
/// What an import does with the altitude the source file carries.
///
/// <para>
/// The file is the only source. Deriving an altitude from an elevation model is deliberately
/// not offered: a public model is a grid tens of metres across, and beside a cliff — which is
/// where entrances are — it is wrong by a hundred metres in a way nothing on screen would show.
/// A number that looks surveyed and is not is worse than an empty field.
/// </para>
/// </summary>
public enum ImportElevationPolicy
{
    /// <summary>Keep what the file says. Right for a file from a barometric unit or a survey.</summary>
    Keep = 0,

    /// <summary>Leave the altitude empty. Right for a handheld fix, where the vertical error is the worst of the three.</summary>
    Discard = 1,

    /// <summary>
    /// Decide per candidate. Nothing is taken unless a row says so, because a file where the
    /// altitude is trustworthy for some points and not others is the normal case: a survey
    /// station and a walk-past waypoint sit in the same GPX.
    /// </summary>
    PerCandidate = 2,
}

/// <summary>What an import does with the tracks and routes in a file.</summary>
public enum ImportTrackHandling
{
    /// <summary>Leave them where they are: drawn as the file's own map layer, not in the registry.</summary>
    Ignore = 0,

    /// <summary>Create a line feature of the chosen type — an approach path or a search sweep worth keeping.</summary>
    ImportAsLine = 1,
}

/// <summary>What becomes of a point no term rule claimed.</summary>
/// <remarks>
/// The counterpart of <see cref="ImportTrackHandling"/> for points, and it exists for a measured
/// reason: a rule set recognises the names a group actually writes down, and the file a GPS unit
/// produces at the end of a season is mostly <c>WPT0142</c>. Those rows arrive proposing nothing,
/// and a row proposing nothing cannot be selected — so "select everything and import it" quietly
/// selects the fraction the rules happened to name, and the reviewer's only other route is to give
/// several thousand rows a kind one row at a time. Naming the fallback once, here, is the whole of
/// the fix, and it stays a deliberate choice rather than a default because importing every stray
/// waypoint as a registry object is the wrong answer just as often as it is the right one.
/// </remarks>
public enum ImportUnmatchedPoints
{
    /// <summary>
    /// Leave them for the reviewer, one at a time. What happened before this setting existed.
    /// </summary>
    Ignore = 0,

    /// <summary>Propose a surface feature of the chosen type.</summary>
    ImportAsSurfaceFeature = 1,

    /// <summary>Propose a cave entrance of the chosen type.</summary>
    ImportAsCaveEntrance = 2,
}

/// <summary>What the reviewer decided about one candidate.</summary>
public enum ImportDecisionAction
{
    /// <summary>Create the proposed object.</summary>
    Create = 0,

    /// <summary>
    /// Do not create anything: the candidate is a position already in the registry.
    /// <see cref="ImportDecision.AttachToFeatureId"/> records which one, so the batch keeps
    /// the evidence that this waypoint was seen and deliberately not duplicated.
    /// </summary>
    Attach = 1,

    /// <summary>Leave it out.</summary>
    Skip = 2,
}

/// <summary>
/// Which source field becomes which attribute. Empty means "work it out": the reader tries
/// the names GPS units and desktop tools actually write, in the order below.
/// </summary>
public sealed record ImportAttributeMapping
{
    public string? NameField { get; init; }

    public string? DescriptionField { get; init; }

    public string? CodeField { get; init; }

    public string? ElevationField { get; init; }

    /// <summary>Source keys tried for the name, best first.</summary>
    public static IReadOnlyList<string> NameCandidates { get; } =
        ["name", "Name", "NAME", "title", "cmt", "desc", "label", "id"];

    /// <summary>Source keys tried for the description, best first.</summary>
    public static IReadOnlyList<string> DescriptionCandidates { get; } =
        ["desc", "description", "cmt", "comment", "notes", "note"];

    /// <summary>Source keys tried for an identification code, best first.</summary>
    public static IReadOnlyList<string> CodeCandidates { get; } =
        ["code", "cod", "ref", "reference", "number", "nr"];

    /// <summary>Source keys tried for an altitude, best first.</summary>
    public static IReadOnlyList<string> ElevationCandidates { get; } =
        ["ele", "elevation", "alt", "altitude", "altitudine", "z", "height"];
}

/// <summary>
/// Everything an import decides once, for the whole file, rather than per row: which rules
/// ran, what the created objects get bound to, how altitude and tracks are treated, and how
/// far duplicate detection looks.
/// </summary>
public sealed record ImportOptions
{
    public Guid? TermRuleSetId { get; init; }

    /// <summary>Languages whose terms take part. Empty means every language the set holds.</summary>
    public IReadOnlyList<string> Languages { get; init; } = [];

    public ImportAttributeMapping Mapping { get; init; } = new();

    public ImportElevationPolicy Elevation { get; init; } = ImportElevationPolicy.Keep;

    public ImportTrackHandling Tracks { get; init; } = ImportTrackHandling.Ignore;

    /// <summary>Feature-type code line work becomes when <see cref="Tracks"/> imports it.</summary>
    public string? TrackFeatureTypeCode { get; init; }

    /// <summary>What to propose for a point no rule claimed. See <see cref="ImportUnmatchedPoints"/>.</summary>
    public ImportUnmatchedPoints UnmatchedPoints { get; init; } = ImportUnmatchedPoints.Ignore;

    /// <summary>
    /// Type code an unclaimed point takes when <see cref="UnmatchedPoints"/> proposes something for
    /// it — a feature type or an entrance type, according to which kind was chosen.
    /// </summary>
    public string? UnmatchedTypeCode { get; init; }

    /// <summary>
    /// Visibility every created object starts with. Defaults to the most restrictive value
    /// rather than to whatever the file's geofile carries — an import is a bulk action, and a
    /// bulk action that publishes by default publishes a whole trip's worth of holes at once.
    /// </summary>
    public Visibility Visibility { get; init; } = Visibility.Private;

    public Guid? CavingGroupId { get; init; }

    /// <summary>Marks every created object a protection root.</summary>
    public bool LocationProtected { get; init; }

    public IReadOnlyList<long> TagIds { get; init; } = [];

    public string? NamePrefix { get; init; }

    /// <summary>How far duplicate detection looks around a candidate.</summary>
    public double DuplicateRadiusMeters { get; init; } = DefaultDuplicateRadiusMeters;

    public const double DefaultDuplicateRadiusMeters = 50;

    /// <summary>The widest search duplicate detection will run — a bulk proximity query, so it is bounded.</summary>
    public const double MaxDuplicateRadiusMeters = 5000;
}

/// <summary>
/// One reviewed candidate. Every field beyond <see cref="Action"/> is an override of what the
/// rules proposed; leaving one null keeps the proposal, so a reviewer who agrees with a row
/// stores almost nothing.
/// </summary>
public sealed record ImportDecision
{
    public ImportDecisionAction Action { get; init; } = ImportDecisionAction.Create;

    public ImportTargetKind? Kind { get; init; }

    public string? FeatureTypeCode { get; init; }

    public string? CaveTypeCode { get; init; }

    public string? EntranceTypeCode { get; init; }

    public string? Name { get; init; }

    /// <summary>
    /// For <see cref="ImportDecisionAction.Attach"/>, the existing feature this candidate is.
    /// For a <see cref="ImportTargetKind.CaveEntrance"/> being created, the cave it becomes an
    /// entrance of — which is how a candidate becomes a *second* entrance of a cave already in
    /// the registry rather than a new cave of its own.
    /// </summary>
    public Guid? AttachToFeatureId { get; init; }

    /// <summary>
    /// Whether this candidate keeps the altitude its row carries. Null follows the import's own
    /// policy; true and false answer for this row alone, which is what the per-candidate policy
    /// exists for. There is no third value: the file is the only source of an altitude.
    /// </summary>
    public bool? KeepElevation { get; init; }
}

/// <summary>
/// The single JSON shape for everything import stores or exchanges: the rule documents in a
/// set, the options and decisions on a session, the snapshot on a batch, and the file two
/// clubs send each other. One options object, so a rule set written by the API and one read
/// back off disk cannot disagree about how an enum is spelled.
/// </summary>
public static class ImportJson
{
    public static JsonSerializerOptions Options { get; } = Build();

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    private static JsonSerializerOptions Build()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
