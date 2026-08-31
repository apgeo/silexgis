// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Sync;

/// <summary>
/// The lookup kinds an upload needs, read once per batch and answered by stable code.
/// </summary>
/// <remarks>
/// Never by numeric identifier. The lookup tables carry identity keys handed out by whichever
/// installation seeded them, in whatever order that installation's rows were inserted, so the
/// number standing for "cave" on one server stands for something else on the next — and the same
/// phone is expected to talk to more than one. The code is the only part of a kind that means the
/// same thing on both sides, which is why it is what travels.
/// </remarks>
/// <param name="KindsRequiringParent">
/// The generic kinds that are meaningless outside a containing row, by identifier. Whether a row
/// needs a container is a property of its kind and is read from the taxonomy, never assumed by
/// this channel: the kinds a selection is rooted in — a surface area, which caves hang under —
/// sit at the top of the tree, and a channel that demanded a container for every row would refuse
/// them for ever and strand everything allocated beneath one.
/// </param>
public sealed record SyncTaxonomies(
    IReadOnlyDictionary<string, long> CaveTypes,
    IReadOnlyDictionary<string, long> EntranceTypes,
    IReadOnlyDictionary<string, long> FeatureTypes,
    IReadOnlySet<long> KindsRequiringParent)
{
    public static async Task<SyncTaxonomies> LoadAsync(SilexGisDbContext db, CancellationToken ct) => new(
        await db.CaveTypes.AsNoTracking()
            .ToDictionaryAsync(t => t.Code, t => t.Id, StringComparer.OrdinalIgnoreCase, ct),
        await db.EntranceTypes.AsNoTracking()
            .ToDictionaryAsync(t => t.Code, t => t.Id, StringComparer.OrdinalIgnoreCase, ct),
        await db.FeatureTypes.AsNoTracking()
            .ToDictionaryAsync(t => t.Code, t => t.Id, StringComparer.OrdinalIgnoreCase, ct),
        (await db.FeatureTypes.AsNoTracking()
            .Where(t => t.RequiresParent)
            .Select(t => t.Id)
            .ToListAsync(ct))
            .ToHashSet());

    public bool TryCaveType(string code, out long id) => CaveTypes.TryGetValue(code, out id);

    public bool TryEntranceType(string code, out long id) => EntranceTypes.TryGetValue(code, out id);

    public bool TryFeatureType(string code, out long id) => FeatureTypes.TryGetValue(code, out id);

    /// <summary>Whether a generic kind only exists inside a containing row.</summary>
    public bool RequiresParent(long featureTypeId) => KindsRequiringParent.Contains(featureTypeId);
}
