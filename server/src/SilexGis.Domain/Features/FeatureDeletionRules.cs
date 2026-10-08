// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Features;

/// <summary>
/// What one deletion of a cave, an entrance or a surface feature took, and when it can be put
/// back.
/// </summary>
/// <remarks>
/// <para>
/// A delete stamps the feature and everything contained in it with one moment, and that moment
/// is the only record of the act: the rows that share it went together, and they come back
/// together. Something inside the same container that had been deleted earlier carries an
/// earlier moment and is a deletion of its own — putting the container back does not put it
/// back, and it is offered separately once its container stands again.
/// </para>
/// <para>
/// <b>Nothing live sits under something deleted.</b> Deleting stamps a whole containment
/// subtree, so that holds of every row the application has ever written, and a great deal reads
/// it as given: who may read a row is inherited from the rows containing it, and a row whose
/// container is hidden would inherit from nothing. So a row is restorable only while nothing
/// above it is deleted. That is one rule and it answers two questions: which deleted rows are a
/// deletion somebody can undo (the top of what an act took — everything else that went with it
/// has the top above it), and what to say of a row deleted before its container was (restore the
/// container first).
/// </para>
/// </remarks>
public static class FeatureDeletionRules
{
    /// <summary>Refusal: the feature is not deleted, so there is nothing to restore.</summary>
    public const string NotDeletedCode = "feature.not_deleted";

    /// <summary>
    /// Refusal: something containing the feature is itself deleted. Restoring the feature alone
    /// would leave it standing inside a container nobody can see; the container is what has to
    /// come back first.
    /// </summary>
    public const string ContainerDeletedCode = "feature.restore_container_deleted";

    /// <summary>
    /// Refusal: a survey line is not put back on its own. It returns with its cave when the two
    /// were deleted together; one deleted by itself was retired, usually because the survey it
    /// was read out of was replaced or removed.
    /// </summary>
    public const string KindNotRestorableCode = "feature.restore_not_supported";

    /// <summary>
    /// Whether a deleted feature of this kind is offered back on its own. A cave, an entrance and
    /// a surface feature are; a survey line is not.
    /// </summary>
    public static bool IsRestorableKind(FeatureKind kind) => kind != FeatureKind.Centerline;

    /// <summary>
    /// The deleted features above this one — every container over every path, the feature itself
    /// left out. Empty means the feature can be restored now.
    /// </summary>
    /// <param name="featureId">The feature asked about.</param>
    /// <param name="ancestorIds">Its ancestor set, which carries the feature itself.</param>
    /// <param name="isDeleted">Whether a feature id names a deleted row.</param>
    public static IReadOnlyList<Guid> DeletedContainers(
        Guid featureId, IEnumerable<Guid> ancestorIds, Func<Guid, bool> isDeleted)
    {
        ArgumentNullException.ThrowIfNull(ancestorIds);
        ArgumentNullException.ThrowIfNull(isDeleted);
        return [.. ancestorIds.Where(id => id != featureId && isDeleted(id)).Distinct()];
    }

    /// <summary>
    /// Of a feature's deleted containers, the ones with no deleted container of their own: what
    /// has to be restored first, before anything beneath it can be.
    /// </summary>
    /// <param name="deletedContainers">The feature's deleted containers.</param>
    /// <param name="ancestorsOf">A container's own ancestor set (itself included or not).</param>
    public static IReadOnlyList<Guid> OutermostDeleted(
        IReadOnlyCollection<Guid> deletedContainers, Func<Guid, IEnumerable<Guid>> ancestorsOf)
    {
        ArgumentNullException.ThrowIfNull(deletedContainers);
        ArgumentNullException.ThrowIfNull(ancestorsOf);
        var deleted = deletedContainers.ToHashSet();
        return [.. deleted.Where(id => !ancestorsOf(id).Any(a => a != id && deleted.Contains(a)))];
    }

    /// <summary>One row a deletion took along with its top.</summary>
    public readonly record struct TakenRow(FeatureKind Kind, DateTimeOffset DeletedAt, IReadOnlyList<Guid> AncestorIds);

    /// <summary>What went with a deleted feature, as a count a list can say.</summary>
    /// <param name="Entrances">Cave entrances.</param>
    /// <param name="Others">Caves and surface features contained in it.</param>
    public readonly record struct Taken(int Entrances, int Others);

    /// <summary>
    /// Counts what one deletion took besides its top: the rows contained in the top that carry
    /// its moment. Survey lines are not counted — they are a cave's own drawing rather than a
    /// thing somebody would look for by name, and one under a protected cave is not to be
    /// disclosed even as a number.
    /// </summary>
    /// <param name="topId">The top of the deletion.</param>
    /// <param name="deletedAt">The moment the deletion stamped.</param>
    /// <param name="rows">The deleted rows the reader may see, the top among them or not.</param>
    public static Taken TakenWith(Guid topId, DateTimeOffset deletedAt, IEnumerable<(Guid Id, TakenRow Row)> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var entrances = 0;
        var others = 0;
        foreach (var (id, row) in rows)
        {
            if (id == topId || row.DeletedAt != deletedAt || !row.AncestorIds.Contains(topId))
            {
                continue;
            }

            switch (row.Kind)
            {
                case FeatureKind.CaveEntrance:
                    entrances++;
                    break;
                case FeatureKind.Cave:
                case FeatureKind.Generic:
                    others++;
                    break;
                default:
                    break;
            }
        }

        return new Taken(entrances, others);
    }
}
