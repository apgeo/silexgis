// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Filters;

/// <summary>
/// One field a world admits in a condition.
/// </summary>
/// <param name="Key">
/// What a condition names. Stored in saved documents, so it is a contract per world: renaming one
/// silently changes what every saved filter means.
/// </param>
/// <param name="LabelKey">
/// The translation key the builder shows. The server never sends a label — a filter built in
/// Romanian and opened in English must read as English, and a stored label would not.
/// </param>
/// <param name="Options">
/// Where the builder gets the choices for an identity field, when the choices are a list rather
/// than something typed. Null means the field takes free input.
/// </param>
/// <remarks>
/// A field says what can be asked of it, not how results are ordered. Ordering is a
/// <see cref="SortKey"/>, which is a small closed set precisely so that a merged list of several
/// worlds is sorted by something that means the same in all of them — a per-field sort flag would
/// promise an order no world could be asked for.
/// </remarks>
public sealed record FieldDescriptor(
    string Key,
    string LabelKey,
    FieldKind Kind,
    string? Options = null);

/// <summary>
/// What one world can be asked about.
/// </summary>
/// <remarks>
/// <para>
/// Each world declares its own. There is deliberately no shared superset that every world must
/// implement: a cabinet has no geometry and a caving group has no protection class, and a
/// vocabulary that pretended otherwise would offer conditions that compile to nothing.
/// </para>
/// <para>
/// What a vocabulary may <em>not</em> declare is the more important half, and it is a rule rather
/// than a preference. No field whose visibility is decided per row and per caller ever appears
/// here — a person's real name, address or contact details, and the pairings that say which cave a
/// trip visited or which cave a survey belongs to. Filtering, matching or sorting on such a field
/// answers a question about a row without returning it, which is the whole shape of a disclosure:
/// the count moves, the row does not appear, and the answer is obtained anyway.
/// </para>
/// </remarks>
public sealed record WorldVocabulary(
    string World,
    string LabelKey,
    IReadOnlyList<FieldDescriptor> Fields,
    IReadOnlyList<SortKey> Sorts)
{
    public FieldDescriptor? Field(string key) =>
        Fields.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.Ordinal));

    public bool Supports(SortKey sort) => Sorts.Contains(sort);
}

/// <summary>
/// How results are ordered.
/// </summary>
/// <remarks>
/// Small on purpose, and every member has to mean the same thing in every world or a merged list
/// would be sorted by nothing in particular. That is why there is no "relevance": three
/// incompatible notions of it already exist in this application, and inventing a comparable one is
/// a decision about policy rather than a detail of implementation.
/// </remarks>
public enum SortKey
{
    /// <summary>Newest first. The only sort every world can answer.</summary>
    Created = 0,

    /// <summary>Most recently changed first.</summary>
    Updated = 1,

    /// <summary>Alphabetical by whatever the world calls a title.</summary>
    Title = 2,

    /// <summary>Who made it.</summary>
    Owner = 3,

    /// <summary>
    /// When the thing happened, as opposed to when somebody wrote it down.
    /// <para>
    /// A trip that took place in May and was typed up in August is a May trip, and a person looking
    /// for it looks in May. Only worlds where the two genuinely differ declare this; a feature has
    /// no such date and does not offer it, rather than quietly answering with the record's own.
    /// </para>
    /// </summary>
    Occurred = 5,

    /// <summary>
    /// Nearest an anchor first — the selected object, or the middle of the view when nothing is
    /// selected.
    /// <para>
    /// A rank is a position. A row this caller may not place exactly is never ranked by distance;
    /// it keeps the order the fallback sort gives it. Not excluded, which would show as a gap, and
    /// not pushed to the end, which is still an answer — simply sorted as though proximity had not
    /// been asked for.
    /// </para>
    /// </summary>
    Proximity = 4,
}
