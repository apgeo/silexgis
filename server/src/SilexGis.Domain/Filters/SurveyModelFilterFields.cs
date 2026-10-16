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
/// <b>The cave a model belongs to is not here.</b> Every model this world returns already belongs
/// to a cave the reader may place exactly, so the condition would not leak by itself — but "models
/// of cave X" is the pairing every vocabulary declines on principle, and a second statement of the
/// cave rule is the copy that drifts. Nor is the anchor, which is the cave's position by another
/// name, nor the processing state, which is about the server's queue rather than the survey.
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

    public static IReadOnlyList<FieldDescriptor> Fields { get; } =
    [
        new FieldDescriptor(CommonFilterFields.Name, "filters.fields.name", FieldKind.Text),
        new FieldDescriptor(Format, "filters.fields.surveyFormat", FieldKind.Id, Options: "surveyModelFormats"),
        new FieldDescriptor(SurveyedAt, "filters.fields.surveyedAt", FieldKind.Instant),
        new FieldDescriptor(IsCurrent, "filters.fields.isCurrent", FieldKind.Boolean),
        .. CommonFilterFields.Descriptors.Where(f => f.Key is CommonFilterFields.CreatedAt
            or CommonFilterFields.UpdatedAt),
    ];

    public static WorldVocabulary Vocabulary { get; } = new(
        World,
        $"filters.worlds.{World}",
        Fields,
        [SortKey.Created, SortKey.Updated, SortKey.Title]);
}
