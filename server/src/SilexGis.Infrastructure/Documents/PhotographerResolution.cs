// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Import;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Documents;

/// <summary>
/// Who took a photograph, wherever a photographer is shown or filtered on: the one named on the
/// picture, or — when nobody is — the caver the phone user who added it on a phone is linked to.
/// </summary>
/// <remarks>
/// <para>
/// A name somebody wrote on the picture always wins, a roster caver or a typed name alike: it is
/// a person's statement about this picture, while a phone user is only who held the phone. The
/// fallback is resolved when the photograph is read and never copied onto it, as a trip's people
/// are, so a link made in the browser later applies to every picture already here and an unlink
/// takes it back off them.
/// </para>
/// <para>
/// Stated once, as an expression, so the filter a gallery runs in the database and the answer a
/// listing builds in memory are the same rule and cannot disagree about whose picture it is.
/// </para>
/// </remarks>
public static class PhotographerResolution
{
    /// <summary>Whether the picture names its photographer itself, by caver or by name.</summary>
    private static readonly Expression<Func<PhotoDetails, bool>> NamedExpression =
        p => p.PhotographerCaverId != null || (p.PhotographerName != null && p.PhotographerName != "");

    private static readonly Func<PhotoDetails, bool> IsNamed = NamedExpression.Compile();

    /// <summary>
    /// The caver each photograph's phone user is linked to, for the photographs among
    /// <paramref name="photos"/> that name nobody themselves and whose phone user is linked.
    /// </summary>
    /// <param name="details">The photographs' details by document, as already read; a photograph with none names nobody.</param>
    public static async Task<Dictionary<Guid, Guid>> LinkedCaversAsync(
        SilexGisDbContext db,
        IReadOnlyCollection<Document> photos,
        IReadOnlyDictionary<Guid, PhotoDetails> details,
        CancellationToken ct)
    {
        var unnamed = photos
            .Where(d => d.CreatedByDeviceUserId is not null
                && !(details.TryGetValue(d.Id, out var detail) && IsNamed(detail)))
            .DistinctBy(d => d.Id)
            .ToList();
        if (unnamed.Count == 0)
        {
            return [];
        }

        var links = await DevicePersonMapping.LinksOfAsync(
            db, [.. unnamed.Select(d => d.CreatedByDeviceUserId!.Value).Distinct()], ct);
        return unnamed
            .Where(d => links.ContainsKey(d.CreatedByDeviceUserId!.Value))
            .ToDictionary(d => d.Id, d => links[d.CreatedByDeviceUserId!.Value]);
    }

    /// <summary>
    /// The caver credited with a photograph: the one it names, else the one its phone user is
    /// linked to (<paramref name="linkedCaverId"/>, from <see cref="LinkedCaversAsync"/>) — but
    /// never when it names a photographer by name alone, who is not on the roster.
    /// </summary>
    public static Guid? CaverOf(PhotoDetails? details, Guid? linkedCaverId) =>
        details is not null && IsNamed(details) ? details.PhotographerCaverId : linkedCaverId;

    /// <summary>
    /// The photographs <paramref name="caverId"/> is credited with, by the same rule, as a
    /// condition the database evaluates.
    /// </summary>
    public static Expression<Func<Document, bool>> TakenBy(SilexGisDbContext db, Guid caverId)
    {
        var named = NamedExpression;
        Expression<Func<Document, bool>> taken = d =>
            db.PhotoDetails.Any(p => p.DocumentId == d.Id && p.PhotographerCaverId == caverId)
            || (d.CreatedByDeviceUserId != null
                && !db.PhotoDetails.Where(p => p.DocumentId == d.Id).Any(named)
                && db.DevicePersons.Any(x => x.DeviceUserId == d.CreatedByDeviceUserId && x.CaverId == caverId));
        return taken;
    }
}
