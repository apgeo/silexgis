// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Filters;

/// <summary>
/// What a document can be filtered by.
/// </summary>
/// <remarks>
/// <para>
/// <b>What a document hangs on is not here.</b> A document reached by being attached to a cave is
/// readable by whoever may read that cave — which means "documents attached to cave X" would answer
/// a question about cave X to somebody who may read a photograph of it but not the cave itself. The
/// attachment is a fact about both ends, and it is disclosed by each end's own screen under that
/// end's own rule.
/// </para>
/// <para>
/// Nor is the extracted text. It is searched elsewhere, by a path that knows how to rank and redact
/// it; a substring condition here would let somebody test for a phrase — a cave name, a person's
/// name, a grid reference — against documents whose contents they are not entitled to read, and
/// read the answer off the count.
/// </para>
/// <para>
/// The language is here and the cabinet is not. A cabinet is somebody's filing decision, and which
/// cabinet a document sits in can be as telling as the document: "everything in the survey archive"
/// is a question about the archive.
/// </para>
/// </remarks>
public static class DocumentFilterFields
{
    public const string Title = "title";
    public const string TypeId = "typeId";
    public const string Language = "language";
    public const string OwnerId = "ownerId";
    public const string CavingGroupId = "cavingGroupId";
    public const string Visibility = "visibility";
    public const string CreatedAt = "createdAt";
    public const string UpdatedAt = "updatedAt";

    public static WorldVocabulary Vocabulary { get; } = new(
        World: "document",
        LabelKey: "filters.worlds.document",
        Fields:
        [
            new FieldDescriptor(Title, "filters.fields.title", FieldKind.Text),
            new FieldDescriptor(TypeId, "filters.fields.type", FieldKind.Id, Options: "documentTypes"),
            new FieldDescriptor(Language, "filters.fields.language", FieldKind.Text),
            new FieldDescriptor(OwnerId, "filters.fields.owner", FieldKind.Id, Options: "users"),
            new FieldDescriptor(CavingGroupId, "filters.fields.cavingGroup", FieldKind.Id, Options: "cavingGroups"),
            new FieldDescriptor(Visibility, "filters.fields.visibility", FieldKind.Id, Options: "visibilities"),
            new FieldDescriptor(CreatedAt, "filters.fields.created", FieldKind.Instant),
            new FieldDescriptor(UpdatedAt, "filters.fields.updated", FieldKind.Instant),
        ],
        Sorts: [SortKey.Created, SortKey.Updated, SortKey.Title, SortKey.Owner]);
}
