// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Import;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Import;

/// <summary>What class of thing a source row draws.</summary>
public enum CandidateGeometry
{
    Point = 0,

    /// <summary>A track or a route: an approach path or a search sweep, not a place.</summary>
    Line = 1,

    Area = 2,

    /// <summary>A geometry class this pipeline has no answer for; never proposed.</summary>
    Other = 3,
}

/// <summary>
/// One candidate as the rules see it, before anything is fetched about where it sits. This is
/// what the dry run counts and what the review table filters and pages over.
/// </summary>
public sealed record CandidateSummary
{
    public required long SourceId { get; init; }

    public required CandidateGeometry Geometry { get; init; }

    public string? SourceName { get; init; }

    public string? SourceDescription { get; init; }

    public string? SourceCode { get; init; }

    public double? SourceElevation { get; init; }

    /// <summary>The row's own attributes, verbatim, as the file wrote them.</summary>
    public required string SourceProperties { get; init; }

    public string? RuleId { get; init; }

    public string? RuleName { get; init; }

    /// <summary>Rules that also matched and were beaten by order — the UI says so rather than arbitrating silently.</summary>
    public IReadOnlyList<string> ConflictingRuleNames { get; init; } = [];

    public ImportTargetKind? ProposedKind { get; init; }

    public string? ProposedCaveTypeCode { get; init; }

    public string? ProposedEntranceTypeCode { get; init; }

    public string? ProposedFeatureTypeCode { get; init; }

    /// <summary>The name as it would be created — mapped, stripped of the matched term, prefixed.</summary>
    public string? ProposedName { get; init; }
}

/// <summary>A candidate with everything the map preview and duplicate column need.</summary>
public sealed record CandidateDetail(CandidateSummary Summary, Geometry? Geom, DuplicateHint? Duplicate);

/// <summary>
/// The nearest thing already in the registry, and how alike its name is.
///
/// <para>
/// This is a location oracle and is treated as one: it is only ever computed against features
/// whose exact position the caller may already see. "There is something within twelve metres"
/// would otherwise hand a protected entrance's position to anybody who could upload a
/// waypoint near it and read the answer — a search that costs one file and converges in a few
/// rounds.
/// </para>
/// </summary>
public sealed record DuplicateHint(
    Guid FeatureId,
    string? Name,
    FeatureKind Kind,
    double DistanceMeters,
    double NameSimilarity,
    Guid? CaveFeatureId,
    string? CaveName);

/// <summary>The dry run: which rule claimed how many candidates, before anything is created.</summary>
public sealed record RuleHit(string RuleId, string RuleName, int Count);

/// <summary>
/// Builds the candidate list a file's review works from.
///
/// <para>
/// Nothing here writes. The candidates are the file's already-parsed rows, which is what makes
/// staging free: the upload is imported once into the geofile's own world, and the review is a
/// reading of those rows through a rule set. Re-running a preview with different rules costs a
/// scan, not a re-parse, and until a batch is confirmed the registry has not moved.
/// </para>
/// </summary>
public sealed class ImportCandidateService(SilexGisDbContext db, VisibleProximitySearch proximity)
{
    /// <summary>
    /// How many rows one scan reads. A file larger than this is still imported and still drawn
    /// as a layer — only the review is bounded, and the caller is told it was, because a
    /// silently truncated candidate list reads exactly like a complete one.
    /// </summary>
    public const int MaxScanRows = 50_000;

    /// <summary>
    /// Every row of the file, classified by the rule set. The whole file rather than a page:
    /// the hit counts are an answer about the file, and filtering "by rule" or "by kind" needs
    /// every row's answer before it can page anything.
    /// </summary>
    public async Task<(IReadOnlyList<CandidateSummary> Candidates, bool Truncated, int TotalRows)> ScanAsync(
        Guid geofileId,
        IReadOnlyList<TermRule> rules,
        ImportOptions options,
        CancellationToken ct = default)
    {
        var totalRows = await ImportSql.CountAsync(db, geofileId, ct);
        var rows = await ImportSql.ScanAsync(db, geofileId, MaxScanRows, ct);

        var candidates = new List<CandidateSummary>(rows.Count);
        foreach (var row in rows)
        {
            candidates.Add(Classify(row, rules, options));
        }

        return (candidates, totalRows > rows.Count, totalRows);
    }

    /// <summary>Per-rule counts over a scanned file.</summary>
    public static IReadOnlyList<RuleHit> HitsOf(
        IReadOnlyList<CandidateSummary> candidates, IReadOnlyList<TermRule> rules)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            if (candidate.RuleId is { } ruleId)
            {
                counts[ruleId] = counts.GetValueOrDefault(ruleId) + 1;
            }
        }

        // Every rule is listed, including the ones that claimed nothing — a rule with a zero
        // beside it is the single most useful line in a dry run, because it is the one that is
        // not doing what its author thought.
        return [.. rules.Select(r => new RuleHit(r.Id, r.Name, counts.GetValueOrDefault(r.Id)))];
    }

    /// <summary>
    /// Geometry and duplicate hints for the candidates on one page. Split from the scan
    /// because both are expensive per row and neither is needed for the rows nobody is
    /// looking at.
    /// </summary>
    public async Task<IReadOnlyList<CandidateDetail>> HydrateAsync(
        Guid geofileId,
        IReadOnlyList<CandidateSummary> page,
        ImportOptions options,
        AccessContext ctx,
        CancellationToken ct = default)
    {
        if (page.Count == 0)
        {
            return [];
        }

        var ids = page.Select(c => c.SourceId).ToList();
        var geometries = await db.GeofileFeatures.AsNoTracking()
            .Where(f => f.GeofileId == geofileId && ids.Contains(f.Id))
            .Select(f => new { f.Id, f.Geom })
            .ToDictionaryAsync(f => f.Id, f => f.Geom, ct);

        var points = page
            .Select(c => (c.SourceId, Point: geometries.GetValueOrDefault(c.SourceId) as Point))
            .Where(x => x.Point is not null)
            .ToDictionary(x => x.SourceId, x => x.Point!);
        var duplicates = await NearestVisibleAsync(points, options.DuplicateRadiusMeters, page, ctx, ct);

        return
        [
            .. page.Select(c => new CandidateDetail(
                c,
                geometries.GetValueOrDefault(c.SourceId),
                duplicates.GetValueOrDefault(c.SourceId)))
        ];
    }

    // ---------- classification ----------

    private static CandidateSummary Classify(SourceRow row, IReadOnlyList<TermRule> rules, ImportOptions options)
    {
        var properties = ReadProperties(row.Properties);
        var attributes = SourceAttributeReader.Read(properties, options.Mapping);
        var geometry = GeometryOf(row.GeometryType);

        var proposal = TermRuleEvaluator.Evaluate(
            rules, new CandidateText(attributes.Name, attributes.Description), options.Languages);

        var summary = new CandidateSummary
        {
            SourceId = row.Id,
            Geometry = geometry,
            SourceName = attributes.Name,
            SourceDescription = attributes.Description,
            SourceCode = attributes.Code,
            SourceElevation = attributes.Elevation,
            SourceProperties = row.Properties,
        };

        // Line work is never proposed as a cave or a feature by a term: a track called
        // "Peștera Ursilor" is the walk to that cave, not the cave. It becomes a line feature
        // only when the import was told to keep tracks, and then of the type it was told.
        if (geometry != CandidateGeometry.Point)
        {
            return geometry == CandidateGeometry.Line && options.Tracks == ImportTrackHandling.ImportAsLine
                ? summary with
                {
                    ProposedKind = ImportTargetKind.SurfaceFeature,
                    ProposedFeatureTypeCode = options.TrackFeatureTypeCode,
                    ProposedName = ImportNameCleaner.WithPrefix(options.NamePrefix, attributes.Name),
                }
                : summary;
        }

        if (proposal.Winner is not { } winner)
        {
            return summary;
        }

        // Stripping only ever touches the name, and only what the winning rule matched there.
        var stripped = winner.Field == CandidateField.Name
            ? ImportNameCleaner.Apply(attributes.Name, winner.Match, winner.Rule.Strip)
            : attributes.Name;

        return summary with
        {
            RuleId = winner.Rule.Id,
            RuleName = winner.Rule.Name,
            ConflictingRuleNames = [.. proposal.Contenders.Select(c => c.Rule.Name)],
            ProposedKind = winner.Rule.Target,
            ProposedCaveTypeCode = winner.Rule.CaveTypeCode,
            ProposedEntranceTypeCode = winner.Rule.EntranceTypeCode,
            ProposedFeatureTypeCode = winner.Rule.FeatureTypeCode,
            ProposedName = ImportNameCleaner.WithPrefix(options.NamePrefix, stripped),
        };
    }

    private static CandidateGeometry GeometryOf(string postgisType) => postgisType switch
    {
        "ST_Point" or "ST_MultiPoint" => CandidateGeometry.Point,
        "ST_LineString" or "ST_MultiLineString" => CandidateGeometry.Line,
        "ST_Polygon" or "ST_MultiPolygon" => CandidateGeometry.Area,
        _ => CandidateGeometry.Other,
    };

    /// <summary>
    /// The row's attributes as text. Numbers and booleans are stringified rather than typed:
    /// everything downstream compares, folds or displays them, and a source that writes an
    /// elevation as <c>"431"</c> in one row and <c>431</c> in the next is the normal case.
    /// </summary>
    private static Dictionary<string, string?> ReadProperties(string json)
    {
        var properties = new Dictionary<string, string?>(StringComparer.Ordinal);
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return properties;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                properties[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.Null or JsonValueKind.Undefined => null,
                    JsonValueKind.String => property.Value.GetString(),
                    _ => property.Value.ToString(),
                };
            }
        }
        catch (JsonException)
        {
            // A row whose attributes cannot be read still has a geometry and still imports;
            // it simply arrives unnamed, which the review shows.
        }

        return properties;
    }

    // ---------- duplicate detection ----------

    /// <summary>
    /// The nearest existing feature to each candidate. Who may be in the comparison at all is
    /// decided by <see cref="VisibleProximitySearch"/> — it is a location oracle and has one
    /// home — and what is left here is only this pipeline's own answer: the single nearest, with
    /// how alike the two names are.
    /// </summary>
    private async Task<Dictionary<long, DuplicateHint>> NearestVisibleAsync(
        Dictionary<long, Point> points,
        double radiusMeters,
        IReadOnlyList<CandidateSummary> page,
        AccessContext ctx,
        CancellationToken ct)
    {
        var hints = new Dictionary<long, DuplicateHint>();
        var comparable = await proximity.NearAsync(
            points.Values,
            Math.Min(radiusMeters, ImportOptions.MaxDuplicateRadiusMeters),
            ctx,
            ct);
        if (comparable.Count == 0)
        {
            return hints;
        }

        var namesBySource = page.ToDictionary(c => c.SourceId, c => c.ProposedName ?? c.SourceName);
        foreach (var (sourceId, point) in points)
        {
            var candidateName = namesBySource.GetValueOrDefault(sourceId);
            DuplicateHint? best = null;
            foreach (var feature in comparable)
            {
                var distance = Geodesy.DistanceMeters(point.Coordinate, feature.Geom.Coordinate);
                if (distance > radiusMeters || (best is not null && distance >= best.DistanceMeters))
                {
                    continue;
                }

                best = new DuplicateHint(
                    feature.FeatureId,
                    feature.Name,
                    feature.Kind,
                    distance,
                    NameSimilarity.Of(candidateName, feature.Name),
                    feature.CaveFeatureId,
                    feature.CaveName);
            }

            if (best is not null)
            {
                hints[sourceId] = best;
            }
        }

        return hints;
    }
}
