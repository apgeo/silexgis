// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Domain.ResLinks;

namespace SilexGis.Infrastructure.Persistence;

/// <summary>What one attach did, or why it did nothing.</summary>
public enum TripMomentPictureOutcome
{
    /// <summary>A membership was queued for this unit of work.</summary>
    Attached = 0,

    /// <summary>This photograph is already on this moment; nothing was queued.</summary>
    AlreadyThere = 1,

    /// <summary>The shipped relation row is missing — an installation that was never seeded.</summary>
    RelationMissing = 2,
}

/// <summary>
/// What one attach did, and the membership it queued. <paramref name="MemberId"/> is the row a
/// later detach names, and is meaningless unless the outcome is
/// <see cref="TripMomentPictureOutcome.Attached"/>.
/// </summary>
public readonly record struct TripMomentPictureResult(TripMomentPictureOutcome Outcome, Guid MemberId);

/// <summary>
/// Photographs hung on the moments of a tracked trip, read and written through the general link
/// mechanism.
/// </summary>
/// <remarks>
/// <para>
/// <b>The attachment hangs on the trip at an instant, never on the report row, and that is the
/// whole design.</b> A tracking log is append-only and a correction is a deletion followed by a
/// fresh report with a new id — <c>DeleteEventAsync</c> removes the row outright and there is no
/// update route at all. So a picture keyed to a report is destroyed the first time somebody fixes a
/// typo in a time, silently, by an act nobody thinks of as destructive. What a link stores instead
/// is <c>(this trip, 14:05)</c>, and neither of those identifiers moves when a report is deleted
/// and re-entered: the picture survives the correction untouched, and the replay folds the
/// corrected log to place it again.
/// </para>
/// <para>
/// <b>The honest cost, which is not hidden.</b> Correcting a report's <em>time</em> does not drag
/// the pictures near it along. A picture at 14:05 stays at 14:05 and will fold against whatever the
/// corrected log says was in force then. That is the right default — the moment is a fact about the
/// camera, not about the report — and moving one is a deliberate edit of its anchor.
/// </para>
/// <para>
/// <b>No table of its own, and one anchor kind rather than one.</b> A member already says which
/// part of its target it means; what the vocabulary lacked was a word for a part of a trip that is
/// an instant. Everything else a picture-per-moment table would have had to grow — the target, the
/// note, the sort order, the audit rooting, the delete semantics, a resolver, a visibility rule —
/// a membership already has, decided in one place for every kind of link.
/// </para>
/// <para>
/// The shape written here is one link per <c>(trip, moment, subject)</c>: the trip anchored to the
/// instant as the <b>main</b> member, the photographs as document members, and optionally the caver
/// the moment is about. Main on the trip side because the relation is <c>documents</c> — directed,
/// so exactly one main member is required once there are two — and because the curation rule
/// ("whoever may write the link's main member may edit the link") then resolves to trip-write
/// without a new permission rule being invented for this.
/// </para>
/// <para>
/// Nothing is saved here: the memberships join the caller's own unit of work, so a failure later in
/// a bulk write takes the whole batch with it rather than leaving half a memory card attached.
/// </para>
/// </remarks>
public sealed class TripMomentPictures(SilexGisDbContext db)
{
    /// <summary>The shipped relation this reads out of: "this moment of the trip is documented
    /// by this photograph".</summary>
    public const string RelationCode = "documents";

    /// <summary>Links opened earlier in this same unit of work, by the moment they are for —
    /// they have no row to find yet, and a bulk attach of one memory card lands thirty pictures
    /// on a handful of moments.</summary>
    private readonly Dictionary<(DateTimeOffset At, Guid? CaverId), Guid> opened = [];

    /// <summary>Member counts this unit of work has already decided, against the same bound the
    /// general link write is held to.</summary>
    private readonly Dictionary<Guid, int> counts = [];

    /// <summary>
    /// Hangs one photograph on one moment of one trip, extending the link that already holds that
    /// moment or opening one when there is none. Extending is what somebody doing it by hand would
    /// do: a second link saying the same thing about the same instant says nothing the first did
    /// not, and it would make the same picture appear twice in the strip.
    /// </summary>
    public async Task<TripMomentPictureResult> AttachAsync(
        Guid tripLogId,
        Guid documentId,
        DateTimeOffset at,
        Guid? caverId,
        string? caption,
        Guid? addedBy,
        CancellationToken ct)
    {
        var relationId = await RelationIdAsync(ct);
        if (relationId is null)
        {
            return new(TripMomentPictureOutcome.RelationMissing, Guid.Empty);
        }

        var hosts = await HostLinkIdsAsync(tripLogId, relationId.Value, at, caverId, ct);
        foreach (var host in hosts)
        {
            if (await HoldsDocumentAsync(host, documentId, ct))
            {
                return new(TripMomentPictureOutcome.AlreadyThere, Guid.Empty);
            }
        }

        // The first host with room. A link that has reached the shared member bound is not pushed
        // past it — the moment is the union of its links, so the next picture opens another rather
        // than building an object no other surface would accept or could later edit.
        Guid? target = null;
        foreach (var host in hosts)
        {
            if (ResLinkRules.MayAddMember(await MemberCountAsync(host, ct)))
            {
                target = host;
                break;
            }
        }

        if (target is null)
        {
            var link = new ResLink
            {
                ShortCode = ResLinkRules.NewShortCode(),
                RelationTypeId = relationId,
                CreatedBy = addedBy,
            };
            db.ResLinks.Add(link);
            db.ResLinkMembers.Add(new ResLinkMember
            {
                ResLinkId = link.Id,
                EntityType = AttachedEntityType.TripLog,
                EntityId = tripLogId,
                IsMain = true,
                SortOrder = 0,
                AnchorKind = AnchorKind.TripMoment,
                Anchor = TripMomentAnchor.Payload(at),
                AddedBy = addedBy,
            });
            counts[link.Id] = 1;
            if (caverId is { } subject)
            {
                // Who the moment is about, so the replay knows whose position to place the picture
                // at. A picture with no caver is timeline-only and is never placed: a party that
                // has split is in two places, and guessing "the party's station" would put a
                // photograph somewhere nobody was.
                db.ResLinkMembers.Add(new ResLinkMember
                {
                    ResLinkId = link.Id,
                    EntityType = AttachedEntityType.Caver,
                    EntityId = subject,
                    SortOrder = 1,
                    AddedBy = addedBy,
                });
                counts[link.Id] = 2;
            }

            // Only the newest link of a moment is offered to the next picture; the full ones stay
            // readable and are simply never extended again.
            opened[(at, caverId)] = link.Id;
            target = link.Id;
        }

        var count = await MemberCountAsync(target.Value, ct);
        var picture = new ResLinkMember
        {
            ResLinkId = target.Value,
            EntityType = AttachedEntityType.Document,
            EntityId = documentId,
            SortOrder = count,
            Note = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim(),
            AddedBy = addedBy,
        };
        db.ResLinkMembers.Add(picture);
        counts[target.Value] = count + 1;
        return new(TripMomentPictureOutcome.Attached, picture.Id);
    }

    /// <summary>
    /// Takes one photograph off a moment of this trip, and takes the link with it once nothing is
    /// left to relate — a link holding only the trip and a caver is an association that says
    /// nothing and that no surface offers.
    /// </summary>
    /// <remarks>
    /// <b>Only a link this feature itself wrote is touched, and the check is a permission check
    /// rather than tidiness.</b> Curation of a link belongs to whoever may write its <em>main</em>
    /// member, and this route's only gate is write on the trip. The general link route lets any
    /// caller who may read two things relate them and choose which is main — so somebody who may
    /// write a document can author a <c>documents</c> link with the document as its main member and
    /// a moment of this trip beside it, and that link is curated by the document's writers and not
    /// by the trip's. Recognising it by the moment member alone would let this route delete it.
    /// What is demanded instead is the exact shape the attach above writes: the documenting
    /// relation, with the trip's moment as the main member — the shape whose curation really does
    /// resolve to trip-write. Anything else is answered as not found, like every other thing this
    /// route does not own.
    /// </remarks>
    /// <returns>False when the member is not a photograph on a moment of this trip: missing,
    /// belonging to another trip, not the document half of the link, or hung on a link whose
    /// subject is not this trip's moment. One answer for all of them, so the route cannot be used
    /// to probe.</returns>
    public async Task<bool> DetachAsync(Guid tripLogId, Guid memberId, CancellationToken ct)
    {
        var member = await db.ResLinkMembers.FirstOrDefaultAsync(m => m.Id == memberId, ct);
        if (member is null || member.EntityType != AttachedEntityType.Document)
        {
            return false;
        }

        var relationId = await RelationIdAsync(ct);
        var link = await db.ResLinks.FirstOrDefaultAsync(l => l.Id == member.ResLinkId, ct);
        if (relationId is null || link is null || link.RelationTypeId != relationId)
        {
            return false;
        }

        var siblings = await db.ResLinkMembers
            .Where(m => m.ResLinkId == member.ResLinkId)
            .ToListAsync(ct);
        var moment = siblings.Find(m =>
            m.EntityType == AttachedEntityType.TripLog
            && m.EntityId == tripLogId
            && m.AnchorKind == AnchorKind.TripMoment
            && m.IsMain);
        if (moment is null)
        {
            return false;
        }

        db.ResLinkMembers.Remove(member);

        // What would be left: the moment, the caver, and nothing that is being documented.
        var remaining = siblings
            .Where(m => m.Id != member.Id && m.EntityType == AttachedEntityType.Document)
            .ToList();
        if (remaining.Count == 0)
        {
            foreach (var sibling in siblings.Where(m => m.Id != member.Id))
            {
                db.ResLinkMembers.Remove(sibling);
            }

            db.ResLinks.Remove(link);
        }

        return true;
    }

    /// <summary>
    /// Every link of this trip that hangs pictures on the given moment and subject, newest first —
    /// the ones an attach may extend. Instants are compared as moments rather than as text: two
    /// spellings of one instant are one moment, and a host chosen by string equality would open a
    /// second link for the same 14:05 written with a different offset.
    /// </summary>
    /// <remarks>
    /// The moment has to be the link's <b>main</b> member, and for the reason the detach above
    /// gives: a link somebody authored through the general route with a document as its main
    /// member is curated by that document's writers, and extending one here would be this route
    /// writing into a link trip-write does not steward. Such a link is passed over rather than
    /// joined, and the attach opens one of its own beside it.
    /// </remarks>
    private async Task<List<Guid>> HostLinkIdsAsync(
        Guid tripLogId, long relationId, DateTimeOffset at, Guid? caverId, CancellationToken ct)
    {
        var rows = await (
                from member in db.ResLinkMembers.AsNoTracking()
                where member.EntityType == AttachedEntityType.TripLog
                    && member.EntityId == tripLogId
                    && member.AnchorKind == AnchorKind.TripMoment
                    && member.IsMain
                join link in db.ResLinks.AsNoTracking() on member.ResLinkId equals link.Id
                where link.RelationTypeId == relationId
                select new { link.Id, member.Anchor })
            .ToListAsync(ct);

        var hosts = new List<Guid>();
        foreach (var row in rows)
        {
            if (TripMomentAnchor.Read(row.Anchor) != at)
            {
                continue;
            }

            // The subject has to match too: two pictures of one instant, one of Ana and one of the
            // party as a whole, are two statements and land on two links. Otherwise a picture with
            // no caver would join a link that places it at somebody's station.
            if (await SubjectOfAsync(row.Id, ct) != caverId)
            {
                continue;
            }

            hosts.Add(row.Id);
        }

        if (opened.TryGetValue((at, caverId), out var pending) && !hosts.Contains(pending))
        {
            hosts.Add(pending);
        }

        return hosts;
    }

    /// <summary>The caver a link's moment is about, or null when it is about the party.</summary>
    private async Task<Guid?> SubjectOfAsync(Guid linkId, CancellationToken ct)
    {
        var stored = await db.ResLinkMembers.AsNoTracking()
            .Where(m => m.ResLinkId == linkId && m.EntityType == AttachedEntityType.Caver)
            .Select(m => m.EntityId)
            .ToListAsync(ct);
        return stored.Count == 1 ? stored[0] : null;
    }

    private async Task<bool> HoldsDocumentAsync(Guid linkId, Guid documentId, CancellationToken ct)
    {
        var stored = await db.ResLinkMembers.AsNoTracking().AnyAsync(
            m => m.ResLinkId == linkId
                && m.EntityType == AttachedEntityType.Document
                && m.EntityId == documentId,
            ct);
        if (stored)
        {
            return true;
        }

        return db.ChangeTracker.Entries<ResLinkMember>().Any(e =>
            e.State == EntityState.Added
            && e.Entity.ResLinkId == linkId
            && e.Entity.EntityType == AttachedEntityType.Document
            && e.Entity.EntityId == documentId);
    }

    /// <summary>How many members a link will have once this unit of work is saved.</summary>
    private async Task<int> MemberCountAsync(Guid linkId, CancellationToken ct)
    {
        if (counts.TryGetValue(linkId, out var known))
        {
            return known;
        }

        var stored = await db.ResLinkMembers.AsNoTracking().CountAsync(m => m.ResLinkId == linkId, ct);
        counts[linkId] = stored;
        return stored;
    }

    private Task<long?> RelationIdAsync(CancellationToken ct) =>
        db.ResLinkRelationTypes.AsNoTracking()
            .Where(r => r.Code == RelationCode)
            .Select(r => (long?)r.Id)
            .FirstOrDefaultAsync(ct);
}
