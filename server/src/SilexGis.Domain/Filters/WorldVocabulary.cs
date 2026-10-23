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
/// <param name="Label">
/// The name to show as written, for a field the installation itself declared — a typed property
/// takes the title its feature type's schema gives it, which is data somebody typed rather than a
/// string this application translates. Null for every field the application declares, which is
/// named through <paramref name="LabelKey"/> alone.
/// </param>
/// <param name="Requires">
/// A condition that has to stand beside this one for it to be asked at all — null for nearly every
/// field. A typed property names the type whose schema declares it, and is valid only inside a
/// group that also fixes that type; see <see cref="FieldRequirement"/>.
/// </param>
/// <param name="Ops">
/// The operators this field admits, when they are fewer than its kind's — null for nearly every
/// field. A cave chosen from a picker is an identity, but "hangs on no cave at all" is a question
/// about every pairing the row has, including the ones the reader is not shown, so that field
/// admits only equality. Narrower than the kind's list, never wider: every member here must be one
/// the kind's table holds.
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
    string? Options = null,
    string? Label = null,
    FieldRequirement? Requires = null,
    IReadOnlyList<FilterOp>? Ops = null)
{
    /// <summary>
    /// The operators a condition on this field may use: the field's own list when it has one,
    /// else its kind's. The validator and the published vocabulary both read this.
    /// </summary>
    public IReadOnlyList<FilterOp> Operators =>
        Ops is null ? FilterOps.For(Kind) : [.. Ops.Where(op => FilterOps.For(Kind).Contains(op))];
}

/// <summary>
/// The condition a field is only valid beside: <paramref name="Field"/> equal to
/// <paramref name="Value"/>, as a direct member of an all-of group that encloses the field's own
/// condition.
/// </summary>
/// <remarks>
/// <para>
/// A typed property is meaningful only for the type that declares it — "depth under 30" asked of
/// every feature would test a key on rows whose schema never mentions it. Requiring the type
/// condition beside it keeps every such question inside the type it was offered for, and the
/// compiler scopes the property's own test to that type as well, so the two agree.
/// </para>
/// <para>
/// A condition without its companion is refused in exactly the words used for a field that does
/// not exist. Refusing it differently would make "is there a key called X on some type" a question
/// with an answer.
/// </para>
/// </remarks>
public sealed record FieldRequirement(string Field, string Value);

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
/// the count moves, the row does not appear, and the answer is obtained anyway. A pairing is offered
/// only where it cannot do that: a survey model's cave, when every model returned already belongs to
/// a cave its reader may read and place; a photograph's cave, when the cave named has to pass the
/// placement gate first and is refused like a missing one otherwise.
/// </para>
/// </remarks>
public sealed record WorldVocabulary(
    string World,
    string LabelKey,
    IReadOnlyList<FieldDescriptor> Fields,
    IReadOnlyList<SortKey> Sorts)
{
    /// <summary>
    /// The largest radius a distance condition may ask about here, in metres — the installation's
    /// setting, stamped on every vocabulary where the worlds are composed, so the validator and the
    /// published limits read the one value. The default is the shipped one.
    /// </summary>
    public double MaxRadiusMeters { get; init; } = SpatialConditions.DefaultMaxRadiusMeters;

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
    /// Nearest an anchor first — a point sent with the request, or an object the caller may place
    /// exactly. Always nearest first: the direction flag does not turn it round, because "farthest
    /// first" is not a question anybody asks of a list and a flag that silently meant it would be.
    /// <para>
    /// A rank is a position. Only the rows this caller may place exactly are ranked, by their true
    /// distance. Every other row — a protected point they are shown snapped, a row with no
    /// position — follows all of them, newest first, exactly as though proximity had not been asked
    /// for. Not ranked by its snapped point either: a distance to a lattice point is still a
    /// measurement, and the same rows ranked from two anchors would triangulate the snapping away.
    /// Not excluded, which would show as a gap. Putting them after the ranked rows tells nobody
    /// anything new, because every row already says whether its caller may place it.
    /// </para>
    /// </summary>
    Proximity = 4,
}
