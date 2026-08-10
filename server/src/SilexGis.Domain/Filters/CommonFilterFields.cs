// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Filters;

/// <summary>
/// The fields nearly every world has, spelled once.
/// </summary>
/// <remarks>
/// <para>
/// Not a superset every world must implement — a cabinet has no geometry and a caving group has no
/// protection class, and a vocabulary that pretended otherwise would offer conditions that compile
/// to nothing. This is the opposite: the handful of things that genuinely are the same everywhere,
/// so that a person who has learned to filter caves by owner already knows how to filter map views
/// by owner, and so that a key means one thing across every saved filter on the installation.
/// </para>
/// <para>
/// A world adds its own on top and may leave any of these out.
/// </para>
/// </remarks>
public static class CommonFilterFields
{
    /// <summary>What the row is called. Every world has one, even if it spells the column differently.</summary>
    public const string Name = "name";

    public const string OwnerId = "ownerId";
    public const string CavingGroupId = "cavingGroupId";
    public const string Visibility = "visibility";
    public const string CreatedAt = "createdAt";
    public const string UpdatedAt = "updatedAt";

    public static IReadOnlyList<FieldDescriptor> Descriptors { get; } =
    [
        new FieldDescriptor(Name, "filters.fields.name", FieldKind.Text),
        new FieldDescriptor(OwnerId, "filters.fields.owner", FieldKind.Id, Options: "users"),
        new FieldDescriptor(CavingGroupId, "filters.fields.cavingGroup", FieldKind.Id, Options: "cavingGroups"),
        new FieldDescriptor(Visibility, "filters.fields.visibility", FieldKind.Id, Options: "visibilities"),
        new FieldDescriptor(CreatedAt, "filters.fields.created", FieldKind.Instant),
        new FieldDescriptor(UpdatedAt, "filters.fields.updated", FieldKind.Instant),
    ];

    /// <summary>The sorts any world with a name and timestamps can answer.</summary>
    public static IReadOnlyList<SortKey> Sorts { get; } =
        [SortKey.Created, SortKey.Updated, SortKey.Title, SortKey.Owner];

    /// <summary>A vocabulary made of the common fields plus whatever this world adds.</summary>
    public static WorldVocabulary Vocabulary(
        string world,
        string labelKey,
        IEnumerable<FieldDescriptor>? extra = null,
        IReadOnlyList<SortKey>? sorts = null) =>
        new(world, labelKey, [.. Descriptors, .. extra ?? []], sorts ?? Sorts);
}
