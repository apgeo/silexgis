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
    private static async Task<Results<Created<ResLinkMemberDto>, UnauthorizedHttpResult, ProblemHttpResult>> AddMemberAsync(
        Guid id,
        ResLinkMemberAddRequest request,
        SilexGisDbContext db,
        ResLinkTargetDirectory targets,
        PlacePointDisclosure placePoints,
        PlaceStationDisclosure placeStations,
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

        var link = await Standing(db).AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);
        if (link is null || await HiddenWholeAsync(placePoints, placeStations, ctx, link.Id, ct))
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (await FixedLinkAsync(db, link, ct) is { } fixedProblem)
        {
            return fixedProblem;
        }

        // The whole act — the edit decision, the optional new feature, the marker handover
        // and the insert — is one transaction, opened by taking the link's row lock so the
        // snapshot every rule below runs against cannot be moved by a concurrent writer.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockForMemberWriteAsync(db, link.Id, ct);

        // Inside the lock, for the reason the update handler above spells out: the rule
        // reads the main marker, and the marker moves.
        if (!await MayEditAsync(db, access, targets, ctx, link, ct))
        {
            return ApiProblems.Forbidden();
        }

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
        // the audience is the people who curate this link — its creator, a full
        // administrator who reads every member anyway, and whoever may write the main
        // member the link is about — and the honest alternatives are worse: a
        // disclosure-cut snapshot would drive the insert into the unique backstop
        // index, and answering "created" for a row that already exists would either
        // misstate its fields or overwrite another author's. The count-based rules
        // above share the same snapshot and the same bounded audience: a curator can
        // infer that hidden members exist, never which targets they name.
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

        // Whichever member ends up main: the arriving one, the one that was, or the one the
        // marker is handed back to. Asked for every arrival and not only for one that claims the
        // marker, because a photograph or a person added to a link already about a trip's moment
        // changes what is said of that moment just as much.
        if (await TripMomentProblemAsync(
                targets, ctx, relation, request.IsMain ? member : currentMain ?? incumbent, ct)
            is { } momentProblem)
        {
            return momentProblem;
        }

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
        PlacePointDisclosure placePoints,
        PlaceStationDisclosure placeStations,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var link = await Standing(db).AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);
        if (link is null || await HiddenWholeAsync(placePoints, placeStations, ctx, link.Id, ct))
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (await FixedLinkAsync(db, link, ct) is { } fixedProblem)
        {
            return fixedProblem;
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockForMemberWriteAsync(db, link.Id, ct);

        // Inside the lock, for the reason the update handler above spells out: the rule
        // reads the main marker, and the marker moves.
        if (!await MayEditAsync(db, access, targets, ctx, link, ct))
        {
            return ApiProblems.Forbidden();
        }

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

        var resultingMain = request.IsMain
            ? member
            : currentMain is not null && currentMain.Id != member.Id ? currentMain : null;
        if (await TripMomentProblemAsync(targets, ctx, relation, resultingMain, ct) is { } momentProblem)
        {
            return momentProblem;
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
        ResLinkTargetDirectory targets,
        PlacePointDisclosure placePoints,
        PlaceStationDisclosure placeStations,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var link = await Standing(db).AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);
        if (link is null || await HiddenWholeAsync(placePoints, placeStations, ctx, link.Id, ct))
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (await FixedLinkAsync(db, link, ct) is { } fixedProblem)
        {
            return fixedProblem;
        }

        // Locked first: the one-member floor and the directed-main successor rule count
        // a snapshot, and two concurrent removals passing the same count could otherwise
        // leave a zero-member link — a state nothing repairs.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockForMemberWriteAsync(db, link.Id, ct);

        // Inside the lock, for the reason the update handler above spells out: the rule
        // reads the main marker, and the marker moves.
        if (!await MayEditAsync(db, access, targets, ctx, link, ct))
        {
            return ApiProblems.Forbidden();
        }

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

        var relation = await RelationAsync(db, link.RelationTypeId, ct);
        var directed = relation?.Directed ?? false;
        var remaining = members.Count - 1;
        if (member.IsMain && directed && remaining >= 2)
        {
            return ApiProblems.BadRequest(
                ResLinkRules.MainRequiredCode, "Promote another member before removing the main one.");
        }

        // What is left is still read as a statement about the trip's moment when its main member
        // stays — and taking away one of two people a picture was about leaves it about the other.
        var survivingMain = remaining >= 2 ? members.FirstOrDefault(m => m.IsMain && m.Id != member.Id) : null;
        if (await TripMomentProblemAsync(targets, ctx, relation, survivingMain, ct) is { } momentProblem)
        {
            return momentProblem;
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
}
