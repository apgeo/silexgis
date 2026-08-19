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
/// <para>
/// <b>Where the write-up has got to is here, and belongs here.</b> Unlike the two above, it is a
/// plain row fact: the same for every caller who may read the row, disclosing nothing about any
/// other row and nothing the trip's own screen does not already say. It is also never consulted
/// when deciding who may read a trip — a draft is not hidden, only unannounced — so asking for it
/// cannot be a second way to ask a question visibility already answered.
/// </para>
/// <para>
/// <b>Whether something went wrong is here, on the same reasoning.</b> It names no cave and no
/// person: it is a fact about the trip being asked about, and about nothing else, so an answer
/// discloses only rows the caller already reads. That is the whole test the two refusals above
/// fail — "which trips visited cave X" and "which trips had person Y on them" answer questions
/// about a cave and about a person from rows the asker may never see, and the count alone gives
/// the answer away. Nothing of the sort follows from knowing that a trip one may already read
/// went badly. The account of what went wrong is a different matter and is not a field here;
/// it is disclosed by the trip's own reading, under its own narrower rule.
/// </para>
/// </remarks>
public static class TripLogFilterFields
{
    public const string Title = "title";

    /// <summary>
    /// What the trip was for. The key stays what it always was because saved filter documents are
    /// addressed by it, but what it holds is now a row identity from the trip-purpose vocabulary
    /// rather than the name of a value fixed when the software was built.
    /// </summary>
    public const string Type = "type";

    /// <summary>How far the write-up has got — a draft, announced, or called off.</summary>
    public const string State = "state";

    /// <summary>The day it happened — not the day the record was written.</summary>
    public const string TripDate = "tripDate";

    public const string OwnerId = "ownerId";
    public const string CavingGroupId = "cavingGroupId";

    /// <summary>The group that put the trip on, which is not always the owner's own.</summary>
    public const string OrganizingCavingGroupId = "organizingCavingGroupId";

    /// <summary>Whether anything went wrong — the fact, never the account of it.</summary>
    public const string HadIncident = "hadIncident";

    public const string Visibility = "visibility";
    public const string CreatedAt = "createdAt";
    public const string UpdatedAt = "updatedAt";

    public static WorldVocabulary Vocabulary { get; } = new(
        World: "tripLog",
        LabelKey: "filters.worlds.tripLog",
        Fields:
        [
            new FieldDescriptor(Title, "filters.fields.title", FieldKind.Text),
            new FieldDescriptor(Type, "filters.fields.tripType", FieldKind.Id, Options: "tripTypes"),
            // The whole lifecycle vocabulary is offered, whichever of it a trip may hold: the
            // option set names a vocabulary shared by every kind of activity, and narrowing it per
            // kind here would put a second copy of "which states a trip may hold" a long way from
            // the first.
            new FieldDescriptor(State, "filters.fields.state", FieldKind.Id, Options: "activityStates"),
            new FieldDescriptor(TripDate, "filters.fields.tripDate", FieldKind.Instant),
            new FieldDescriptor(HadIncident, "filters.fields.hadIncident", FieldKind.Boolean),
            new FieldDescriptor(OwnerId, "filters.fields.owner", FieldKind.Id, Options: "users"),
            new FieldDescriptor(CavingGroupId, "filters.fields.cavingGroup", FieldKind.Id, Options: "cavingGroups"),
            new FieldDescriptor(
                OrganizingCavingGroupId, "filters.fields.organizingGroup", FieldKind.Id, Options: "cavingGroups"),
            new FieldDescriptor(Visibility, "filters.fields.visibility", FieldKind.Id, Options: "visibilities"),
            new FieldDescriptor(CreatedAt, "filters.fields.created", FieldKind.Instant),
            new FieldDescriptor(UpdatedAt, "filters.fields.updated", FieldKind.Instant),
        ],
        // Occurred is the one people actually want here: a trip is found by when it happened, not
        // by when somebody got round to typing it up.
        Sorts: [SortKey.Occurred, SortKey.Created, SortKey.Updated, SortKey.Title, SortKey.Owner]);
}
