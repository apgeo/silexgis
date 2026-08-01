// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Features;

/// <summary>
/// Typed associations between features (not containment). A locating link on a record
/// with exact coordinates discloses a protected endpoint's position by proximity, so
/// such links are redacted — in both directions — for callers without exact view on
/// the protected endpoint.
/// </summary>
public static class FeatureLinkEndpoints
{
    public static RouteGroupBuilder MapFeatureLinkEndpoints(this RouteGroupBuilder api)
    {
        var features = api.MapGroup("/features").WithTags("Features");

        features.MapGet("/{id:guid}/links", GetLinksAsync)
            .WithSummary("The feature's links, both directions; protected endpoints redacted.");
        features.MapPut("/{id:guid}/links", SetLinksAsync).WithValidation<SetLinksRequest>()
            .WithSummary("Replaces the feature's outgoing links (Write permission).");

        return api;
    }

    private static async Task<Results<Ok<List<FeatureLinkDto>>, ProblemHttpResult>> GetLinksAsync(
        Guid id,
        SilexGisDbContext db,
        IPermissionService permissions,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var feature = await db.Features.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        if (feature is null || !await permissions.CanAsync(user, feature, ObjectPermission.Read, ct))
        {
            return ApiProblems.NotFound("feature.not_found");
        }

        return TypedResults.Ok(await LinksViewAsync(db, protection, user, id, ct));
    }

    private static async Task<Results<Ok<List<FeatureLinkDto>>, UnauthorizedHttpResult, ProblemHttpResult>> SetLinksAsync(
        Guid id,
        SetLinksRequest request,
        HttpContext http,
        SilexGisDbContext db,
        IPermissionService permissions,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var feature = await db.Features.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (feature is null)
        {
            return ApiProblems.NotFound("feature.not_found");
        }

        if (user is null || !await permissions.CanAsync(user, feature, ObjectPermission.Write, ct))
        {
            return await permissions.CanAsync(user, feature, ObjectPermission.Read, ct)
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("feature.not_found");
        }

        if (await Concurrency.CheckIfMatchAsync(http, db, VersionedTable.Features, feature.Id, ct) is { } stale)
        {
            return stale;
        }

        if (request.Links.Any(l => l.ToId == id))
        {
            return ApiProblems.BadRequest("feature.link_self", "A feature cannot link to itself.");
        }

        var codes = request.Links.Select(l => l.LinkKindCode).Distinct(StringComparer.Ordinal).ToArray();
        var kindsByCode = await db.LinkKinds.AsNoTracking()
            .Where(k => codes.Contains(k.Code))
            .ToDictionaryAsync(k => k.Code, StringComparer.Ordinal, ct);
        if (kindsByCode.Count != codes.Length)
        {
            return ApiProblems.BadRequest("feature.link_kind_unknown", "Unknown link kind.");
        }

        if (request.Links
            .GroupBy(l => (l.ToId, kindsByCode[l.LinkKindCode].Id))
            .Any(g => g.Count() > 1))
        {
            return ApiProblems.BadRequest("feature.link_duplicate", "Duplicate link to the same feature and kind.");
        }

        var targetIds = request.Links.Select(l => l.ToId).Distinct().ToArray();
        if (targetIds.Length > 0)
        {
            var visibleTargets = await db.Features.AsNoTracking().VisibleTo(user, db.ObjectAcls)
                .Where(f => targetIds.Contains(f.Id)).Select(f => f.Id).ToListAsync(ct);
            if (visibleTargets.Count != targetIds.Length)
            {
                // A target the caller cannot read is reported exactly like a missing one.
                return ApiProblems.BadRequest("feature.link_target_not_found", "A linked feature does not exist.");
            }
        }

        var stored = await db.FeatureLinks.Where(l => l.FromId == id).ToListAsync(ct);
        var kindsById = await LinkKindsOfAsync(db, stored.Select(l => l.LinkKindId), ct);

        // A caller without exact view was shown a link set with the locating links to
        // protected endpoints omitted; a full replace from that view must not silently
        // sever them. Such stored rows are preserved verbatim and out of reach.
        var endpointIds = stored.Select(l => l.ToId).Concat(targetIds).Append(id).Distinct().ToList();
        var redacted = await protection.RedactedLinkTargetIdsAsync(user, endpointIds, ct);
        bool HiddenFromCaller(FeatureLink link) =>
            kindsById[link.LinkKindId].Locating
            && (redacted.Contains(link.ToId) || redacted.Contains(id));
        var preservedKeys = stored.Where(HiddenFromCaller)
            .Select(l => (l.ToId, l.LinkKindId))
            .ToHashSet();

        var desired = request.Links
            .Select(l => (l.ToId, KindId: kindsByCode[l.LinkKindCode].Id, l.Note))
            .Where(l => !preservedKeys.Contains((l.ToId, l.KindId)))
            .ToDictionary(l => (l.ToId, l.KindId), l => l.Note);

        foreach (var link in stored.Where(l => !HiddenFromCaller(l)))
        {
            if (desired.Remove((link.ToId, link.LinkKindId), out var note))
            {
                link.Note = note;
            }
            else
            {
                db.FeatureLinks.Remove(link);
            }
        }

        foreach (var ((toId, kindId), note) in desired)
        {
            db.FeatureLinks.Add(new FeatureLink { FromId = id, ToId = toId, LinkKindId = kindId, Note = note });
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await LinksViewAsync(db, protection, user, id, ct));
    }

    /// <summary>
    /// The links of a feature, both directions, as the caller may see them: rows whose
    /// other endpoint is unreadable are omitted, and locating rows are omitted when the
    /// caller lacks exact view on either endpoint (the pairing itself places the
    /// protected one).
    /// </summary>
    private static async Task<List<FeatureLinkDto>> LinksViewAsync(
        SilexGisDbContext db, FeatureProtection protection, UserContext? user, Guid id, CancellationToken ct)
    {
        var links = await db.FeatureLinks.AsNoTracking()
            .Where(l => l.FromId == id || l.ToId == id)
            .ToListAsync(ct);
        if (links.Count == 0)
        {
            return [];
        }

        var kindsById = await LinkKindsOfAsync(db, links.Select(l => l.LinkKindId), ct);
        var otherIds = links.Select(l => l.FromId == id ? l.ToId : l.FromId).Distinct().ToArray();
        var readableOthers = (await db.Features.AsNoTracking().VisibleTo(user!, db.ObjectAcls)
            .Where(f => otherIds.Contains(f.Id)).Select(f => f.Id).ToListAsync(ct)).ToHashSet();
        var redacted = await protection.RedactedLinkTargetIdsAsync(user, [.. otherIds, id], ct);

        return links
            .Where(l => readableOthers.Contains(l.FromId == id ? l.ToId : l.FromId))
            .Where(l => !kindsById[l.LinkKindId].Locating
                || (!redacted.Contains(l.FromId) && !redacted.Contains(l.ToId)))
            .OrderBy(l => l.FromId == id ? 0 : 1)
            .ThenBy(l => kindsById[l.LinkKindId].Code, StringComparer.Ordinal)
            .ThenBy(l => l.FromId == id ? l.ToId : l.FromId)
            .Select(l => new FeatureLinkDto(l.FromId, l.ToId, kindsById[l.LinkKindId].Code, l.Note))
            .ToList();
    }

    private static async Task<Dictionary<long, LinkKind>> LinkKindsOfAsync(
        SilexGisDbContext db, IEnumerable<long> kindIds, CancellationToken ct)
    {
        var ids = kindIds.Distinct().ToArray();
        return ids.Length == 0
            ? []
            : await db.LinkKinds.AsNoTracking().Where(k => ids.Contains(k.Id)).ToDictionaryAsync(k => k.Id, ct);
    }
}
