// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Geodata;

/// <summary>
/// Who a map statement answers for, as the two SQL fragments it splices in: which feature rows the
/// reader may see at all, and which of those they may place exactly. Built over one parameter set,
/// as the access fragments always are, so the two cannot disagree about the reader.
/// </summary>
public sealed record FeatureAudience(string VisibleSql, string ExactSql, DynamicParameters Parameters);

/// <summary>
/// The two audiences a map answers: a signed-in caller, and somebody holding a shared view's link.
/// </summary>
/// <remarks>
/// <para>
/// <b>The link holder is the least-privileged reader there is.</b> They are given what every
/// signed-in account holds and no account holds less of — no personal grant, no caving group,
/// nothing owned — and then narrowed twice more. They read only rows that are <i>themselves</i>
/// at the widest visibility level, <see cref="Visibility.Public"/>: a generic feature by its own
/// level, an entrance by its cave's own level (an entrance has no audience of its own; it is the
/// cave's door). The ordinary cascade — a row is readable when it or any feature containing it
/// admits the reader — is deliberately not used here: through it a private cave filed inside a
/// public karst area reads as public, which is right for accounts that may read the area's
/// contents and wrong for a URL anybody can be sent. A view shared from an account that reads
/// more cannot hand the link what that account reads. And they are given no protected row at
/// all, whatever the installation grants every account: not snapped to the protection grid, not
/// counted in a cluster, not in any window — the link reads as if the row did not exist. An
/// approximate position is not protection: a snapped point still says that a cave is there and
/// roughly where, and a URL anybody can be sent is the last place to say it. That is stricter
/// than the least-privileged account, which an installation can give exact view of a protected
/// area — the link leaves the installation, and nothing an administrator granted to accounts
/// reaches it.
/// </para>
/// <para>
/// A rule that denies something to every account still denies it here: the visibility fragment
/// is the ordinary walk for that context, and the public narrowing only removes rows from it.
/// </para>
/// </remarks>
public static class FeatureAudienceSql
{
    /// <summary>A signed-in caller: their own visibility walk and their own exact-view decision.</summary>
    public static FeatureAudience Caller(AccessContext ctx, string alias)
    {
        var (visibleSql, exactSql, parameters) = AccessSql.FeatureLayerFragments(ctx, alias);
        return new FeatureAudience(visibleSql, exactSql, parameters);
    }

    /// <summary>
    /// A shared view's link holder: what <paramref name="anyAccount"/> may read, narrowed to public
    /// rows that nothing protects.
    /// </summary>
    /// <param name="anyAccount">The context every signed-in account has — no account in particular.</param>
    public static FeatureAudience SharedLink(AccessContext anyAccount, string alias)
    {
        var (visibleSql, _, parameters) = AccessSql.FeatureLayerFragments(anyAccount, alias);
        return new FeatureAudience(
            $"""
            ({visibleSql}
             AND CASE WHEN {alias}.kind = {(short)FeatureKind.CaveEntrance}
                      THEN EXISTS (SELECT 1 FROM cave_entrances ce
                                   JOIN features cave ON cave.id = ce.cave_feature_id
                                   WHERE ce.id = {alias}.id
                                     AND cave.deleted_at IS NULL
                                     AND cave.visibility = {(short)Visibility.Public})
                      ELSE {alias}.visibility = {(short)Visibility.Public}
                 END
             AND NOT {alias}.is_protected_effective)
            """,
            // A protected row is not visible to the link at all (above), so every row the link
            // reads is placed exactly. The flag is the row's own effective-protection fact — some
            // row of its ancestry, itself included and soft-deleted ones still counting, is a
            // protection root — kept with the row by every write and checked by the integrity
            // verifier. No grant, owner arm or administrator arm reaches a link, so there is no
            // walk to make; walking the roots instead cost an index probe per candidate, which over
            // a whole view's reach was a third of the statement. Stated here once more rather than
            // as a bare TRUE so that a statement which ever met a protected row through this
            // audience would snap it, never place it.
            $"(NOT {alias}.is_protected_effective)",
            parameters);
    }

    /// <summary>
    /// The rows a shared view's link holder may read, as a query: the EF form of
    /// <see cref="SharedLink"/>'s visibility fragment — an entrance by its cave's own level, every
    /// other row by its own, and no protected row at all.
    /// </summary>
    public static IQueryable<Feature> SharedLinkReadable(SilexGisDbContext db, AccessContext anyAccount) =>
        db.Features.AsQueryable()
            .VisibleTo(anyAccount, db.Features, db.FeatureSetMembers)
            .Where(f => !f.IsProtectedEffective)
            .Where(f => (f.Kind != FeatureKind.CaveEntrance && f.Visibility == Visibility.Public)
                || (f.Kind == FeatureKind.CaveEntrance
                    && db.CaveEntrances.Any(e => e.Id == f.Id
                        && db.Features.Any(cave => cave.Id == e.CaveFeatureId && cave.Visibility == Visibility.Public))));
}
