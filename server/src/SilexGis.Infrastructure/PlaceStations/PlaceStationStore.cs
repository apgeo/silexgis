// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Domain.ResLinks;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.PlaceStations;

/// <summary>One place-to-station mapping as its writer holds it: the link and its two members.</summary>
public sealed class PlaceStation
{
    public required ResLink Link { get; init; }

    /// <summary>The place mapped; the link's main member.</summary>
    public required ResLinkMember Place { get; init; }

    /// <summary>The survey model or survey file, anchored at the station.</summary>
    public required ResLinkMember Survey { get; init; }

    public Guid PlaceId => Place.FeatureId!.Value;

    /// <summary>The station and splay the mapping carries.</summary>
    public PlaceStationAnchor.Parsed? Station => PlaceStationAnchor.Read(Survey.Anchor);
}

/// <summary>
/// The one writer of the places phones mapped to survey stations. A mapping is a link of the
/// place-at-station relation, and nothing else creates one or changes what it says: the upload
/// section and review acceptance come here, and the browser's delete. Tracked, not saved.
/// </summary>
/// <remarks>
/// <para>
/// The mapping keeps the identifier the phone gave it, as the link's. Its revision — what a phone
/// arbitrates an edit on — is the link's own write time, so every change a phone must hear of (a
/// station, a splay, a removal, a return) moves it.
/// </para>
/// <para>
/// One live mapping per place in one survey cannot be an index, because the pair spans two member
/// rows. Every write that could break it takes the place's row lock first and asks after the lock.
/// </para>
/// <para>
/// Provisional, like the pins on phones' maps: the pin, marker and map sync is to be analysed again
/// as a whole, and this is kept small so it can be reshaped then.
/// </para>
/// </remarks>
public sealed class PlaceStationStore(SilexGisDbContext db)
{
    /// <summary>Takes the place's row lock for the rest of the surrounding transaction.</summary>
    public Task LockAsync(Guid placeId, CancellationToken ct = default) => PlaceStationSql.LockPlaceAsync(db, placeId, ct);

    /// <summary>Whether another live mapping already maps the place in the survey.</summary>
    public Task<bool> LiveMappingOfAsync(
        Guid placeId, Guid? surveyModelId, Guid? documentId, Guid except, CancellationToken ct = default) =>
        PlaceStationQueries.Rows(db).AsNoTracking()
            .AnyAsync(
                r => r.PlaceId == placeId
                    && r.SurveyModelId == surveyModelId
                    && r.DocumentId == documentId
                    && r.DeletedAt == null
                    && r.Id != except,
                ct);

    /// <summary>
    /// A new mapping under <paramref name="id"/>: the place, and the survey model or survey file at
    /// the station. Exactly one of <paramref name="surveyModelId"/> and <paramref name="documentId"/>.
    /// The caller has asked whether the place is free in that survey.
    /// </summary>
    public async Task<ResLink> AddAsync(
        Guid id,
        Guid placeId,
        Guid? surveyModelId,
        Guid? documentId,
        string station,
        IReadOnlyList<double>? splay,
        Guid? by,
        CancellationToken ct = default)
    {
        if ((surveyModelId is null) == (documentId is null))
        {
            throw new ArgumentException("A mapping names a survey model or a survey file, exactly one.");
        }

        var relationId = await PlaceStationQueries.RelationIdAsync(db, ct)
            ?? throw new InvalidOperationException("The place-at-station relation is not seeded.");
        var link = new ResLink
        {
            Id = id,
            ShortCode = ResLinkRules.NewShortCode(),
            RelationTypeId = relationId,
            CreatedBy = by,
        };
        db.ResLinks.Add(link);
        db.ResLinkMembers.Add(new ResLinkMember
        {
            ResLinkId = id,
            FeatureId = placeId,
            IsMain = true,
            SortOrder = 0,
            AddedBy = by,
        });
        db.ResLinkMembers.Add(new ResLinkMember
        {
            ResLinkId = id,
            EntityType = surveyModelId is null ? AttachedEntityType.Document : AttachedEntityType.SurveyModel,
            EntityId = surveyModelId ?? documentId,
            AnchorKind = AnchorKind.ModelStation,
            Anchor = PlaceStationAnchor.Write(station, splay),
            SortOrder = 1,
            AddedBy = by,
        });
        return link;
    }

    /// <summary>The mapping under this identifier, tracked, removed or not; null when no mapping has it.</summary>
    public async Task<PlaceStation?> FindAsync(Guid id, CancellationToken ct = default)
    {
        if (!await PlaceStationQueries.Rows(db).AsNoTracking().AnyAsync(r => r.Id == id, ct))
        {
            return null;
        }

        var link = await db.ResLinks.FirstAsync(l => l.Id == id, ct);
        var members = await db.ResLinkMembers.Where(m => m.ResLinkId == id).ToListAsync(ct);
        return new PlaceStation
        {
            Link = link,
            Place = members.First(m => m.IsMain && m.FeatureId != null),
            Survey = members.First(m => !m.IsMain
                && m.EntityType is AttachedEntityType.SurveyModel or AttachedEntityType.Document),
        };
    }

    /// <summary>The mapping now names another station, or another splay end.</summary>
    public void SetStation(PlaceStation mapping, string station, IReadOnlyList<double>? splay)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        mapping.Survey.Anchor = PlaceStationAnchor.Write(station, splay);
        Changed(mapping);
    }

    /// <summary>The mapping removed — stamped, so the phones are told.</summary>
    public void Remove(PlaceStation mapping, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        if (mapping.Link.DeletedAt is null)
        {
            mapping.Link.DeletedAt = at;
            Changed(mapping);
        }
    }

    /// <summary>A removed mapping back under its own identifier, so the phones receive it again.</summary>
    public void Restore(PlaceStation mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        mapping.Link.DeletedAt = null;
        Changed(mapping);
    }

    // The link row itself is written on every change a phone must hear of, so its write time — the
    // mapping's revision — moves even when only a member changed.
    private void Changed(PlaceStation mapping) => db.Entry(mapping.Link).Property(l => l.UpdatedAt).IsModified = true;
}
