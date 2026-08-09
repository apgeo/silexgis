// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Filters;

/// <summary>
/// What a trip log can be filtered by.
/// </summary>
/// <remarks>
/// <para>
/// The absences carry the weight here, more than in any other world. A trip log is a record of
/// people going to places, so two of its columns are exactly the kind of thing this design refuses
/// to make askable.
/// </para>
/// <para>
/// <b>Which caves a trip visited is not here.</b> A caller who may read a trip but not the cave it
/// went to could otherwise ask "trips that visited cave X" and read the answer off the count — the
/// row itself never appears, and the question is answered anyway. The pairing is a fact about the
/// cave as much as about the trip, and it is disclosed by the trip's own screen under its own rule.
/// </para>
/// <para>
/// <b>Who was on a trip is not here either.</b> Filtering by participant would let somebody
/// assemble where a named person has been from rows they may not read, which is a question about a
/// person rather than about the records. The same reasoning keeps the free-text location note out:
/// it is where a trip went, written down.
/// </para>
/// </remarks>
public static class TripLogFilterFields
{
    public const string Title = "title";
    public const string Type = "type";

    /// <summary>The day it happened — not the day the record was written.</summary>
    public const string TripDate = "tripDate";

    public const string OwnerId = "ownerId";
    public const string CavingGroupId = "cavingGroupId";

    /// <summary>The group that put the trip on, which is not always the owner's own.</summary>
    public const string OrganizingCavingGroupId = "organizingCavingGroupId";

    public const string Visibility = "visibility";
    public const string CreatedAt = "createdAt";
    public const string UpdatedAt = "updatedAt";

    public static WorldVocabulary Vocabulary { get; } = new(
        World: "tripLog",
        LabelKey: "filters.worlds.tripLog",
        Fields:
        [
            new FieldDescriptor(Title, "filters.fields.title", FieldKind.Text, Sortable: true),
            new FieldDescriptor(Type, "filters.fields.tripType", FieldKind.Id, Options: "tripTypes"),
            new FieldDescriptor(TripDate, "filters.fields.tripDate", FieldKind.Instant, Sortable: true),
            new FieldDescriptor(OwnerId, "filters.fields.owner", FieldKind.Id, Options: "users"),
            new FieldDescriptor(CavingGroupId, "filters.fields.cavingGroup", FieldKind.Id, Options: "cavingGroups"),
            new FieldDescriptor(
                OrganizingCavingGroupId, "filters.fields.organizingGroup", FieldKind.Id, Options: "cavingGroups"),
            new FieldDescriptor(Visibility, "filters.fields.visibility", FieldKind.Id, Options: "visibilities"),
            new FieldDescriptor(CreatedAt, "filters.fields.created", FieldKind.Instant, Sortable: true),
            new FieldDescriptor(UpdatedAt, "filters.fields.updated", FieldKind.Instant, Sortable: true),
        ],
        Sorts: [SortKey.Created, SortKey.Updated, SortKey.Title, SortKey.Owner]);
}
