// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Infrastructure.Documents;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Npgsql;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.ResLinks;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.RasterMaps;

namespace SilexGis.Api.Features.ResLinks;

public static partial class ResLinkEndpoints
{
    /// <summary>
    /// The panel query: links having a member that targets the named entity, each with
    /// all members resolved for display. Requires Read on the target — the panel sits on
    /// the target's page, so the target's own rules gate it.
    /// </summary>
    /// <param name="relation">
    /// Optional relation code, narrowing the answer to links of that one relation — how a
    /// page asks for a single role ("what did this trip survey?") without reading every
    /// link and sorting them itself. Addressed by code rather than by row id because the
    /// code is the shared vocabulary clients already translate their labels by, while the
    /// numeric id is assigned per installation. Blank or absent means every relation,
    /// untyped links included; a code no relation type carries is refused rather than
    /// answered with an empty page, so a mistyped role does not read as a role nobody used.
    /// The answer is a page of links, not one link. Somebody who may write the target a role
    /// is about amends the existing link rather than opening a rival one — but recording a
    /// role never took more than being able to read what it names, so a caller who may read
    /// the target without writing it can only add a second link of the same role, and what
    /// the role names is the union of their members. A caller that renders a role must read
    /// every link the filter returns — taking the first would hide the other author's work.
    /// </param>
    private static async Task<Results<Ok<PagedResult<ResLinkDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ForTargetAsync(
        string type,
        Guid id,
        SilexGisDbContext db,
        ResLinkTargetDirectory targets,
        AssociationDisclosure associations,
        FeatureProtection protection,
        PhotoPositionDisclosure photoPositions,
        PlacePointDisclosure placePoints,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct,
        int? page = null,
        int? pageSize = null,
        string? relation = null)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!ResLinkTargets.TryParse(type, out var parsedType))
        {
            return ApiProblems.BadRequest(EntityTypeUnknownCode, $"Unknown target type '{type}'.");
        }

        if (!await targets.CanReadAsync(ctx, parsedType, id, ct))
        {
            // The target is invisible to the caller — so are its links.
            return ApiProblems.NotFound(TargetNotFoundCode);
        }

        long? relationTypeId = null;
        if (!string.IsNullOrWhiteSpace(relation))
        {
            var relationCode = relation.Trim();
            relationTypeId = await db.ResLinkRelationTypes.AsNoTracking()
                .Where(r => r.Code == relationCode)
                .Select(r => (long?)r.Id)
                .FirstOrDefaultAsync(ct);
            if (relationTypeId is null)
            {
                return ApiProblems.BadRequest(
                    RelationNotFoundCode, $"No relation type carries the code '{relationCode}'.");
            }
        }

        var (p, size) = Paging.Normalize(page, pageSize);

        var incident = parsedType is { } pairType
            ? db.ResLinkMembers.AsNoTracking().Where(m => m.EntityType == pairType && m.EntityId == id)
            : db.ResLinkMembers.AsNoTracking().Where(m => m.FeatureId == id);
        var incidentLinks = Standing(db).AsNoTracking()
            .Where(l => incident.Any(m => m.ResLinkId == l.Id));

        // The relation filter narrows the one query both paging arms below are built from,
        // and it has to: one arm counts rows in the database while the other counts what
        // survived the disclosure cut, so a filter reaching only one of them would report a
        // total that disagrees with the rows returned — and that total is the badge.
        if (relationTypeId is { } roleId)
        {
            incidentLinks = incidentLinks.Where(l => l.RelationTypeId == roleId);
        }

        // Places pinned on phones' maps are shown whole or not at all, and a hidden one is not
        // counted either: cut from the query both arms below order, page and count, for the reason
        // the relation filter is.
        var hiddenPins = await placePoints.HiddenLinkIdsAsync(ctx, incidentLinks, ct);
        if (hiddenPins.Count > 0)
        {
            incidentLinks = incidentLinks.Where(l => !hiddenPins.Contains(l.Id));
        }

        var linkQuery = incidentLinks
            .OrderByDescending(l => l.CreatedAt)
            .ThenBy(l => l.Id);

        // Every link this panel could list is connected to the queried target by a
        // member naming it, and for a feature target that connection is itself an
        // association under the disclosure rule. A feature the caller may place exactly
        // is never withheld, so its panel pages ordinarily below. Otherwise the rule's
        // uniform arm is asked first: when the association is withheld for this caller
        // and feature, no link has a connection that may be shown, so the panel is
        // empty — listing even one row (or a non-zero total, which doubles as the badge
        // count) would disclose that links about this feature exist.
        if (parsedType is null
            && !(await protection.ExactViewIdsAsync(ctx, [id], ct)).Contains(id))
        {
            if (await associations.IsWithheldAsync(ctx, ResLinkRules.MemberAssociation(id), ct))
            {
                return TypedResults.Ok(new PagedResult<ResLinkDto>([], p, size, 0));
            }

            // The installation reveals protected associations, so each link now decides
            // for itself: a sibling member showing this caller exact coordinates
            // re-withholds the member naming this feature, and with it the link's
            // presence here — a row on this feature's page states the association the
            // dropped member no longer may. The projection already makes that per-link
            // decision, so the panel projects every link the query above admits and lists
            // exactly those still connected to the feature, paging in memory to keep the
            // total (and badge) honest. Links a feature accrues are bounded in practice;
            // the exact-view fast path above keeps unprotected features off this path.
            // Nothing bounds them in principle, though, and this arm's cost grows with the
            // number of candidates rather than the page size — narrowed by a relation
            // filter when one is asked for, and by nothing otherwise: the accepted price
            // of an honest badge. It cannot be capped without changing what the total
            // means — the total is the count of candidates that survived the cut, so a cap
            // would make the badge say "at least this many" while the panel still claims a
            // number. If a heavily-linked protected feature ever appears, the way out is
            // the cheaper first pass: answer the sibling-exposure question without
            // resolving displays or assembling members, then project only the page. What
            // must not happen meanwhile is a new per-row decision multiplying this set,
            // which is why the curation answer below is taken after the slice. What it
            // costs today is measured rather than assumed: at 150 candidate links on a
            // page of twenty this answers in tens of milliseconds, and a test holds it to
            // a budget so that a change turning the arm quadratic fails there instead of
            // on somebody's cave page.
            var candidates = await linkQuery.ToListAsync(ct);
            var projected = await ProjectAsync(
                db, targets, associations, protection, photoPositions, access, ctx, candidates, ct,
                skipCuration: true);
            var listed = projected
                .Where(l => l.Members.Any(m =>
                    m.TargetType == ResLinkTargets.FeatureName && m.TargetId == id))
                .ToList();

            // Curation is decided after the slice, never with the projection above: which
            // links survive to be listed is only known once every candidate has been
            // projected, but who may curate them is only ever asked about the rows actually
            // returned. So this arm's per-row cost is the page's, not the candidate set's,
            // and the honest total stays the count of what survived.
            var rows = listed.Skip((p - 1) * size).Take(size).ToList();
            var pageIds = rows.Select(l => l.Id).ToHashSet();
            List<ResLinkMember> pageMains = pageIds.Count == 0
                ? []
                : await db.ResLinkMembers.AsNoTracking()
                    .Where(m => pageIds.Contains(m.ResLinkId) && m.IsMain)
                    .ToListAsync(ct);
            var curated = await CuratedLinkIdsAsync(
                db, access, targets, ctx, [.. candidates.Where(l => pageIds.Contains(l.Id))], pageMains, ct);
            return TypedResults.Ok(new PagedResult<ResLinkDto>(
                [.. rows.Select(l => l with { MayEdit = curated.Contains(l.Id) })],
                p,
                size,
                listed.Count));
        }

        var total = await linkQuery.CountAsync(ct);
        var links = await linkQuery.Skip((p - 1) * size).Take(size).ToListAsync(ct);
        var dtos = await ProjectAsync(
            db, targets, associations, protection, photoPositions, access, ctx, links, ct);
        return TypedResults.Ok(new PagedResult<ResLinkDto>(dtos, p, size, total));
    }

    private static async Task<Results<Ok<List<ResLinkTargetHitDto>>, UnauthorizedHttpResult, ProblemHttpResult>> SearchTargetsAsync(
        string type,
        SilexGisDbContext db,
        ResLinkTargetDirectory targets,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct,
        string? q = null,
        int? limit = null)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!ResLinkTargets.TryParse(type, out var parsedType))
        {
            return ApiProblems.BadRequest(EntityTypeUnknownCode, $"Unknown target type '{type}'.");
        }

        // A kind the filter model serves is refused rather than answered empty, so a caller still
        // asking here learns where the question moved instead of concluding that nothing matches.
        if (targets.SearchOf(parsedType) is not { } search)
        {
            return ApiProblems.BadRequest(
                TargetSearchedByFilterCode,
                $"Targets of type '{type}' are found through the filter query, not this feed.");
        }

        if (string.IsNullOrWhiteSpace(q))
        {
            return TypedResults.Ok(new List<ResLinkTargetHitDto>());
        }

        var hits = await search.SearchAsync(ctx, q.Trim(), Math.Clamp(limit ?? 20, 1, 50), ct);
        return TypedResults.Ok(hits.ToList());
    }

    /// <summary>
    /// What a new GPS point's audience resolves to for this caller when they state no
    /// visibility of their own — the same rule the member-add path applies, answered
    /// before the point exists so a form can name the audience rather than recite the
    /// rule. The group's name travels with its id because a notice that says "your
    /// group" and a reader who belongs to one they forgot about are not the same thing.
    /// </summary>
    /// <remarks>
    /// Says nothing the caller does not already know: it reports only their own
    /// membership, and only when it is the single one that decides the default. A group
    /// they belong to is one they can already list from their own profile.
    /// </remarks>
    private static async Task<Results<Ok<ResLinkPointDefaultDto>, UnauthorizedHttpResult>> PointDefaultAsync(
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var (visibility, groupId) = ResLinkRules.DefaultPointAudience(ctx.CavingGroupIds);
        if (groupId is null)
        {
            return TypedResults.Ok(new ResLinkPointDefaultDto(visibility, null, null));
        }

        var name = await db.CavingGroups.AsNoTracking()
            .Where(group => group.Id == groupId.Value)
            .Select(group => group.Name)
            .FirstOrDefaultAsync(ct);
        return TypedResults.Ok(new ResLinkPointDefaultDto(visibility, groupId, name));
    }
}
