// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Domain.ResLinks;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.PlaceStations;

/// <summary>One place a phone mapped to a station of a survey, as the mappings are read.</summary>
public sealed class PlaceStationRow
{
    /// <summary>The mapping's identifier: the link's, which is the one the phone gave it.</summary>
    public Guid Id { get; init; }

    public Guid PlaceId { get; init; }

    /// <summary>The survey model the station is in, when the mapping names a model.</summary>
    public Guid? SurveyModelId { get; init; }

    /// <summary>The survey file the station is in, when the mapping names a document instead.</summary>
    public Guid? DocumentId { get; init; }

    /// <summary>The survey member's payload: the station and, for a splay end, its offset.</summary>
    public string? Anchor { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>The mapping's revision, which a phone arbitrates its edits on.</summary>
    public DateTimeOffset UpdatedAt { get; init; }

    public DateTimeOffset? DeletedAt { get; init; }
}

/// <summary>
/// Where the places phones mapped to survey stations are read: the place-at-station links, each with
/// its place, the survey it names and the station. The survey-models stream, the review screens, the
/// held lines and the browser's disclosure all read mappings through this, so what a mapping is —
/// which link, which two members — is said once.
/// </summary>
/// <remarks>
/// A mapping is a link of the place-at-station relation with the place as its main member and one
/// survey member — a survey model or a document — anchored at the station. A link missing either
/// end is no mapping. Removed mappings are not filtered: the moment one went is what a phone is
/// told, so every read that wants only live mappings says so itself.
/// </remarks>
public static class PlaceStationQueries
{
    public static IQueryable<PlaceStationRow> Rows(SilexGisDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        return
            from link in db.ResLinks
            join relation in db.ResLinkRelationTypes on link.RelationTypeId equals (long?)relation.Id
            where relation.Code == ResLinkRelationTypeSeeds.PlaceAtStationCode
            from place in db.ResLinkMembers.Where(m => m.ResLinkId == link.Id && m.IsMain && m.FeatureId != null)
            from survey in db.ResLinkMembers.Where(m => m.ResLinkId == link.Id && !m.IsMain
                && (m.EntityType == AttachedEntityType.SurveyModel || m.EntityType == AttachedEntityType.Document))
            select new PlaceStationRow
            {
                Id = link.Id,
                PlaceId = place.FeatureId!.Value,
                SurveyModelId = survey.EntityType == AttachedEntityType.SurveyModel ? survey.EntityId : null,
                DocumentId = survey.EntityType == AttachedEntityType.Document ? survey.EntityId : null,
                Anchor = survey.Anchor,
                CreatedAt = link.CreatedAt,
                UpdatedAt = link.UpdatedAt,
                DeletedAt = link.DeletedAt,
            };
    }

    /// <summary>The place-at-station links among <paramref name="links"/>, whole or husk.</summary>
    public static IQueryable<ResLink> PlaceStations(SilexGisDbContext db, IQueryable<ResLink> links)
    {
        ArgumentNullException.ThrowIfNull(db);
        var relationIds = db.ResLinkRelationTypes
            .Where(r => r.Code == ResLinkRelationTypeSeeds.PlaceAtStationCode)
            .Select(r => (long?)r.Id);
        return links.Where(l => relationIds.Contains(l.RelationTypeId));
    }

    /// <summary>The place-at-station relation's row id, or null before it is seeded.</summary>
    public static Task<long?> RelationIdAsync(SilexGisDbContext db, CancellationToken ct = default) =>
        db.ResLinkRelationTypes.AsNoTracking()
            .Where(r => r.Code == ResLinkRelationTypeSeeds.PlaceAtStationCode)
            .Select(r => (long?)r.Id)
            .FirstOrDefaultAsync(ct);
}
