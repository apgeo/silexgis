// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Filters;

/// <summary>
/// What a photograph can be filtered by.
/// </summary>
/// <remarks>
/// <para>
/// A photograph is a document whose current file is a picture, so it has a document's owner,
/// group and visibility, and the day the picture was taken as its camera recorded it. Beside those
/// are the facts the gallery shows every reader of the picture alongside it, and asks about with
/// its own controls: the caption and the place name its search box reads with the title, the tags
/// on the picture, and the date the gallery dates it by. Each is shown whole to whoever may see the
/// picture, so a condition over one tells a reader nothing the picture's own page does not.
/// </para>
/// <para>
/// <b>The cave, through the placement gate.</b> "Pictures of cave X" names a cave, and a picture can
/// hang on a cave its reader may read but not place exactly. So the cave a condition names has to
/// pass the gate every spatial anchor passes — readable and placeable exactly by the reader — or the
/// whole filter is refused in the words a cave that does not exist is refused in. Only equality is
/// offered: "hangs on no cave" would be a question about every pairing the picture has, the ones
/// the reader is not shown included.
/// </para>
/// <para>
/// <b>The position, as the reader is shown it.</b> A picture's fix is disclosed per reader by what
/// it hangs on; a position condition is decided on the fix only for a picture whose fix this reader
/// is shown, and a picture whose fix is withheld matches no position condition — exactly as a
/// picture with no fix at all, so the two cannot be told apart. Asked with "has a position" it would
/// still read the rule off; that is why there is no such field.
/// </para>
/// <para>
/// <b>Not here, each for its own reason.</b> What else the picture hangs on — a trip, an album — is
/// the pairing every vocabulary declines: each is an object with a visibility of its own. The
/// photographer is a person, whose name is shown per reader. The camera and the rest of the metadata bag: the
/// gallery matches a camera as a substring of the whole bag, and a condition over the bag would be
/// a way to test for anything written into it, including what no reader is shown.
/// </para>
/// </remarks>
public static class PhotographFilterFields
{
    public const string World = "photograph";

    public const string Title = "title";

    /// <summary>The caption written for the picture.</summary>
    public const string Caption = "caption";

    /// <summary>The place name written for the picture: words, never a position.</summary>
    public const string PlaceName = "placeName";

    /// <summary>A tag on the picture, by the tag's slug — the same catalogue the feature world's tag reads.</summary>
    public const string Tag = "tag";

    /// <summary>When the picture was taken, as its camera recorded it; empty when it did not.</summary>
    public const string TakenAt = "takenAt";

    /// <summary>
    /// The moment the gallery dates a picture by: when it was taken, or, for a picture whose camera
    /// recorded no time, when it was uploaded. Never empty. The gallery's date range asks this.
    /// </summary>
    public const string DatedAt = "datedAt";

    /// <summary>
    /// A cave the picture hangs on, or anything inside it — named by the cave's id, which the reader
    /// must be able to read and place exactly.
    /// </summary>
    public const string Cave = "cave";

    /// <summary>The operators the cave condition admits: which caves, never whether there is one.</summary>
    public static IReadOnlyList<FilterOp> CaveOps { get; } = [FilterOp.Equals, FilterOp.In];

    public static IReadOnlyList<FieldDescriptor> Fields { get; } =
    [
        new FieldDescriptor(Title, "filters.fields.title", FieldKind.Text),
        new FieldDescriptor(Caption, "filters.fields.caption", FieldKind.Text),
        new FieldDescriptor(PlaceName, "filters.fields.placeName", FieldKind.Text),
        new FieldDescriptor(Tag, "filters.fields.tag", FieldKind.Id, Options: "tags"),
        new FieldDescriptor(TakenAt, "filters.fields.takenAt", FieldKind.Instant),
        new FieldDescriptor(DatedAt, "filters.fields.datedAt", FieldKind.Instant),
        .. CommonFilterFields.Descriptors.Where(f => f.Key is not CommonFilterFields.Name),
        // Declared last, once each is decided through the placement gate and the disclosure rule.
        new FieldDescriptor(Cave, "filters.fields.cave", FieldKind.Id, Options: "caves", Ops: CaveOps),
        new FieldDescriptor(SpatialConditions.PositionField, "filters.fields.position", FieldKind.Spatial),
    ];

    public static WorldVocabulary Vocabulary { get; } = new(
        World,
        $"filters.worlds.{World}",
        Fields,
        CommonFilterFields.Sorts);
}
