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
    // ---- shared pieces --------------------------------------------------------------

    /// <summary>The links that stand — read through their one home.</summary>
    private static IQueryable<ResLink> Standing(SilexGisDbContext db) => ResLinkStanding.Of(db);

    /// <summary>
    /// Whether this caller may edit or delete one link, at write time. The rule itself
    /// lives in <see cref="CuratedLinkIdsAsync"/> and is asked here for a set of one: what
    /// this method adds is the lock-time re-read of the main marker, which a write must do
    /// for itself because the marker can move under a concurrent promotion.
    /// </summary>
    private static async Task<bool> MayEditAsync(
        SilexGisDbContext db,
        IAccessService access,
        ResLinkTargetDirectory targets,
        AccessContext ctx,
        ResLink link,
        CancellationToken ct)
    {
        // One row through the partial unique index that already makes "at most one main
        // per link" a schema fact. Every caller asks this after taking the link's row
        // lock, so the marker it reads is the one the write will act on — which is the
        // whole reason a write does not simply reuse a marker somebody else read earlier.
        var main = await db.ResLinkMembers.AsNoTracking()
            .Where(m => m.ResLinkId == link.Id && m.IsMain)
            .FirstOrDefaultAsync(ct);
        var curated = await CuratedLinkIdsAsync(
            db, access, targets, ctx, [link], main is null ? [] : [main], ct);
        return curated.Contains(link.Id);
    }

    /// <summary>
    /// Refuses a write that would leave a link saying "this moment of this trip is documented
    /// by…" unless the caller may write the trip.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one place this route asks for more than Read on what a link names. Everywhere else a
    /// link is its author's assertion, shown as such to whoever can read both ends. A link of
    /// this shape is not shown as an assertion: the trip's replay draws its photographs at that
    /// moment as the trip's own, and an installation may publish that replay to people without an
    /// account, with a photograph about one person shown under that person's place in the party.
    /// Left at the ordinary floor, any account that can read a trip — on a trip visible to every
    /// account, any account at all — could decide what a public page shows of it and of whom.
    /// </para>
    /// <para>
    /// Asked about the state a write would leave behind rather than about the member it names, so
    /// that the shape cannot be reached in two steps: a link made about something else and its
    /// marker then moved onto the trip, a relation changed afterwards, a photograph or a person
    /// added to a link that already has the shape. Removing a whole link is not asked — it can
    /// only take a statement back.
    /// </para>
    /// <para>
    /// Called after the read floor, so a trip the caller may not read has already been answered
    /// as missing and the refusal here tells them only what the trip's own page does.
    /// </para>
    /// </remarks>
    /// <param name="resultingMain">The member that will be main once the write lands, if any.</param>
    private static async Task<ProblemHttpResult?> TripMomentProblemAsync(
        ResLinkTargetDirectory targets,
        AccessContext ctx,
        ResLinkRelationType? relation,
        ResLinkMember? resultingMain,
        CancellationToken ct)
    {
        if (!TripMomentPictures.IsStatedBy(relation?.Code, resultingMain))
        {
            return null;
        }

        return await targets.CanWriteAsync(ctx, AttachedEntityType.TripLog, resultingMain!.EntityId!.Value, ct)
            ? null
            : ApiProblems.Forbidden(
                detail: "Saying what documents a moment of a trip takes the right to edit that trip.");
    }

    /// <summary>Whether a link is a place pinned on a phone's map.</summary>
    internal static async Task<bool> IsPlacePointAsync(SilexGisDbContext db, ResLink link, CancellationToken ct) =>
        link.RelationTypeId is { } relationId
        && relationId == await PlacePointQueries.RelationIdAsync(db, ct);

    /// <summary>
    /// The refusal a generic edit of a place pin gets: its members and its relation are what the
    /// pin is, and they are written only by the place-pin store.
    /// </summary>
    private static async Task<ProblemHttpResult?> FixedPlacePointAsync(
        SilexGisDbContext db, ResLink link, CancellationToken ct) =>
        await IsPlacePointAsync(db, link, ct)
            ? ApiProblems.Conflict(PlacePointFixedCode, PlacePointFixedDetail)
            : null;

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
    internal static async Task SaveWithFreshShortCodeAsync(
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
}
