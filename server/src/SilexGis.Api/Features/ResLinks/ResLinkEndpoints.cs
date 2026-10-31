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

/// <summary>
/// Resource links: n-ary associations with identity between features, documents, cavers,
/// survey models and the rest — each participation anchored to the whole target or a
/// declared part of it.
/// </summary>
/// <remarks>
/// <para>
/// A link never grants access. Any signed-in caller may read any link — its short code is
/// an address, not a capability — and every member answers under its own target's rules:
/// an unreadable target's member still appears (the association itself is not a secret),
/// but with no display data, no anchor payload and no route. Withholding the member row
/// entirely would make links quietly incomplete for different readers, which is worse
/// than admitting something restricted sits there.
/// </para>
/// <para>
/// One kind of member is withheld outright instead of bared: a member naming a feature
/// whose exact position the caller may not see. There a bare row would not admit that
/// "something restricted" exists — it would name the feature, and which links a guarded
/// feature participates in is exactly the association the attachment world keeps from
/// callers without exact view. The same disclosure rule decides here, installation
/// setting included: such a member is absent from every link read, and the panel on
/// such a feature lists no links at all, since each one it named would disclose the
/// association by existing. One thing the reveal setting never opens: when a sibling
/// member of the same link shows the caller exact coordinates — a feature they may
/// place, a geotagged file whose capture point they may see and reach, a survey model
/// they may open, a waypoint anchor into a geofile they may read — the protected
/// feature's name would stand beside a position, so its member stays withheld
/// regardless. The write acknowledgments stay exempt — the member-write echoes and the
/// create response alike answer the caller with the very memberships that caller just
/// asserted, which cannot be news to them.
/// </para>
/// <para>
/// Authoring is asymmetric on purpose: adding a member takes Read on its target — the
/// author asserts about what they can see, and the link shows that member to others only
/// if they can read it too — while editing or deleting a link belongs to its creator,
/// to global administrators, and to whoever may write the link's main member. That last
/// arm is what keeps a link curated by the people who steward the thing it is about
/// rather than by whoever happened to type it first; a link with no main member has no
/// such subject, so there the creator rule stands alone.
/// </para>
/// </remarks>
public static partial class ResLinkEndpoints
{
    public const string NotFoundCode = "reslink.not_found";
    public const string CodeUnresolvedCode = "reslink.code.unresolved";
    public const string EntityTypeUnknownCode = "reslink.entity_type_unknown";
    public const string TargetSearchedByFilterCode = "reslink.target_search.use_filter";
    public const string TargetNotFoundCode = "reslink.target_not_found";
    public const string RelationNotFoundCode = "reslink.relation.not_found";
    public const string MemberNotFoundCode = "reslink.member.not_found";
    public const string MemberTargetNotFoundCode = "reslink.member.target_not_found";
    public const string DuplicateWholeCode = "reslink.member.duplicate_whole";
    public const string AnchorFileInvalidCode = ResLinkRules.AnchorFileInvalidCode;
    public const string GeoPointUnavailableCode = "reslink.member.geo_point_unavailable";

    /// <summary>
    /// A place pinned on a phone's map is made, moved and removed only through its own routes, and
    /// never edited or made through these.
    /// </summary>
    public const string PlacePointFixedCode = "reslink.place_point.fixed";

    private const string PlacePointFixedDetail =
        "A place pinned on a phone's map is moved or removed from its map, and is not edited as a link.";

    /// <summary>The seeded feature kind a GPS-point member is born as — an ordinary
    /// generic feature, editable and protectable like any other from its first moment.</summary>
    private const string GeoPointFeatureTypeCode = "generic";

    public static RouteGroupBuilder MapResLinkEndpoints(this RouteGroupBuilder api)
    {
        var links = api.MapGroup("/reslinks").WithTags("ResLinks");

        links.MapGet("/for-target", ForTargetAsync)
            .WithSummary("Links incident to one target, sibling members resolved, optionally narrowed to one relation code; the total doubles as the badge count.");
        links.MapGet("/targets/search", SearchTargetsAsync)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithSummary("Picker feed for cavers, caving groups and camps: readable targets of one type matching a query, as uniform rows. Every other kind is found through the filter query.");
        links.MapGet("/point-default", PointDefaultAsync)
            .WithSummary("The audience a new GPS point takes when the request names none, for this caller.");
        links.MapPost("/", CreateAsync).WithValidation<ResLinkCreateRequest>()
            .WithSummary("Creates a link with its initial members (at least one).");
        links.MapGet("/{idOrCode}", GetAsync)
            .WithSummary("One link by id or by short code — told apart by shape.");
        links.MapPatch("/{id:guid}", UpdateAsync).WithValidation<ResLinkUpdateRequest>()
            .WithSummary("Sets description and relation type (creator, admin, or a writer of the main member); mainMemberId moves the marker with them.");
        links.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Hard-deletes a link and its members, audited (creator, admin, or a writer of the main member); a place pinned on a phone's map is removed for the phones instead, by whoever may change its cave.");
        links.MapPost("/{id:guid}/members", AddMemberAsync).WithValidation<ResLinkMemberAddRequest>()
            .WithSummary("Adds a member — a readable target, or a new GPS point via newGeoPoint (creator, admin, or a writer of the main member).");
        links.MapPatch("/{id:guid}/members/{memberId:guid}", UpdateMemberAsync)
            .WithValidation<ResLinkMemberUpdateRequest>()
            .WithSummary("Edits isMain/sortOrder/note; anchors are replace-only — delete and re-add.");
        links.MapDelete("/{id:guid}/members/{memberId:guid}", DeleteMemberAsync)
            .WithSummary("Removes a member; the last member is refused — delete the link instead.");
        links.MapPlacePointEndpoints();

        return api;
    }

    private static async Task<Results<Created<ResLinkDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        ResLinkCreateRequest request,
        SilexGisDbContext db,
        ResLinkTargetDirectory targets,
        AssociationDisclosure associations,
        FeatureProtection protection,
        PhotoPositionDisclosure photoPositions,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var relation = await RelationAsync(db, request.RelationTypeId, ct);
        if (request.RelationTypeId is not null && relation is null)
        {
            return ApiProblems.BadRequest(RelationNotFoundCode, "The relation type does not exist.");
        }

        if (ResLinkRules.IsPlacePoint(relation?.Code))
        {
            return ApiProblems.BadRequest(PlacePointFixedCode, PlacePointFixedDetail);
        }

        var members = new List<ResLinkMember>();
        var wholeTargets = new HashSet<(AttachedEntityType? Type, Guid Id)>();
        foreach (var input in request.Members)
        {
            if (!ResLinkTargets.TryParse(input.TargetType, out var type))
            {
                return ApiProblems.BadRequest(
                    ResLinkRules.TypeNotLinkableCode, $"'{input.TargetType}' is not a linkable target type.");
            }

            var featureId = type is null ? input.TargetId : (Guid?)null;
            var entityId = type is null ? (Guid?)null : input.TargetId;
            var anchor = RawAnchor(input.Anchor);
            var shape = new ResLinkRules.MemberShape(
                featureId, type, entityId, input.AnchorKind, anchor, input.AnchorFileId);
            if (MemberProblem(shape) is { } problem)
            {
                return problem;
            }

            if (input.AnchorKind == AnchorKind.Whole && !wholeTargets.Add((type, input.TargetId)))
            {
                return ApiProblems.BadRequest(
                    DuplicateWholeCode, "The same whole target appears more than once.");
            }

            members.Add(new ResLinkMember
            {
                FeatureId = featureId,
                EntityType = type,
                EntityId = entityId,
                IsMain = input.IsMain,
                AnchorKind = input.AnchorKind,
                Anchor = anchor,
                AnchorFileId = input.AnchorFileId,
                SortOrder = input.SortOrder,
                Note = input.Note,
                AddedBy = ctx.UserId,
            });
        }

        var mains = members.Count(m => m.IsMain);
        if (ResLinkRules.MainMarkerProblem(relation, members.Count, mains) is { } mainProblem)
        {
            return MainProblem(mainProblem);
        }

        // The authoring floor: Read on every named target, asked through each target
        // world's own rules — a missing target and an unreadable one refuse identically.
        foreach (var group in members.GroupBy(m => m.EntityType))
        {
            var ids = group.Select(m => m.FeatureId ?? m.EntityId!.Value).Distinct().ToList();
            var readable = await targets.Of(group.Key).ResolveAsync(ctx, ids, ct);
            if (ids.Any(id => !readable.ContainsKey(id)))
            {
                return ApiProblems.NotFound(MemberTargetNotFoundCode);
            }
        }

        // After the floor, so a trip the caller may not read was already answered as missing.
        if (await TripMomentProblemAsync(targets, ctx, relation, members.FirstOrDefault(m => m.IsMain), ct)
            is { } momentProblem)
        {
            return momentProblem;
        }

        // Pin provenance only after the floor: whether a stored file belongs to a
        // document is a fact about the document, so the answer's shape must never
        // differ for a caller who may not read it.
        foreach (var member in members)
        {
            if (await AnchorFilePinProblemAsync(db, member.EntityId, member.AnchorFileId, ct) is { } pinProblem)
            {
                return pinProblem;
            }
        }

        var link = new ResLink
        {
            ShortCode = ResLinkRules.NewShortCode(),
            RelationTypeId = relation?.Id,
            Description = request.Description,
            CreatedBy = ctx.UserId,
        };
        foreach (var member in members)
        {
            member.ResLinkId = link.Id;
        }

        db.ResLinks.Add(link);
        db.ResLinkMembers.AddRange(members);
        await SaveWithFreshShortCodeAsync(db, link, ct);

        // Echoed without the membership-disclosure cut, like the member-write
        // acknowledgments: every member of this response was asserted by this caller in
        // this very request, so none can be news to them — while a cut echo of a
        // single-member link would read as a failed write.
        var dto = (await ProjectAsync(
            db, targets, associations, protection, photoPositions, access, ctx, [link], ct,
            skipMembershipCut: true)).Single();
        return TypedResults.Created($"/api/v1/reslinks/{link.Id}", dto);
    }

    private static async Task<Results<Ok<ResLinkDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        string idOrCode,
        SilexGisDbContext db,
        ResLinkTargetDirectory targets,
        AssociationDisclosure associations,
        FeatureProtection protection,
        PhotoPositionDisclosure photoPositions,
        PlacePointDisclosure placePoints,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // A Guid is 36 characters with dashes and a short code is 8 alphanumerics, so the
        // shapes can never collide; anything of neither shape addresses nothing.
        if (Guid.TryParse(idOrCode, out var id))
        {
            var byId = await Standing(db).AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);
            return byId is null || await placePoints.IsHiddenAsync(ctx, byId.Id, ct)
                ? ApiProblems.NotFound(NotFoundCode)
                : TypedResults.Ok((await ProjectAsync(
                    db, targets, associations, protection, photoPositions, access, ctx, [byId], ct)).Single());
        }

        var byCode = ResLinkRules.IsShortCode(idOrCode)
            ? await Standing(db).AsNoTracking().FirstOrDefaultAsync(l => l.ShortCode == idOrCode, ct)
            : null;
        return byCode is null || await placePoints.IsHiddenAsync(ctx, byCode.Id, ct)
            ? ApiProblems.NotFound(CodeUnresolvedCode)
            : TypedResults.Ok((await ProjectAsync(
                db, targets, associations, protection, photoPositions, access, ctx, [byCode], ct)).Single());
    }

    private static async Task<Results<Ok<ResLinkDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        ResLinkUpdateRequest request,
        SilexGisDbContext db,
        ResLinkTargetDirectory targets,
        AssociationDisclosure associations,
        FeatureProtection protection,
        PhotoPositionDisclosure photoPositions,
        PlacePointDisclosure placePoints,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var link = await Standing(db).FirstOrDefaultAsync(l => l.Id == id, ct);
        if (link is null || await placePoints.IsHiddenAsync(ctx, link.Id, ct))
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (await FixedPlacePointAsync(db, link, ct) is { } fixedProblem)
        {
            return fixedProblem;
        }

        var relation = await RelationAsync(db, request.RelationTypeId, ct);
        if (request.RelationTypeId is not null && relation is null)
        {
            return ApiProblems.BadRequest(RelationNotFoundCode, "The relation type does not exist.");
        }

        // Nor is a link of another kind turned into one: a pin made by a side door would answer to
        // none of the cave, place and one-per-place rules a pin is written under.
        if (ResLinkRules.IsPlacePoint(relation?.Code))
        {
            return ApiProblems.Conflict(PlacePointFixedCode, PlacePointFixedDetail);
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockForMemberWriteAsync(db, link.Id, ct);

        // Decided inside the lock rather than before it: the third arm of the edit rule
        // reads which member carries the main marker, and that marker moves. A decision
        // taken on a snapshot this write would not go on to see could let a caller whose
        // right came from the old main member finish a write after the subject had already
        // been handed to something else.
        if (!await MayEditAsync(db, access, targets, ctx, link, ct))
        {
            return ApiProblems.Forbidden();
        }

        var members = await db.ResLinkMembers.Where(m => m.ResLinkId == link.Id).ToListAsync(ct);
        var directed = relation?.Directed ?? false;

        ResLinkMember? promote = null;
        if (request.MainMemberId is { } mainId)
        {
            promote = members.FirstOrDefault(m => m.Id == mainId);
            if (promote is null)
            {
                return ApiProblems.BadRequest(MemberNotFoundCode, "mainMemberId names no member of this link.");
            }
        }

        // The state this write would leave behind: an explicit mainMemberId becomes the
        // sole main; without one, switching to an undirected (or no) relation clears the
        // now-meaningless markers, and a directed relation keeps what is there.
        var resultingMains = promote is not null ? 1 : directed ? members.Count(m => m.IsMain) : 0;
        if (ResLinkRules.MainMarkerProblem(directed, members.Count, resultingMains) is { } problem)
        {
            return MainProblem(problem);
        }

        var resultingMain = promote ?? (directed ? members.FirstOrDefault(m => m.IsMain) : null);
        if (await TripMomentProblemAsync(targets, ctx, relation, resultingMain, ct) is { } momentProblem)
        {
            return momentProblem;
        }

        var demotions = members
            .Where(m => m.IsMain && (promote is not null ? m.Id != promote.Id : !directed))
            .ToList();

        // Demotions land before the promotion: the single-main partial unique index is
        // checked per statement, so the marker must come off one row before it can go
        // onto another inside the same transaction.
        foreach (var member in demotions)
        {
            member.IsMain = false;
        }

        link.Description = request.Description;
        link.RelationTypeId = relation?.Id;
        await db.SaveChangesAsync(ct);
        if (promote is not null && !promote.IsMain)
        {
            promote.IsMain = true;
            await db.SaveChangesAsync(ct);
        }

        await tx.CommitAsync(ct);

        return TypedResults.Ok((await ProjectAsync(
            db, targets, associations, protection, photoPositions, access, ctx, [link], ct)).Single());
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> DeleteAsync(
        Guid id,
        SilexGisDbContext db,
        ResLinkTargetDirectory targets,
        PlacePointDisclosure placePoints,
        PlacePointStore placePointStore,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var link = await Standing(db).FirstOrDefaultAsync(l => l.Id == id, ct);
        if (link is null || await placePoints.IsHiddenAsync(ctx, link.Id, ct))
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        // A place pinned on a phone's map is removed the way a phone removes it — stamped, so the
        // phones are told, and by whoever may change the map's cave.
        if (await IsPlacePointAsync(db, link, ct))
        {
            return await PlacePointEndpoints.RemoveAsync(db, placePointStore, access, ctx, link.Id, ct);
        }

        // Locked like every other write of this link, so the edit decision and the delete
        // it authorises see one snapshot: the rule reads the main marker, and a concurrent
        // promotion could otherwise hand the subject away between the two.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockForMemberWriteAsync(db, link.Id, ct);

        // Inside the lock, for the reason the update handler above spells out: the rule
        // reads the main marker, and the marker moves.
        if (!await MayEditAsync(db, access, targets, ctx, link, ct))
        {
            return ApiProblems.Forbidden();
        }

        // Members are removed explicitly rather than left to the cascade so each removal
        // is audited where members surface: in their target's timeline.
        var members = await db.ResLinkMembers.Where(m => m.ResLinkId == link.Id).ToListAsync(ct);
        db.ResLinkMembers.RemoveRange(members);
        db.ResLinks.Remove(link);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return TypedResults.NoContent();
    }
}
