// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Filters;

/// <summary>
/// What a survey model can be filtered by.
/// </summary>
/// <remarks>
/// <para>
/// A survey model has no owner, no caving group and no visibility of its own: it is seen through
/// its cave, by whoever may read the cave and place it exactly. So none of those common fields is
/// offered and there is no sort by owner.
/// </para>
/// <para>
/// <b>The cave a model belongs to is here, with nothing of its own to decide.</b> Every model this
/// world returns already belongs to a cave the reader may read and place exactly, so "models of cave
/// X" is answered over those rows alone: for a cave the reader may not place there are no rows to
/// match, exactly as for a cave that does not exist. The rule stays where it is, in the world's
/// visible set, and is not stated a second time here. Only equality is offered, as on photographs.
/// </para>
/// <para>
/// <b>The position is the cave's, as the reader is shown it</b> — its entrances, decided the way a
/// cave's position is decided in the feature world. Not the model's own anchor, which is the same
/// place by another name, nor the processing state, which is about the server's queue.
/// </para>
/// </remarks>
public static class SurveyModelFilterFields
{
    public const string World = "surveyModel";

    /// <summary>The file's format: a Therion line plot, a Survex line plot or a wall mesh.</summary>
    public const string Format = "format";

    /// <summary>The day the survey was taken, when the uploader recorded one.</summary>
    public const string SurveyedAt = "surveyedAt";

    /// <summary>Whether this is the model its cave is represented by, among the cave's models of its kind.</summary>
    public const string IsCurrent = "isCurrent";

    /// <summary>The cave the model belongs to, by its id.</summary>
    public const string Cave = "cave";

    public static IReadOnlyList<FieldDescriptor> Fields { get; } =
    [
        new FieldDescriptor(CommonFilterFields.Name, "filters.fields.name", FieldKind.Text),
        new FieldDescriptor(Format, "filters.fields.surveyFormat", FieldKind.Id, Options: "surveyModelFormats"),
        new FieldDescriptor(SurveyedAt, "filters.fields.surveyedAt", FieldKind.Instant),
        new FieldDescriptor(IsCurrent, "filters.fields.isCurrent", FieldKind.Boolean),
        .. CommonFilterFields.Descriptors.Where(f => f.Key is CommonFilterFields.CreatedAt
            or CommonFilterFields.UpdatedAt),
        // Declared last, once both are answered.
        new FieldDescriptor(Cave, "filters.fields.cave", FieldKind.Id, Options: "caves", Ops: PhotographFilterFields.CaveOps),
        new FieldDescriptor(SpatialConditions.PositionField, "filters.fields.position", FieldKind.Spatial),
    ];

    public static WorldVocabulary Vocabulary { get; } = new(
        World,
        $"filters.worlds.{World}",
        Fields,
        [SortKey.Created, SortKey.Updated, SortKey.Title]);
}
