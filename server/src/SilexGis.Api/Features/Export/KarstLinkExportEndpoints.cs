// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Export;
using SilexGis.Infrastructure.Grottocenter;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Export;

/// <summary>
/// What an exporter asked for: which caves, and what to do about the ones whose position
/// this installation protects.
/// </summary>
/// <param name="CaveIds">
/// The caves to export. Empty or absent means every cave the filters below select. Ids the
/// caller may not read are dropped silently rather than refused — answering "no such cave"
/// would tell the asker which ids exist, which is the question the visibility filter has
/// already declined to answer.
/// </param>
/// <param name="Search">
/// Name or toponym fragment, matched the way the cave list matches it — the same clause, so
/// the set on the screen and the set in the file are the same set.
/// </param>
/// <param name="Region">Region fragment, matched the way the cave list matches it.</param>
/// <param name="CaveTypeId">Cave type.</param>
/// <param name="Bbox">Bounding box, as the other exports take one.</param>
/// <param name="Tag">
/// Tag slug, matched the way the cave list matches one. Present because the screen this
/// export is started from filters by tag: an export that ignored it would hand over caves
/// nobody on that screen was looking at, and the count shown before the choice would be about
/// a different set from the file.
/// </param>
/// <param name="Treatments">
/// What to do about one named cave's protected position, keyed by cave id. Wins over the
/// other two.
/// </param>
/// <param name="TreatmentForAll">
/// One answer for every protected cave in the set, so a large export is one decision rather
/// than thousands.
/// </param>
/// <param name="DefaultTreatment">
/// The answer this caller settled on before, so somebody who has already decided is not
/// asked again. Consulted last.
/// </param>
public sealed record KarstLinkExportRequest(
    IReadOnlyList<Guid>? CaveIds = null,
    string? Search = null,
    string? Region = null,
    long? CaveTypeId = null,
    string? Bbox = null,
    string? Tag = null,
    IReadOnlyDictionary<Guid, string>? Treatments = null,
    string? TreatmentForAll = null,
    string? DefaultTreatment = null);

/// <summary>Structural limits only; what a treatment may say is decided elsewhere.</summary>
/// <remarks>
/// A treatment code is deliberately not validated here. Naming something outside the closed
/// vocabulary — including any spelling of "the exact position" — has to come back as its own
/// refusal code with the caves it is about, and a generic validation failure would flatten
/// that into a message nothing can act on.
/// </remarks>
public sealed class KarstLinkExportRequestValidator : AbstractValidator<KarstLinkExportRequest>
{
    public KarstLinkExportRequestValidator()
    {
        RuleFor(r => r.CaveIds!.Count)
            .LessThanOrEqualTo(KarstLinkExportEndpoints.MaxExportedCaves)
            .When(r => r.CaveIds is not null);
        RuleFor(r => r.Treatments!.Count)
            .LessThanOrEqualTo(KarstLinkExportEndpoints.MaxExportedCaves)
            .When(r => r.Treatments is not null);
        RuleFor(r => r.Search).MaximumLength(200);
        RuleFor(r => r.Region).MaximumLength(200);
        RuleFor(r => r.Bbox).MaximumLength(200);
        RuleFor(r => r.Tag).MaximumLength(200);
    }
}

/// <summary>
/// How large the decision is, before it is made. What an exporter needs to know is not how
/// many caves the request selects but how many of them the choice actually changes: three
/// and two thousand nine hundred are different decisions, and only the server can tell them
/// apart, because whether a cave's position is protected is a fact about the cave that the
/// client is never shown and could not be trusted to compute.
/// </summary>
/// <param name="CaveCount">Caves the request selects that this caller may read.</param>
/// <param name="ProtectedCaveCount">
/// Of those, how many have a position this installation protects — exactly the caves a
/// treatment has to be chosen for. It counts the caves, not the caller's rights: somebody who
/// may see every exact position in the registry still has to say what the file should carry,
/// because the file will outlive that right. Null when the request selects more caves than one
/// file may hold, because the answer is not worth computing for a request that will be refused
/// anyway.
/// </param>
/// <param name="ExceedsLimit">Whether the request selects more caves than one file may hold.</param>
/// <param name="MaxCaveCount">How many caves one file may hold.</param>
public sealed record KarstLinkExportPreview(
    int CaveCount,
    int? ProtectedCaveCount,
    bool ExceedsLimit,
    int MaxCaveCount);

/// <summary>
/// Caves described in the published cave and karst vocabulary, as a file somebody can hand
/// to somebody else.
/// </summary>
/// <remarks>
/// <para>
/// This is a fourth export beside the vector ones and is gated exactly as they are — signed
/// in, narrowed to what the caller may read, and never anonymous. What it adds is that the
/// exporter decides, per cave, what happens to a position this installation protects: the
/// cave goes in with no coordinates, or it is left out, or it is placed on the same coarse
/// grid the map already publishes. The surveyed position is not among the choices and there
/// is no request that reaches it, because the vocabulary of choices has no member naming it.
/// </para>
/// <para>
/// It is a POST although it reads nothing: a decision per cave does not fit in a query
/// string, and an export of thousands of caves with an answer for each would not survive a
/// URL length limit. Nothing is written by it except the record that it happened.
/// </para>
/// </remarks>
public static class KarstLinkExportEndpoints
{
    /// <summary>
    /// How many caves one file holds. The whole document is built in memory before a byte of
    /// it is sent, so this is a real bound rather than a policy — and the request is refused
    /// rather than quietly truncated, because a file that stopped early and a file that is
    /// complete look identical to whoever receives it.
    /// </summary>
    public const int MaxExportedCaves = 5000;

    /// <summary>The request selects more caves than one file may hold.</summary>
    public const string TooManyCavesCode = "export.too_many_caves";

    public static RouteGroupBuilder MapKarstLinkExportEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/export/caves/karstlink", ExportAsync)
            .WithValidation<KarstLinkExportRequest>()
            .WithTags("Export")
            .WithSummary(
                "Caves as JSON-LD in the published cave and karst vocabulary. The exporter "
                + "chooses, per cave, what happens to a protected position: no coordinates, "
                + "omitted, or the protection-grid position.");
        api.MapPost("/export/caves/karstlink/preview", PreviewAsync)
            .WithValidation<KarstLinkExportRequest>()
            .WithTags("Export")
            .WithSummary(
                "How many caves a KarstLink export would hold, and how many of them need a "
                + "decision about a protected position.");
        return api;
    }

    /// <summary>Flat row: everything the document may say about one cave, plus its geometry.</summary>
    private sealed record Row(
        Guid Id,
        string? Name,
        Geometry? Geom,
        bool LocationProtected,
        string? IdentificationCode,
        string? OtherToponyms,
        string? Region,
        decimal? SurveyedLength,
        decimal? Depth,
        decimal? Altitude);

    /// <summary>
    /// The caves a request selects, narrowed to what this caller may read.
    /// </summary>
    /// <remarks>
    /// Written once and used by both the export and the count that precedes it. A preview
    /// that selected a different set from the export it previews would report a decision of
    /// the wrong size, and the exporter would be answering about caves that are not the ones
    /// going into the file.
    /// </remarks>
    private static IQueryable<Feature> Selected(
        SilexGisDbContext db,
        AccessContext ctx,
        KarstLinkExportRequest request)
    {
        // Read gate first and on its own. Which caves the caller may read is a different
        // question from which positions they may place, and it is settled before the second
        // one is asked: a cave that fails here is not in the file under any treatment, is not
        // counted among the omitted, and is not named by any refusal.
        var query = db.Features.AsNoTracking()
            .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .Where(f => f.Kind == FeatureKind.Cave && f.Cave != null);

        // Deliberately no "has a geometry" clause, unlike the vector exports: a cave included
        // with no position at all is a record this format can carry, and filtering on geometry
        // here would drop exactly the caves that treatment exists for.
        if (request.CaveIds is { Count: > 0 })
        {
            var wanted = request.CaveIds.Distinct().ToArray();
            query = query.Where(f => wanted.Contains(f.Id));
        }

        if (!string.IsNullOrWhiteSpace(request.Tag))
        {
            var tag = request.Tag;
            query = query.Where(f => db.Taggings.Any(tg =>
                tg.FeatureId == f.Id && db.Tags.Any(t => t.Id == tg.TagId && t.Slug == tag)));
        }

        if (request.CaveTypeId is not null)
        {
            query = query.Where(f => f.Cave!.CaveTypeId == request.CaveTypeId);
        }

        if (!string.IsNullOrWhiteSpace(request.Region))
        {
            var region = $"%{request.Region}%";
            query = query.Where(f => f.Cave!.Region != null && EF.Functions.ILike(f.Cave!.Region, region));
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            // Name or toponym, because that is what the box on the cave list matches and this
            // export is started from that list with its text carried across. A narrower clause
            // here would quietly drop caves the person could see on the screen while the file
            // still declared that nothing had been left out of it — true statements adding up
            // to a false impression, which is the one thing this format is built not to do.
            var pattern = $"%{request.Search}%";
            query = query.Where(f =>
                (f.Name != null
                    && EF.Functions.ILike(EF.Functions.Unaccent(f.Name), EF.Functions.Unaccent(pattern)))
                || (f.Cave!.OtherToponyms != null
                    && EF.Functions.ILike(
                        EF.Functions.Unaccent(f.Cave!.OtherToponyms), EF.Functions.Unaccent(pattern))));
        }

        if (Bbox.TryParse(request.Bbox, out var box))
        {
            var polygon = box.ToPolygon();
            query = query.Where(f => f.Geom!.Intersects(polygon));
        }

        return query;
    }

    /// <summary>
    /// Answers how big the decision is without making it.
    /// </summary>
    /// <remarks>
    /// It reads and writes nothing, and deliberately takes no treatments: it exists so the
    /// person about to choose can see how many caves the choice moves. That number is the
    /// count of caves in scope whose position this installation protects — a fact about the
    /// caves rather than about the caller, so the question is asked of an owner and of an
    /// administrator exactly as it is asked of anyone else.
    /// </remarks>
    private static async Task<Results<Ok<KarstLinkExportPreview>, UnauthorizedHttpResult>> PreviewAsync(
        KarstLinkExportRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var query = Selected(db, ctx, request);
        var flags = await query
            .Select(f => f.IsProtectedEffective)
            .Take(MaxExportedCaves + 1)
            .ToListAsync(ct);

        if (flags.Count > MaxExportedCaves)
        {
            // The full count is worth one more cheap query — "more than you may export" is a
            // less useful thing to be told than by how much.
            var total = await query.CountAsync(ct);
            return TypedResults.Ok(new KarstLinkExportPreview(total, null, true, MaxExportedCaves));
        }

        var protectedCount = flags.Count(isProtected => isProtected);
        return TypedResults.Ok(
            new KarstLinkExportPreview(flags.Count, protectedCount, false, MaxExportedCaves));
    }

    private static async Task<Results<FileContentHttpResult, UnauthorizedHttpResult, ProblemHttpResult>> ExportAsync(
        KarstLinkExportRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        ICurrentUser currentUser,
        IOptions<AccessOptions> access,
        GrottocenterClient grottocenter,
        TimeProvider clock,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // Read gate first and on its own. Which caves the caller may read is a different
        // question from which positions they may place, and it is settled before the second
        // one is asked: a cave that fails here is not in the file under any treatment, is not
        // counted among the omitted, and is not named by any refusal.
        var query = Selected(db, ctx, request);

        var rows = await query
            .OrderBy(f => f.Name)
            .Select(f => new Row(
                f.Id,
                f.Name,
                f.Geom,
                f.IsProtectedEffective,
                f.Cave!.IdentificationCode,
                f.Cave.OtherToponyms,
                f.Cave.Region,
                f.Cave.SurveyedLength,
                f.Cave.Depth,
                f.Cave.Altitude))
            .Take(MaxExportedCaves + 1)
            .ToListAsync(ct);

        if (rows.Count > MaxExportedCaves)
        {
            return ApiProblems.BadRequest(
                TooManyCavesCode,
                $"This request selects more than {MaxExportedCaves} caves. Narrow it and export "
                + "in parts; a file that silently stopped at a limit would disagree with the "
                + "registry for a reason nothing in it explains.",
                "limit",
                MaxExportedCaves);
        }

        var gridMeters = access.Value.LocationGridMeters;
        var ids = rows.Select(r => r.Id).ToArray();

        // What has to be decided is which caves this installation protects, not which ones
        // this caller happens to be allowed to place exactly. Those are different sets and
        // the difference is the whole point of the route: an owner and a full administrator
        // may read every surveyed coordinate here, but the file they take away answers to
        // nobody afterwards, so a protected cave gets a treatment or the export is refused.
        var protectedIds = rows.Where(r => r.LocationProtected).Select(r => r.Id).ToHashSet();

        // The same cave in a register outside this installation, so two files about the same
        // caves can be joined at all — this file's own identifiers mean nothing anywhere else.
        // Read only for the caves whose position is not protected: an entry in somebody else's
        // register publishes coordinates, so a link to it hands over by reference exactly the
        // position a treatment was chosen to withhold.
        var openIds = ids.Where(id => !protectedIds.Contains(id)).ToArray();
        var sameAs = await db.FeatureExternalIds.AsNoTracking()
            .Where(x => openIds.Contains(x.FeatureId) && x.System == ExternalIdSystem.Grottocenter)
            .ToDictionaryAsync(x => x.FeatureId, x => x.Value, ct);

        var result = CaveExportPlanner.Resolve(
            ids,
            protectedIds,
            new CaveExportChoices(
                request.Treatments,
                request.TreatmentForAll,
                request.DefaultTreatment));

        if (result.Refusal is { } refusal)
        {
            return Refused(refusal);
        }

        var plan = result.Plan!;
        var caves = new List<KarstLinkCave>(rows.Count);
        foreach (var row in rows)
        {
            var position = plan.Positions[row.Id];
            if (position == CaveExportPosition.Omitted)
            {
                continue;
            }

            double? lat = null;
            double? lon = null;
            double? precision = null;

            if (row.Geom is Point point)
            {
                switch (position)
                {
                    case CaveExportPosition.Exact:
                        lat = point.Y;
                        lon = point.X;
                        break;
                    case CaveExportPosition.Grid:
                        // The one place a treatment turns into a coordinate, called rather
                        // than repeated. A second rounding written next to an exporter is how
                        // this file and the map come to disagree about how far a position was
                        // moved, and the difference between two obfuscations of the same point
                        // narrows down where the point is.
                        var snapped = ProtectedPositionTreatments.Position(
                            ProtectedPositionTreatment.GridPosition, point, gridMeters)!;
                        lat = snapped.Y;
                        lon = snapped.X;
                        precision = gridMeters;
                        break;
                }
            }

            // A cave whose geometry is not a plain point cannot be moved onto a grid without
            // disclosing its outline, and one with no geometry has nothing to place. Either
            // way the record goes in without a position rather than with a degraded one: what
            // the exporter chose is still recorded on the cave, so the file does not claim a
            // position was never protected.

            plan.Treatments.TryGetValue(row.Id, out var treatment);
            caves.Add(new KarstLinkCave(
                row.Id,
                row.Name,
                row.IdentificationCode,
                row.OtherToponyms,
                row.Region,
                lat,
                lon,
                row.Altitude is null ? null : (double)row.Altitude,
                row.SurveyedLength,
                row.Depth,
                precision,
                plan.Treatments.ContainsKey(row.Id) ? treatment : null,
                sameAs.TryGetValue(row.Id, out var externalId) ? grottocenter.EntryUrl(externalId) : null));
        }

        var exportId = Guid.CreateVersion7();
        var now = clock.GetUtcNow();

        // Recorded before the bytes are handed over, in the same request, so there is no
        // window in which a file exists and the record of who took it does not.
        db.Set<AuditEntry>().Add(new AuditEntry
        {
            UserId = currentUser.UserId,
            Action = AuditActions.Exported,
            EntityType = "CaveExport",
            EntityId = exportId.ToString(),
            Changes = JsonSerializer.Serialize(AuditDetail(plan, caves.Count)),
        });
        await db.SaveChangesAsync(ct);

        var bytes = KarstLinkDocument.Write(caves, plan.Omitted.Count, gridMeters, now, exportId);
        var fileName = $"caves-karstlink-{now:yyyyMMdd}.{KarstLinkDocument.FileExtension}";
        return TypedResults.File(bytes, KarstLinkDocument.ContentType, fileName);
    }

    /// <summary>
    /// What the audit row carries. Grouped by treatment rather than one entry per cave: the
    /// question asked afterwards is "which caves did this export hide, and how", and a map
    /// from cave to answer answers it just as well at a thousandth of the size.
    /// </summary>
    private static Dictionary<string, object?> AuditDetail(CaveExportPlan plan, int caveCount)
    {
        var byTreatment = plan.Treatments
            .GroupBy(pair => ProtectedPositionTreatments.Code(pair.Value))
            .ToDictionary(
                group => group.Key,
                group => (object?)group.Select(pair => pair.Key.ToString()).OrderBy(id => id).ToList());

        return new Dictionary<string, object?>
        {
            ["format"] = "karstlink-jsonld",
            ["caveCount"] = caveCount,
            ["omittedCaveCount"] = plan.Omitted.Count,
            ["protectedPositionTreatments"] = byTreatment,
        };
    }

    private static ProblemHttpResult Refused(CaveExportRefusal refusal)
    {
        var detail = refusal.Code == CaveExportPlanner.TreatmentUnknownCode
            ? $"'{refusal.NamedTreatment}' is not something an export may do with a protected "
              + $"position. Choose one of: {string.Join(", ", ProtectedPositionTreatments.Codes)}."
            : "Some caves in this request have a position this installation protects and nothing "
              + "was chosen for them. An export decides that deliberately or not at all.";

        return ApiProblems.BadRequest(
            refusal.Code,
            detail,
            "caveIds",
            refusal.CaveIds.Select(id => id.ToString()).ToList());
    }
}
