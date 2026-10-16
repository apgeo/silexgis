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
/// <b>Not here, each for its own reason.</b> The position, and whether there is one: a picture's
/// fix is disclosed per reader by what it hangs on, so even "has a position" would read that rule
/// off the gallery. What the picture hangs on — a cave, a trip, an album — is the pairing every
/// vocabulary declines: each is an object with a visibility of its own. The photographer is a
/// person, whose name is shown per reader. The camera and the rest of the metadata bag: the
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

    public static IReadOnlyList<FieldDescriptor> Fields { get; } =
    [
        new FieldDescriptor(Title, "filters.fields.title", FieldKind.Text),
        new FieldDescriptor(Caption, "filters.fields.caption", FieldKind.Text),
        new FieldDescriptor(PlaceName, "filters.fields.placeName", FieldKind.Text),
        new FieldDescriptor(Tag, "filters.fields.tag", FieldKind.Id, Options: "tags"),
        new FieldDescriptor(TakenAt, "filters.fields.takenAt", FieldKind.Instant),
        new FieldDescriptor(DatedAt, "filters.fields.datedAt", FieldKind.Instant),
        .. CommonFilterFields.Descriptors.Where(f => f.Key is not CommonFilterFields.Name),
    ];

    public static WorldVocabulary Vocabulary { get; } = new(
        World,
        $"filters.worlds.{World}",
        Fields,
        CommonFilterFields.Sorts);
}
