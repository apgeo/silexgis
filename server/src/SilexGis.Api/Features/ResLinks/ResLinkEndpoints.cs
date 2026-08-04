// SPDX-License-Identifier: AGPL-3.0-or-later
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
using SilexGis.Infrastructure.Persistence;

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
/// Authoring is asymmetric on purpose: adding a member takes Read on its target — the
/// author asserts about what they can see, and the link shows that member to others only
/// if they can read it too — while editing or deleting a link belongs to its creator and
/// global administrators.
/// </para>
/// </remarks>
public static class ResLinkEndpoints
{
    public const string NotFoundCode = "reslink.not_found";
    public const string CodeUnresolvedCode = "reslink.code.unresolved";
    public const string EntityTypeUnknownCode = "reslink.entity_type_unknown";
    public const string TargetNotFoundCode = "reslink.target_not_found";
    public const string RelationNotFoundCode = "reslink.relation.not_found";
    public const string MemberNotFoundCode = "reslink.member.not_found";
    public const string MemberTargetNotFoundCode = "reslink.member.target_not_found";
    public const string DuplicateWholeCode = "reslink.member.duplicate_whole";
    public const string AnchorFileInvalidCode = ResLinkRules.AnchorFileInvalidCode;
    public const string GeoPointUnavailableCode = "reslink.member.geo_point_unavailable";

    /// <summary>The seeded feature kind a GPS-point member is born as — an ordinary
    /// generic feature, editable and protectable like any other from its first moment.</summary>
    private const string GeoPointFeatureTypeCode = "generic";

    public static RouteGroupBuilder MapResLinkEndpoints(this RouteGroupBuilder api)
    {
        var links = api.MapGroup("/reslinks").WithTags("ResLinks");

        links.MapGet("/for-target", ForTargetAsync)
            .WithSummary("Links incident to one target, sibling members resolved; the total doubles as the badge count.");
        links.MapGet("/targets/search", SearchTargetsAsync)
            .WithSummary("Picker feed: readable targets of one type matching a query, as uniform rows.");
        links.MapPost("/", CreateAsync).WithValidation<ResLinkCreateRequest>()
            .WithSummary("Creates a link with its initial members (at least one).");
        links.MapGet("/{idOrCode}", GetAsync)
            .WithSummary("One link by id or by short code — told apart by shape.");
        links.MapPatch("/{id:guid}", UpdateAsync).WithValidation<ResLinkUpdateRequest>()
            .WithSummary("Sets description and relation type (creator or admin); mainMemberId moves the marker with them.");
        links.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Hard-deletes a link and its members, audited (creator or admin).");
        links.MapPost("/{id:guid}/members", AddMemberAsync).WithValidation<ResLinkMemberAddRequest>()
            .WithSummary("Adds a member — a readable target, or a new GPS point via newGeoPoint (creator or admin).");
        links.MapPatch("/{id:guid}/members/{memberId:guid}", UpdateMemberAsync)
            .WithValidation<ResLinkMemberUpdateRequest>()
            .WithSummary("Edits isMain/sortOrder/note; anchors are replace-only — delete and re-add.");
        links.MapDelete("/{id:guid}/members/{memberId:guid}", DeleteMemberAsync)
            .WithSummary("Removes a member; the last member is refused — delete the link instead.");

        return api;
    }

    private static async Task<Results<Created<ResLinkDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        ResLinkCreateRequest request,
        SilexGisDbContext db,
        ResLinkTargetDirectory targets,
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

        var dto = (await ProjectAsync(db, targets, ctx, [link], ct)).Single();
        return TypedResults.Created($"/api/v1/reslinks/{link.Id}", dto);
    }

    private static async Task<Results<Ok<ResLinkDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        string idOrCode,
        SilexGisDbContext db,
        ResLinkTargetDirectory targets,
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
            var byId = await db.ResLinks.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);
            return byId is null
                ? ApiProblems.NotFound(NotFoundCode)
                : TypedResults.Ok((await ProjectAsync(db, targets, ctx, [byId], ct)).Single());
        }

        var byCode = ResLinkRules.IsShortCode(idOrCode)
            ? await db.ResLinks.AsNoTracking().FirstOrDefaultAsync(l => l.ShortCode == idOrCode, ct)
            : null;
        return byCode is null
            ? ApiProblems.NotFound(CodeUnresolvedCode)
            : TypedResults.Ok((await ProjectAsync(db, targets, ctx, [byCode], ct)).Single());
    }

    private static async Task<Results<Ok<ResLinkDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        ResLinkUpdateRequest request,
        SilexGisDbContext db,
        ResLinkTargetDirectory targets,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var link = await db.ResLinks.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (link is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!MayEdit(ctx, link))
        {
            return ApiProblems.Forbidden();
        }

        var relation = await RelationAsync(db, request.RelationTypeId, ct);
        if (request.RelationTypeId is not null && relation is null)
        {
            return ApiProblems.BadRequest(RelationNotFoundCode, "The relation type does not exist.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockForMemberWriteAsync(db, link.Id, ct);

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

        return TypedResults.Ok((await ProjectAsync(db, targets, ctx, [link], ct)).Single());
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> DeleteAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var link = await db.ResLinks.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (link is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!MayEdit(ctx, link))
        {
            return ApiProblems.Forbidden();
        }

        // Members are removed explicitly rather than left to the cascade so each removal
        // is audited where members surface: in their target's timeline.
        var members = await db.ResLinkMembers.Where(m => m.ResLinkId == link.Id).ToListAsync(ct);
        db.ResLinkMembers.RemoveRange(members);
        db.ResLinks.Remove(link);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Created<ResLinkMemberDto>, UnauthorizedHttpResult, ProblemHttpResult>> AddMemberAsync(
        Guid id,
        ResLinkMemberAddRequest request,
        SilexGisDbContext db,
        ResLinkTargetDirectory targets,
        FeatureWriteService featureWriter,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var link = await db.ResLinks.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);
        if (link is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!MayEdit(ctx, link))
        {
            return ApiProblems.Forbidden();
        }

        // The whole act — the optional new feature, the marker handover and the insert —
        // is one transaction, opened by taking the link's row lock so the snapshot the
        // count-sensitive rules below run against cannot be moved by a concurrent writer.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockForMemberWriteAsync(db, link.Id, ct);

        var members = await db.ResLinkMembers.Where(m => m.ResLinkId == link.Id).ToListAsync(ct);
        var relation = await RelationAsync(db, link.RelationTypeId, ct);
        if (!ResLinkRules.MayAddMember(members.Count))
        {
            return ApiProblems.Conflict(
                ResLinkRules.MemberLimitCode,
                $"A link holds at most {ResLinkRules.MaxMembers} members.");
        }

        Guid? featureId;
        AttachedEntityType? entityType = null;
        Guid? entityId = null;
        Feature? newPoint = null;
        if (request.NewGeoPoint is { } point)
        {
            var pointType = await db.FeatureTypes.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Code == GeoPointFeatureTypeCode, ct);
            if (pointType is null)
            {
                return ApiProblems.BadRequest(
                    GeoPointUnavailableCode, "The generic feature kind is not installed.");
            }

            // Creating the point is an ordinary feature creation and answers to the same
            // Create rule — the link surface adds no way around it.
            var createFacts = await CreateContext.ParentCreateFactsAsync(
                    db, ctx, null, null, FeatureKind.Generic, ct, pointType.Id)
                ?? AccessTargetFacts.ForCreate(null, [], null, FeatureKind.Generic, pointType.Id);
            if (!CreateRules.MayCreate(ctx, AccessDomain.Features, createFacts))
            {
                return ApiProblems.Forbidden(CreateRules.ForbiddenCode);
            }

            var geom = point.Z is { } z
                ? new Point(new CoordinateZ(point.Lon, point.Lat, z))
                : new Point(point.Lon, point.Lat);
            geom.SRID = 4326;
            newPoint = new Feature
            {
                Name = string.IsNullOrWhiteSpace(point.Name) ? null : point.Name.Trim(),
                FeatureTypeId = pointType.Id,
                Geom = geom,
                OwnerUserId = ctx.UserId,
                // Public by deliberate default: the point exists to be seen by whoever
                // sees the link; the caller narrows it here or later by editing the
                // feature like any other.
                Visibility = point.Visibility ?? Visibility.Public,
            };

            try
            {
                await featureWriter.CreateGenericAsync(newPoint, [], ct);
            }
            catch (FeatureWriteException ex)
            {
                return ApiProblems.BadRequest(ex.Code, string.Join(" ", ex.Errors));
            }

            featureId = newPoint.Id;
        }
        else
        {
            if (!ResLinkTargets.TryParse(request.TargetType, out var type))
            {
                return ApiProblems.BadRequest(
                    ResLinkRules.TypeNotLinkableCode, $"'{request.TargetType}' is not a linkable target type.");
            }

            featureId = type is null ? request.TargetId : null;
            entityType = type;
            entityId = type is null ? null : request.TargetId;

            if (!await targets.CanReadAsync(ctx, type, request.TargetId!.Value, ct))
            {
                return ApiProblems.NotFound(MemberTargetNotFoundCode);
            }
        }

        var anchor = RawAnchor(request.Anchor);
        var shape = new ResLinkRules.MemberShape(
            featureId, entityType, entityId, request.AnchorKind, anchor, request.AnchorFileId);
        if (MemberProblem(shape) is { } memberProblem)
        {
            return memberProblem;
        }

        if (await AnchorFilePinProblemAsync(db, entityId, request.AnchorFileId, ct) is { } pinProblem)
        {
            return pinProblem;
        }

        if (request.AnchorKind == AnchorKind.Whole && members.Any(m =>
                m.AnchorKind == AnchorKind.Whole && m.FeatureId == featureId
                && m.EntityType == entityType && m.EntityId == entityId))
        {
            return ApiProblems.Conflict(DuplicateWholeCode, "This whole target is already a member.");
        }

        var currentMain = members.FirstOrDefault(m => m.IsMain);
        var resultingMains = request.IsMain || currentMain is not null ? 1 : 0;
        if (ResLinkRules.MainMarkerProblem(relation, members.Count + 1, resultingMains) is { } mainProblem)
        {
            return MainProblem(mainProblem);
        }

        var member = new ResLinkMember
        {
            ResLinkId = link.Id,
            FeatureId = featureId,
            EntityType = entityType,
            EntityId = entityId,
            IsMain = request.IsMain,
            AnchorKind = request.AnchorKind,
            Anchor = anchor,
            AnchorFileId = request.AnchorFileId,
            SortOrder = request.SortOrder,
            Note = request.Note,
            AddedBy = ctx.UserId,
        };

        // The current main is demoted in its own statement first because the single-main
        // partial unique index is checked per statement.
        if (request.IsMain && currentMain is not null)
        {
            currentMain.IsMain = false;
            await db.SaveChangesAsync(ct);
        }

        db.ResLinkMembers.Add(member);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var dto = (await ProjectMembersAsync(db, targets, ctx, [member], ct))[member.Id];
        return TypedResults.Created($"/api/v1/reslinks/{link.Id}/members/{member.Id}", dto);
    }

    private static async Task<Results<Ok<ResLinkMemberDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateMemberAsync(
        Guid id,
        Guid memberId,
        ResLinkMemberUpdateRequest request,
        SilexGisDbContext db,
        ResLinkTargetDirectory targets,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var link = await db.ResLinks.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);
        if (link is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!MayEdit(ctx, link))
        {
            return ApiProblems.Forbidden();
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockForMemberWriteAsync(db, link.Id, ct);

        var members = await db.ResLinkMembers.Where(m => m.ResLinkId == link.Id).ToListAsync(ct);
        var member = members.FirstOrDefault(m => m.Id == memberId);
        if (member is null)
        {
            return ApiProblems.NotFound(MemberNotFoundCode);
        }

        var relation = await RelationAsync(db, link.RelationTypeId, ct);
        var currentMain = members.FirstOrDefault(m => m.IsMain);

        // Promoting hands the marker over from the current main in the same act — with
        // exactly one main required, "make this one main" is the only way to move it.
        var resultingMains = request.IsMain
            ? 1
            : currentMain is not null && currentMain.Id != member.Id ? 1 : 0;
        if (ResLinkRules.MainMarkerProblem(relation, members.Count, resultingMains) is { } problem)
        {
            return MainProblem(problem);
        }

        if (request.IsMain && currentMain is not null && currentMain.Id != member.Id)
        {
            currentMain.IsMain = false;
            await db.SaveChangesAsync(ct);
        }

        member.IsMain = request.IsMain;
        member.SortOrder = request.SortOrder;
        member.Note = request.Note;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return TypedResults.Ok((await ProjectMembersAsync(db, targets, ctx, [member], ct))[member.Id]);
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> DeleteMemberAsync(
        Guid id,
        Guid memberId,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var link = await db.ResLinks.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);
        if (link is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!MayEdit(ctx, link))
        {
            return ApiProblems.Forbidden();
        }

        // Locked first: the one-member floor and the directed-main successor rule count
        // a snapshot, and two concurrent removals passing the same count could otherwise
        // leave a zero-member link — a state nothing repairs.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockForMemberWriteAsync(db, link.Id, ct);

        var members = await db.ResLinkMembers.Where(m => m.ResLinkId == link.Id).ToListAsync(ct);
        var member = members.FirstOrDefault(m => m.Id == memberId);
        if (member is null)
        {
            return ApiProblems.NotFound(MemberNotFoundCode);
        }

        if (!ResLinkRules.MayRemoveMember(members.Count))
        {
            return ApiProblems.Conflict(
                ResLinkRules.LastMemberCode,
                "The last member cannot be removed; delete the link instead.");
        }

        var directed = (await RelationAsync(db, link.RelationTypeId, ct))?.Directed ?? false;
        var remaining = members.Count - 1;
        if (member.IsMain && directed && remaining >= 2)
        {
            return ApiProblems.BadRequest(
                ResLinkRules.MainRequiredCode, "Promote another member before removing the main one.");
        }

        db.ResLinkMembers.Remove(member);
        if (remaining < 2)
        {
            // Below two members the marker means nothing and is forbidden by the rules,
            // so it comes off mechanically with the removal that got the link there.
            foreach (var survivor in members.Where(m => m.Id != member.Id && m.IsMain))
            {
                survivor.IsMain = false;
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// The panel query: links having a member that targets the named entity, each with
    /// all members resolved for display. Requires Read on the target — the panel sits on
    /// the target's page, so the target's own rules gate it.
    /// </summary>
    private static async Task<Results<Ok<PagedResult<ResLinkDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ForTargetAsync(
        string type,
        Guid id,
        SilexGisDbContext db,
        ResLinkTargetDirectory targets,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct,
        int? page = null,
        int? pageSize = null)
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

        var incident = parsedType is { } pairType
            ? db.ResLinkMembers.AsNoTracking().Where(m => m.EntityType == pairType && m.EntityId == id)
            : db.ResLinkMembers.AsNoTracking().Where(m => m.FeatureId == id);
        var linkQuery = db.ResLinks.AsNoTracking()
            .Where(l => incident.Any(m => m.ResLinkId == l.Id))
            .OrderByDescending(l => l.CreatedAt)
            .ThenBy(l => l.Id);

        var (p, size) = Paging.Normalize(page, pageSize);
        var total = await linkQuery.CountAsync(ct);
        var links = await linkQuery.Skip((p - 1) * size).Take(size).ToListAsync(ct);
        var dtos = await ProjectAsync(db, targets, ctx, links, ct);
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

        if (string.IsNullOrWhiteSpace(q))
        {
            return TypedResults.Ok(new List<ResLinkTargetHitDto>());
        }

        var hits = await targets.Of(parsedType)
            .SearchAsync(ctx, q.Trim(), Math.Clamp(limit ?? 20, 1, 50), ct);
        return TypedResults.Ok(hits.ToList());
    }

    // ---- shared pieces --------------------------------------------------------------

    /// <summary>Edit and delete belong to the creator and to full administrators; a link
    /// whose creator account is gone answers only to administrators.</summary>
    private static bool MayEdit(AccessContext ctx, ResLink link) =>
        ctx.IsFullAdmin || (link.CreatedBy is { } creator && creator == ctx.UserId);

    private static Task<ResLinkRelationType?> RelationAsync(
        SilexGisDbContext db, long? relationTypeId, CancellationToken ct) =>
        relationTypeId is { } id
            ? db.ResLinkRelationTypes.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct)
            : Task.FromResult<ResLinkRelationType?>(null);

    private static string? RawAnchor(JsonElement? anchor) =>
        anchor is { ValueKind: JsonValueKind.Object } payload ? payload.GetRawText() : null;

    private static JsonElement? ParseAnchor(string? anchor)
    {
        if (anchor is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(anchor);
        return document.RootElement.Clone();
    }

    private static ProblemHttpResult? MemberProblem(ResLinkRules.MemberShape shape) =>
        ResLinkRules.MemberProblem(shape) is { } code
            ? ApiProblems.BadRequest(code, code switch
            {
                ResLinkRules.TargetInvalidCode => "Exactly one target shape must be set.",
                ResLinkRules.TypeNotLinkableCode => "This entity type cannot join a link.",
                ResLinkRules.InvalidAnchorKindCode => "The target type does not admit this anchor kind.",
                ResLinkRules.AnchorFileInvalidCode =>
                    "Only a part-anchor into a document pins a measured-against file.",
                ResLinkRules.AnchorPinRequiredCode =>
                    "This anchor kind addresses pixels of a specific file, so it must pin one.",
                _ => ResLinkRules.AnchorPayloadProblem(shape.AnchorKind, shape.Anchor)
                    ?? "The anchor payload is invalid.",
            })
            : null;

    private static ProblemHttpResult MainProblem(string code) =>
        ApiProblems.BadRequest(code, code switch
        {
            ResLinkRules.MainRequiredCode =>
                "A directed relation with two or more members needs exactly one main member.",
            ResLinkRules.MainNotSingleCode => "Only one member can be main.",
            _ => "A main member is only allowed on a directed relation with two or more members.",
        });

    /// <summary>
    /// The storage half of the measured-against pin: it must name a file of the very
    /// document the member targets. The shape half — which members may or must carry a
    /// pin at all — is decided with the member rules; this check runs only after the
    /// caller has passed the read floor on the target, so its answer never confirms a
    /// file-to-document association about something the caller may not read.
    /// </summary>
    private static async Task<ProblemHttpResult?> AnchorFilePinProblemAsync(
        SilexGisDbContext db,
        Guid? documentId,
        Guid? anchorFileId,
        CancellationToken ct)
    {
        if (anchorFileId is not { } pin)
        {
            return null;
        }

        var belongs = await (
                from file in db.StoredFiles.AsNoTracking()
                join version in db.DocumentVersions.AsNoTracking() on file.DocumentVersionId equals version.Id
                where file.Id == pin && version.DocumentId == documentId
                select file.Id)
            .AnyAsync(ct);
        return belongs
            ? null
            : ApiProblems.BadRequest(
                AnchorFileInvalidCode, "The pinned file does not belong to the target document.");
    }

    /// <summary>
    /// Serializes writers of one link's membership: takes the link's row lock for the
    /// rest of the surrounding transaction (stamping the change on the link's own
    /// UpdatedAt while at it). The floor, marker and duplicate rules are all checked
    /// against a members snapshot loaded after this point, so a concurrent writer
    /// queues here, re-reads what the winner committed, and refuses the way a
    /// sequential request would — instead of racing the same snapshot past a
    /// count-based rule or tripping a backstop unique index into an unhandled error.
    /// </summary>
    private static Task<int> LockForMemberWriteAsync(
        SilexGisDbContext db, Guid linkId, CancellationToken ct) =>
        db.ResLinks
            .Where(l => l.Id == linkId)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.UpdatedAt, DateTimeOffset.UtcNow), ct);

    /// <summary>
    /// Saves pending changes, redrawing the link's short code on the rare unique-index
    /// collision. 62⁸ makes a collision lottery-grade, so the code is never checked
    /// first — the index is the arbiter and the loser simply draws again.
    /// </summary>
    private static async Task SaveWithFreshShortCodeAsync(
        SilexGisDbContext db, ResLink link, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateException e) when (attempt < 4 && IsShortCodeCollision(e))
            {
                link.ShortCode = ResLinkRules.NewShortCode();
            }
        }
    }

    private static bool IsShortCodeCollision(DbUpdateException e) =>
        e.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ix_res_links_short_code",
        };

    private static async Task<List<ResLinkDto>> ProjectAsync(
        SilexGisDbContext db,
        ResLinkTargetDirectory targets,
        AccessContext ctx,
        IReadOnlyList<ResLink> links,
        CancellationToken ct)
    {
        if (links.Count == 0)
        {
            return [];
        }

        var linkIds = links.Select(l => l.Id).ToList();
        var members = await db.ResLinkMembers.AsNoTracking()
            .Where(m => linkIds.Contains(m.ResLinkId))
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.CreatedAt)
            .ThenBy(m => m.Id)
            .ToListAsync(ct);

        var relationIds = links
            .Where(l => l.RelationTypeId is not null)
            .Select(l => l.RelationTypeId!.Value)
            .Distinct()
            .ToList();
        var relations = relationIds.Count == 0
            ? new Dictionary<long, ResLinkRelationType>()
            : await db.ResLinkRelationTypes.AsNoTracking()
                .Where(r => relationIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, ct);

        var memberDtos = await ProjectMembersAsync(db, targets, ctx, members, ct);
        var byLink = members
            .GroupBy(m => m.ResLinkId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ResLinkMemberDto>)[.. g.Select(m => memberDtos[m.Id])]);

        return [.. links.Select(l => new ResLinkDto(
            l.Id,
            l.ShortCode,
            l.RelationTypeId is { } rid && relations.TryGetValue(rid, out var relation)
                ? ResLinkRelationTypeEndpoints.ToDto(relation)
                : null,
            l.Description,
            l.CreatedBy,
            l.CreatedAt,
            l.UpdatedAt,
            byLink.GetValueOrDefault(l.Id) ?? []))];
    }

    /// <summary>
    /// Members as one caller sees them: display through each target world's resolver
    /// (null when the target is unreadable — the member still appears, but nothing of the
    /// target travels with it, the anchor payload included, since a payload can quote
    /// what it anchors to), and the anchor state — Degraded when the pinned
    /// measured-against file is no longer what the document currently serves, because
    /// following such an anchor against current content could highlight the wrong thing.
    /// </summary>
    private static async Task<Dictionary<Guid, ResLinkMemberDto>> ProjectMembersAsync(
        SilexGisDbContext db,
        ResLinkTargetDirectory targets,
        AccessContext ctx,
        IReadOnlyList<ResLinkMember> members,
        CancellationToken ct)
    {
        var displays = new Dictionary<Guid, ResLinkTargetDisplayDto>();
        foreach (var group in members.GroupBy(m => m.EntityType))
        {
            var ids = group.Select(m => m.FeatureId ?? m.EntityId!.Value).Distinct().ToList();
            var resolved = await targets.Of(group.Key).ResolveAsync(ctx, ids, ct);
            foreach (var member in group)
            {
                if (resolved.TryGetValue(member.FeatureId ?? member.EntityId!.Value, out var display))
                {
                    displays[member.Id] = display;
                }
            }
        }

        // Only readable members' pins are even asked about: the anchor state is derived
        // from the pinned document's version chain, so for an unreadable target it is
        // withheld with everything else — a caller barred from a document must not learn
        // from a link that the document was re-versioned.
        var pins = members
            .Where(m => m.AnchorFileId is not null && displays.ContainsKey(m.Id))
            .Select(m => m.AnchorFileId!.Value)
            .Distinct()
            .ToList();
        var stalePins = pins.Count == 0
            ? new HashSet<Guid>()
            : await (
                    from file in db.StoredFiles.AsNoTracking()
                    join version in db.DocumentVersions.AsNoTracking()
                        on file.DocumentVersionId equals version.Id
                    where pins.Contains(file.Id) && !version.IsCurrent
                    select file.Id)
                .ToHashSetAsync(ct);

        return members.ToDictionary(m => m.Id, m =>
        {
            var display = displays.GetValueOrDefault(m.Id);
            var readable = display is not null;
            return new ResLinkMemberDto(
                m.Id,
                ResLinkTargets.NameOf(m.FeatureId, m.EntityType),
                m.FeatureId ?? m.EntityId!.Value,
                m.IsMain,
                m.SortOrder,
                m.Note,
                m.AnchorKind,
                readable ? ParseAnchor(m.Anchor) : null,
                readable ? m.AnchorFileId : null,
                readable && m.AnchorFileId is { } pin && stalePins.Contains(pin)
                    ? ResLinkAnchorState.Degraded
                    : ResLinkAnchorState.Exact,
                display);
        });
    }
}
