// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Filters;

/// <summary>
/// What a feature can be filtered by, and — by omission — what it cannot.
/// </summary>
/// <remarks>
/// <para>
/// The keys are a stored contract. A saved filter written today is read back by a later version,
/// so a key is chosen once and never renamed; a rename would silently change what every saved
/// filter means rather than failing loudly.
/// </para>
/// <para>
/// The absences are deliberate and are the more important half of this file. Nothing here names a
/// feature's geometry, its closest address, its land-registry number or its location notes, and
/// nothing names its parent or its ancestors. Each of those would let a caller ask a question about
/// a row without receiving the row — the count moves, nothing appears, and the answer is obtained
/// anyway. Position is reachable only through the spatial field below, whose anchors are resolved
/// against what the caller may already place exactly.
/// </para>
/// </remarks>
public static class FeatureFilterFields
{
    public const string Name = "name";
    public const string Kind = "kind";
    public const string Category = "category";
    public const string TypeId = "typeId";
    public const string Tag = "tag";
    public const string OwnerId = "ownerId";
    public const string CavingGroupId = "cavingGroupId";
    public const string Visibility = "visibility";

    /// <summary>Whether the object is a protection root — not whether the caller may place it.</summary>
    public const string LocationProtected = "locationProtected";

    public const string CreatedAt = "createdAt";
    public const string UpdatedAt = "updatedAt";

    /// <summary>
    /// A typed property declared by the feature's own type. Addressed as
    /// <c>property:&lt;typeCode&gt;:&lt;key&gt;</c> so a condition always carries the type whose schema
    /// declares the field — a condition that could name any key would also be a way to discover
    /// which keys exist across the whole table.
    /// </summary>
    public const string PropertyPrefix = "property:";

    /// <summary>
    /// Where the object is, reachable only through the spatial operators. The values are resolved
    /// to a set of ids before compilation, over rows the caller may place exactly — which is what
    /// makes the condition safe to negate.
    /// </summary>
    public const string Position = "position";

    /// <summary>The feature world's vocabulary, as the builder and the validator both read it.</summary>
    public static WorldVocabulary Vocabulary { get; } = new(
        World: "feature",
        LabelKey: "filters.worlds.feature",
        Fields:
        [
            new FieldDescriptor(Name, "filters.fields.name", FieldKind.Text, Sortable: true),
            new FieldDescriptor(Kind, "filters.fields.kind", FieldKind.Id, Options: "featureKinds"),
            new FieldDescriptor(Category, "filters.fields.category", FieldKind.Id, Options: "featureCategories"),
            new FieldDescriptor(TypeId, "filters.fields.type", FieldKind.Id, Options: "featureTypes"),
            new FieldDescriptor(Tag, "filters.fields.tag", FieldKind.Id, Options: "tags"),
            new FieldDescriptor(OwnerId, "filters.fields.owner", FieldKind.Id, Options: "users"),
            new FieldDescriptor(CavingGroupId, "filters.fields.cavingGroup", FieldKind.Id, Options: "cavingGroups"),
            new FieldDescriptor(Visibility, "filters.fields.visibility", FieldKind.Id, Options: "visibilities"),
            new FieldDescriptor(LocationProtected, "filters.fields.protected", FieldKind.Boolean),
            new FieldDescriptor(CreatedAt, "filters.fields.created", FieldKind.Instant, Sortable: true),
            new FieldDescriptor(UpdatedAt, "filters.fields.updated", FieldKind.Instant, Sortable: true),
        ],
        // Neither the spatial field nor the proximity sort is declared, because neither is served
        // yet. A vocabulary is an offer: a field listed here is one the builder shows, somebody
        // saves a filter using, and expects to keep meaning what it meant. Offering one that
        // currently matches nothing would be worse than not offering it, since a filter that
        // quietly returns nothing looks exactly like a filter with no matches.
        Sorts: [SortKey.Created, SortKey.Updated, SortKey.Title, SortKey.Owner]);

    /// <summary>
    /// Whether a field key names a typed property, and which type and key it names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Which typed properties exist depends on the installation's own taxonomy, so when they are
    /// offered they will be folded into a copy of the vocabulary built from the feature types —
    /// declared fields like any other, so that "a field is either declared or refused" stays a
    /// single-edged rule and nothing has to recognise a field by its shape.
    /// </para>
    /// <para>
    /// They are not offered yet. The compiler serves equality and presence against the stored
    /// document, which covers a property somebody chose from a list but not one they want a range
    /// of: comparing a depth needs the value pulled out of the document and read as a number, and
    /// that is a decision about how this installation reaches inside jsonb rather than a detail of
    /// this file. Declaring the fields before then would offer "depth under 30" and answer it as
    /// "depth exactly 30", which is the kind of wrong nobody reports because it looks like data.
    /// </para>
    /// </remarks>
    public static bool TryReadProperty(string field, out string typeCode, out string key)
    {
        typeCode = string.Empty;
        key = string.Empty;

        if (!field.StartsWith(PropertyPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = field[PropertyPrefix.Length..];
        var separator = rest.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0 || separator == rest.Length - 1)
        {
            return false;
        }

        typeCode = rest[..separator];
        key = rest[(separator + 1)..];
        return true;
    }
}
