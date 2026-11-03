// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace SilexGis.Api.Common;

/// <summary>
/// What makes a tag, and a tagging, one row: the unique indexes a second writer of the same thing
/// collides with. Every route that coins a tag or applies one matches a lost race here, so a renamed
/// or added identity index is changed in one place and every such route follows it.
/// </summary>
/// <remarks>
/// A tag is unique on both its slug and its name, and either can be the one PostgreSQL reports —
/// they are derived from the same input, so which index refuses first is not the caller's to
/// predict. Matching only the slug caught the race in a test and not in the wild. A tagging is
/// unique per tag and target, in two indexes because the target is a feature or a non-feature
/// entity and never both.
/// </remarks>
public static class TagIdentity
{
    private static readonly string[] TagIndexes = ["ix_tags_slug", "ix_tags_name"];

    private static readonly string[] TaggingIndexes =
        ["ix_taggings_tag_id_feature_id", "ix_taggings_tag_id_entity_type_entity_id"];

    /// <summary>Whether a failed insert of a tag lost to another writer coining the same tag.</summary>
    public static bool IsTagRace(DbUpdateException e) => IsRaceOn(e, TagIndexes);

    /// <summary>Whether a failed insert of a tagging lost to another writer applying the same tag to the same target.</summary>
    public static bool IsTaggingRace(DbUpdateException e) => IsRaceOn(e, TaggingIndexes);

    /// <remarks>
    /// Matched on the index by name rather than on the error code alone, so that a different
    /// unique violation arriving through the same write — a defect somewhere else — is not quietly
    /// swallowed and answered as somebody else's success.
    /// </remarks>
    private static bool IsRaceOn(DbUpdateException e, string[] indexes) =>
        e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } violation
        && violation.ConstraintName is { } name
        && Array.IndexOf(indexes, name) >= 0;
}
