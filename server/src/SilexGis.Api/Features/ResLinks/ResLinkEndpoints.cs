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
            .WithSummary("Links incident to one target, sibling members resolved, optionally narrowed to one relation code; the total doubles as the badge count.");
        links.MapGet("/targets/search", SearchTargetsAsync)
            .WithSummary("Picker feed: readable targets of one type matching a query, as uniform rows.");
        links.MapGet("/point-default", PointDefaultAsync)
            .WithSummary("The audience a new GPS point takes when the request names none, for this caller.");
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
            var byId = await db.ResLinks.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);
            return byId is null
                ? ApiProblems.NotFound(NotFoundCode)
                : TypedResults.Ok((await ProjectAsync(
                    db, targets, associations, protection, photoPositions, access, ctx, [byId], ct)).Single());
        }

        var byCode = ResLinkRules.IsShortCode(idOrCode)
            ? await db.ResLinks.AsNoTracking().FirstOrDefaultAsync(l => l.ShortCode == idOrCode, ct)
            : null;
        return byCode is null
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
        IAccessService access,
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

        return TypedResults.Ok((await ProjectAsync(
            db, targets, associations, protection, photoPositions, access, ctx, [link], ct)).Single());
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
        IAccessService access,
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

            // Audience when the request states none — the shared rule, so what the form
            // told the user before they placed the point is what the point gets. A caller
            // who belongs to several clubs has no single one to mean and lands on the
            // signed-in audience; they pick a club by editing the feature afterwards.
            var (defaultVisibility, ownClubId) = ResLinkRules.DefaultPointAudience(ctx.CavingGroupIds);
            var visibility = point.Visibility ?? defaultVisibility;

            // A club-visible row with no club named is a band that admits nobody, so a
            // point that ends up club-visible names the creator's club. Binding to a club
            // one is a member of is what the binding guard admits anyway, and the id here
            // comes from that very membership list. An explicit club-visible request from
            // a caller with no single club names none — the binding guard is what refuses
            // or admits that, not this line.
            var clubBinding = visibility == Visibility.CavingGroup ? ownClubId : null;

            newPoint = new Feature
            {
                Name = string.IsNullOrWhiteSpace(point.Name) ? null : point.Name.Trim(),
                FeatureTypeId = pointType.Id,
                Geom = geom,
                OwnerUserId = ctx.UserId,
                Visibility = visibility,
                CavingGroupId = clubBinding,
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

        // The snapshot behind this check is uncut by membership disclosure, so the
        // refusal can confirm to the caller that a whole target — possibly a member
        // their own reads withhold — already sits in the link. Accepted, eyes open:
        // only the link's creator or a full administrator (who reads every member
        // anyway) can reach this line, so the audience is whoever authored the link,
        // and the honest alternatives are worse — a disclosure-cut snapshot would
        // drive the insert into the unique backstop index, and answering "created"
        // for a row that already exists would either misstate its fields or overwrite
        // another author's. The count-based rules above share the same snapshot and
        // the same bounded audience: a creator can infer that hidden members exist,
        // never which targets they name.
        if (request.AnchorKind == AnchorKind.Whole && members.Any(m =>
                m.AnchorKind == AnchorKind.Whole && m.FeatureId == featureId
                && m.EntityType == entityType && m.EntityId == entityId))
        {
            return ApiProblems.Conflict(DuplicateWholeCode, "This whole target is already a member.");
        }

        var currentMain = members.FirstOrDefault(m => m.IsMain);

        // A directed link standing at a single member carries no marker — below two
        // members it would mean nothing, so the removal that got the link there took it
        // off. Growing such a link back hands the marker to the member that stayed,
        // unless the arriving one explicitly claims it. Refusing instead would leave the
        // caller exactly one way through — marking the arriving member main — which
        // silently reverses the relation: the link whose distinguished side was "this
        // trip surveyed those caves" would come back reading "this cave surveyed that
        // trip". The member already in the link is what the relation was built around,
        // so it is the one the marker belongs to.
        var incumbent = relation?.Directed == true && currentMain is null
            && !request.IsMain && members.Count == 1
            ? members[0]
            : null;

        var resultingMains = request.IsMain || currentMain is not null || incumbent is not null ? 1 : 0;
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
        else if (incumbent is not null)
        {
            incumbent.IsMain = true;
        }

        db.ResLinkMembers.Add(member);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        // Projected without the membership-disclosure cut the link reads apply: this
        // acknowledgment answers the author of the very write, and the membership
        // cannot be news to whoever just asserted it.
        var dto = (await ProjectMembersAsync(db, targets, access, ctx, [member], ct))[member.Id];
        return TypedResults.Created($"/api/v1/reslinks/{link.Id}/members/{member.Id}", dto);
    }

    private static async Task<Results<Ok<ResLinkMemberDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateMemberAsync(
        Guid id,
        Guid memberId,
        ResLinkMemberUpdateRequest request,
        SilexGisDbContext db,
        ResLinkTargetDirectory targets,
        IAccessService access,
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

        // Like the add acknowledgment: the caller wrote this member, so membership
        // disclosure is not re-asked for the echo.
        return TypedResults.Ok(
            (await ProjectMembersAsync(db, targets, access, ctx, [member], ct))[member.Id]);
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
    /// <param name="relation">
    /// Optional relation code, narrowing the answer to links of that one relation — how a
    /// page asks for a single role ("what did this trip survey?") without reading every
    /// link and sorting them itself. Addressed by code rather than by row id because the
    /// code is the shared vocabulary clients already translate their labels by, while the
    /// numeric id is assigned per installation. Blank or absent means every relation,
    /// untyped links included; a code no relation type carries is refused rather than
    /// answered with an empty page, so a mistyped role does not read as a role nobody used.
    /// The answer is a page of links, not one link: a link answers to whoever authored it,
    /// so two people recording the same role on the same target write two links, and what
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
        var incidentLinks = db.ResLinks.AsNoTracking()
            .Where(l => incident.Any(m => m.ResLinkId == l.Id));

        // The relation filter narrows the one query both paging arms below are built from,
        // and it has to: one arm counts rows in the database while the other counts what
        // survived the disclosure cut, so a filter reaching only one of them would report a
        // total that disagrees with the rows returned — and that total is the badge.
        if (relationTypeId is { } roleId)
        {
            incidentLinks = incidentLinks.Where(l => l.RelationTypeId == roleId);
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
            // of an honest badge. If a heavily-linked protected feature ever
            // appears, cap the candidates or split the sibling-exposure question into
            // a cheaper first pass before projecting.
            var candidates = await linkQuery.ToListAsync(ct);
            var projected = await ProjectAsync(
                db, targets, associations, protection, photoPositions, access, ctx, candidates, ct);
            var listed = projected
                .Where(l => l.Members.Any(m =>
                    m.TargetType == ResLinkTargets.FeatureName && m.TargetId == id))
                .ToList();
            return TypedResults.Ok(new PagedResult<ResLinkDto>(
                [.. listed.Skip((p - 1) * size).Take(size)], p, size, listed.Count));
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

        if (string.IsNullOrWhiteSpace(q))
        {
            return TypedResults.Ok(new List<ResLinkTargetHitDto>());
        }

        var hits = await targets.Of(parsedType)
            .SearchAsync(ctx, q.Trim(), Math.Clamp(limit ?? 20, 1, 50), ct);
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

    /// <param name="skipMembershipCut">
    /// True only for the create echo: like the member-write acknowledgments, it answers
    /// the caller with memberships that caller asserted in the same request, so the
    /// disclosure cut every other link projection applies is deliberately not re-asked.
    /// </param>
    private static async Task<List<ResLinkDto>> ProjectAsync(
        SilexGisDbContext db,
        ResLinkTargetDirectory targets,
        AssociationDisclosure associations,
        FeatureProtection protection,
        PhotoPositionDisclosure photoPositions,
        IAccessService access,
        AccessContext ctx,
        IReadOnlyList<ResLink> links,
        CancellationToken ct,
        bool skipMembershipCut = false)
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

        // Displays are resolved before the disclosure cut: which members this caller can
        // read is also an input to it — only a readable sibling can show them coordinates.
        var displays = await ResolveDisplaysAsync(targets, ctx, members, ct);

        // A member naming a feature is an association that can place the feature, so the
        // rule governing document attachments decides whether this caller is told of it
        // at all. A withheld membership leaves the response entirely — no bare row: for
        // an unreadable target the row admits only that something restricted exists, but
        // this row would name the feature, which is the very fact being kept. Each
        // member is judged with its siblings: one of them showing this caller exact
        // coordinates puts the feature's name beside a position, which is the pairing
        // the reveal setting never opens.
        if (!skipMembershipCut)
        {
            var exposing = await ExposingMemberIdsAsync(
                db, protection, photoPositions, access, ctx, members, displays, ct);
            var exposingByLink = members
                .Where(m => exposing.Contains(m.Id))
                .GroupBy(m => m.ResLinkId)
                .ToDictionary(g => g.Key, g => g.Select(m => m.Id).ToList());
            var withheld = await associations.WithheldIdsAsync(
                ctx,
                [.. members.Select(m => new AssociationCandidate(
                    m.Id,
                    ResLinkRules.MemberAssociation(
                        m.FeatureId,
                        exposingByLink.TryGetValue(m.ResLinkId, out var siblings)
                            && siblings.Any(s => s != m.Id))))],
                ct);
            if (withheld.Count > 0)
            {
                members = [.. members.Where(m => !withheld.Contains(m.Id))];
            }
        }

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

        var memberDtos = await AssembleMembersAsync(db, access, ctx, members, displays, ct);
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
    /// (null when the target is unreadable — the member still appears, but nothing of
    /// the target travels with it), the anchor presented under the anchor-presentation
    /// rule. Whether a member row may be emitted at all — membership disclosure for
    /// protected-feature targets — is its callers' decision: link projections drop
    /// withheld members before asking here, while the write acknowledgments — the
    /// member-write echoes and the create response — deliberately do not, since they
    /// answer the caller with memberships that caller just asserted.
    /// </summary>
    private static async Task<Dictionary<Guid, ResLinkMemberDto>> ProjectMembersAsync(
        SilexGisDbContext db,
        ResLinkTargetDirectory targets,
        IAccessService access,
        AccessContext ctx,
        IReadOnlyList<ResLinkMember> members,
        CancellationToken ct)
    {
        var displays = await ResolveDisplaysAsync(targets, ctx, members, ct);
        return await AssembleMembersAsync(db, access, ctx, members, displays, ct);
    }

    /// <summary>Each member's display through its target world's resolver, keyed by
    /// member id; a member whose target is missing or unreadable has no entry.</summary>
    private static async Task<Dictionary<Guid, ResLinkTargetDisplayDto>> ResolveDisplaysAsync(
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

        return displays;
    }

    /// <summary>
    /// The members that show this caller exact coordinates — the sibling fact the
    /// membership-disclosure mapping asks for. Five ways a member does: it names a
    /// feature with a drawn geometry the caller may both read and place exactly; it
    /// names a trip the caller may read that carries a sketch of its own, which is
    /// served exactly to every reader of the trip; a file of its document carries a
    /// capture point of its own that the photo-position rule discloses to this caller
    /// and that the caller can actually fetch — any currently served file, or a
    /// superseded one for callers the document's own rules let into version history; it
    /// names a survey model the caller may open, which resolves only with exact view on
    /// its cave and routes straight to it; or its anchor reads coordinates out of a
    /// geofile the caller may read. Batched throughout — a link page costs the same
    /// handful of queries however many members it has.
    /// </summary>
    private static async Task<HashSet<Guid>> ExposingMemberIdsAsync(
        SilexGisDbContext db,
        FeatureProtection protection,
        PhotoPositionDisclosure photoPositions,
        IAccessService access,
        AccessContext ctx,
        IReadOnlyList<ResLinkMember> members,
        IReadOnlyDictionary<Guid, ResLinkTargetDisplayDto> displays,
        CancellationToken ct)
    {
        var exposing = new HashSet<Guid>();

        // Feature members: readable (exact view alone does not put coordinates on a page
        // the caller cannot even list the feature on), positioned, and exactly placeable.
        var featureMembers = members
            .Where(m => m.FeatureId is not null && displays.ContainsKey(m.Id))
            .ToList();
        var featureIds = featureMembers.Select(m => m.FeatureId!.Value).Distinct().ToList();
        if (featureIds.Count > 0)
        {
            var positioned = await db.Features.AsNoTracking()
                .Where(f => featureIds.Contains(f.Id) && f.Geom != null)
                .Select(f => f.Id)
                .ToHashSetAsync(ct);
            if (positioned.Count > 0)
            {
                var exact = await protection.ExactViewIdsAsync(ctx, positioned, ct);
                exposing.UnionWith(featureMembers
                    .Where(m => exact.Contains(m.FeatureId!.Value))
                    .Select(m => m.Id));
            }
        }

        // Trip members carrying a sketch of their own. A trip's geometry is served exactly
        // to everyone who may read the trip — it is never snapped or omitted the way a
        // protected feature's is — so a readable positioned trip standing in a link puts
        // coordinates in front of the caller just as a placeable feature member does. A
        // resolved display is the proof the caller may read the trip; only the geometry is
        // left to ask about, and it is asked in one batched query.
        var tripMembers = members
            .Where(m => m.EntityType == AttachedEntityType.TripLog && displays.ContainsKey(m.Id))
            .ToList();
        var tripIds = tripMembers.Select(m => m.EntityId!.Value).Distinct().ToList();
        if (tripIds.Count > 0)
        {
            var positionedTrips = await db.TripLogs.AsNoTracking()
                .Where(t => tripIds.Contains(t.Id) && t.Geom != null)
                .Select(t => t.Id)
                .ToHashSetAsync(ct);
            exposing.UnionWith(tripMembers
                .Where(m => positionedTrips.Contains(m.EntityId!.Value))
                .Select(m => m.Id));
        }

        // Geofile members whose anchor addresses coordinates of a file the caller may read.
        exposing.UnionWith(members
            .Where(m => m.EntityType == AttachedEntityType.Geofile
                && ResLinkRules.AnchorAddressesCoordinates(m.AnchorKind)
                && displays.ContainsKey(m.Id))
            .Select(m => m.Id));

        // Survey-model members the caller may open. A model resolves only for callers
        // with exact view on its cave — its files are absolute georeferenced coordinates
        // and its display routes to that cave — so a resolved display IS the proof that
        // this member puts the cave's exact position one step from the reader, the same
        // disclosure as a feature member the caller may place.
        exposing.UnionWith(members
            .Where(m => m.EntityType == AttachedEntityType.SurveyModel && displays.ContainsKey(m.Id))
            .Select(m => m.Id));

        // Document members with a file stamped with a position of its own — the geotagged
        // photo — that the photo-position rule discloses to this caller. An undisclosable
        // capture point shows the caller nothing, so it does not count against the
        // siblings either; nor does a capture point the caller cannot reach: a superseded
        // file's geotag counts only for callers the document's own rules let into version
        // history, because for everyone else no route serves it.
        var documentMembers = members
            .Where(m => m.EntityType == AttachedEntityType.Document && displays.ContainsKey(m.Id))
            .ToList();
        var documentIds = documentMembers.Select(m => m.EntityId!.Value).Distinct().ToList();
        if (documentIds.Count > 0)
        {
            var geotagged = await (
                    from version in db.DocumentVersions.AsNoTracking()
                    join file in db.StoredFiles.AsNoTracking() on version.Id equals file.DocumentVersionId
                    where documentIds.Contains(version.DocumentId) && file.Geom != null
                    select new { version.DocumentId, FileId = file.Id, version.IsCurrent })
                .ToListAsync(ct);
            if (geotagged.Count > 0)
            {
                var disclosable = await photoPositions.DisclosableIdsAsync(
                    ctx, [.. geotagged.Select(g => g.FileId)], ct);
                var exposingDocuments = geotagged
                    .Where(g => g.IsCurrent && disclosable.Contains(g.FileId))
                    .Select(g => g.DocumentId)
                    .ToHashSet();

                var supersededOnly = geotagged
                    .Where(g => !g.IsCurrent && disclosable.Contains(g.FileId))
                    .Select(g => g.DocumentId)
                    .Where(id => !exposingDocuments.Contains(id))
                    .Distinct()
                    .ToList();
                if (supersededOnly.Count > 0)
                {
                    exposingDocuments.UnionWith(
                        await VersionReaderDocumentIdsAsync(db, access, ctx, supersededOnly, ct));
                }

                exposing.UnionWith(documentMembers
                    .Where(m => exposingDocuments.Contains(m.EntityId!.Value))
                    .Select(m => m.Id));
            }
        }

        return exposing;
    }

    /// <summary>
    /// Of the given documents, those whose superseded versions this caller may see — the
    /// document-write walk, the same one the version-history endpoint answers with,
    /// decided against each document's currently served file. Per-document, so callers
    /// keep the input small: documents behind stale pins, documents whose only geotag is
    /// a superseded file.
    /// </summary>
    private static async Task<HashSet<Guid>> VersionReaderDocumentIdsAsync(
        SilexGisDbContext db,
        IAccessService access,
        AccessContext ctx,
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken ct)
    {
        var readers = new HashSet<Guid>();
        if (documentIds.Count == 0)
        {
            return readers;
        }

        var documents = await db.Documents.AsNoTracking()
            .Where(d => documentIds.Contains(d.Id))
            .ToListAsync(ct);
        var currentFiles = (await (
                from version in db.DocumentVersions.AsNoTracking()
                join file in db.StoredFiles.AsNoTracking() on version.Id equals file.DocumentVersionId
                where documentIds.Contains(version.DocumentId) && version.IsCurrent
                orderby file.CreatedAt, file.Id
                select new { version.DocumentId, File = file })
            .ToListAsync(ct))
            .GroupBy(x => x.DocumentId)
            .ToDictionary(g => g.Key, g => g.First().File);
        foreach (var document in documents)
        {
            if (await DocumentAccessRules.CanWriteAsync(
                    db, access, ctx, document, currentFiles.GetValueOrDefault(document.Id), ct))
            {
                readers.Add(document.Id);
            }
        }

        return readers;
    }

    /// <summary>
    /// Member DTOs from already-resolved displays. The anchor travels under the
    /// anchor-presentation rule: an unreadable target takes payload, pin and state with
    /// it — a payload can quote what it anchors to, and that a document was re-versioned
    /// is a fact about the document — while a superseded pin is disclosed as degraded to
    /// every reader but its file id is handed only to callers the document's own rules
    /// let into superseded versions, so the marker never routes a read-only caller into
    /// history their document read would refuse.
    /// </summary>
    private static async Task<Dictionary<Guid, ResLinkMemberDto>> AssembleMembersAsync(
        SilexGisDbContext db,
        IAccessService access,
        AccessContext ctx,
        IReadOnlyList<ResLinkMember> members,
        IReadOnlyDictionary<Guid, ResLinkTargetDisplayDto> displays,
        CancellationToken ct)
    {
        // Only readable members' pins are even asked about: for an unreadable target
        // everything is withheld anyway, and its version chain must not be queried on a
        // path whose timing could differ.
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

        // Of the documents behind stale pins, those whose superseded versions this
        // caller may see. Stale pins are rare, so the per-document walk is bounded by
        // their count, not the page's.
        var staleDocumentIds = members
            .Where(m => m.EntityType == AttachedEntityType.Document && m.EntityId is not null
                && m.AnchorFileId is { } pin && stalePins.Contains(pin)
                && displays.ContainsKey(m.Id))
            .Select(m => m.EntityId!.Value)
            .Distinct()
            .ToList();
        var versionReaders = await VersionReaderDocumentIdsAsync(db, access, ctx, staleDocumentIds, ct);

        return members.ToDictionary(m => m.Id, m =>
        {
            var display = displays.GetValueOrDefault(m.Id);
            var facing = ResLinkRules.PresentAnchor(
                targetReadable: display is not null,
                pinSuperseded: m.AnchorFileId is { } pin && stalePins.Contains(pin),
                supersededVersionsReadable: m.EntityId is { } documentId
                    && versionReaders.Contains(documentId));
            return new ResLinkMemberDto(
                m.Id,
                ResLinkTargets.NameOf(m.FeatureId, m.EntityType),
                m.FeatureId ?? m.EntityId!.Value,
                m.IsMain,
                m.SortOrder,
                m.Note,
                m.AnchorKind,
                facing.PayloadShown ? ParseAnchor(m.Anchor) : null,
                facing.PinShown ? m.AnchorFileId : null,
                facing.DegradationShown ? ResLinkAnchorState.Degraded : ResLinkAnchorState.Exact,
                display);
        });
    }
}
