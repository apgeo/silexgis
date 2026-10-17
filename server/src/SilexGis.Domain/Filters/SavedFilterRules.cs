// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Filters;

/// <summary>
/// Whether a saved filter may be stored as written, and what it reads as to one particular reader.
/// </summary>
/// <remarks>
/// <para>
/// A saved filter is checked twice and against two different people. On save it is validated
/// against its author's vocabulary, because the author is the one who chose the fields. On every
/// read and every run it is validated again against the reader's, because a shared filter only
/// describes a question: a field the reader may not ask about makes the filter unreadable for them,
/// and it must say so in the same words whether the field is withheld from them or simply does not
/// exist — or the refusal would itself be the answer to "is there such a field".
/// </para>
/// </remarks>
public static class SavedFilterRules
{
    public const int MaxNameLength = 200;

    public const int MaxDescriptionLength = 2000;

    /// <summary>The stored document is not one this reader can run.</summary>
    public const string UnreadableCode = "saved_filter.unreadable";

    /// <summary>A document arrived carrying an anchor, which is a position and is never stored.</summary>
    public const string AnchorRefusedCode = "saved_filter.anchor_refused";

    /// <summary>
    /// Who a saved filter may be shown to. Private, or the members of one caving group — the two
    /// audiences the owner settled on. Wider audiences are refused rather than quietly narrowed.
    /// </summary>
    public static bool IsAllowedVisibility(Visibility visibility) =>
        visibility is Visibility.Private or Visibility.CavingGroup;

    /// <summary>
    /// Whether this caller may rewrite, re-share or delete this saved filter: its author, or a full
    /// administrator — never somebody it was only shared with.
    /// </summary>
    /// <remarks>
    /// Narrower than the access rules over saved workspace, and on purpose. A caving group made
    /// through the application starts with rules that let its members write everything bound to the
    /// group, which for a saved filter would let any member change what a question shared under
    /// somebody else's name asks, or hand it on to another club they belong to. A member who may
    /// read a shared filter may run it and save a copy of their own; the original stays its
    /// author's. This is asked in addition to the access decision, never instead of it, so a rule
    /// that denies the author still holds.
    /// </remarks>
    public static bool MayChange(AccessContext caller, SavedFilter filter)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(filter);
        return caller.IsFullAdmin || filter.OwnerUserId == caller.UserId;
    }

    /// <summary>
    /// What stops this document being stored for its author. Empty means it may be saved.
    /// </summary>
    /// <remarks>
    /// An anchor is refused rather than stripped. Stripping would store something other than what
    /// was sent and say nothing about it; a client that sent one has misunderstood what a saved
    /// filter keeps, and is better told.
    /// </remarks>
    public static IReadOnlyList<FilterProblem> ProblemsOnSave(
        FilterDocument document, IReadOnlyDictionary<string, WorldVocabulary> authorVocabularies)
    {
        ArgumentNullException.ThrowIfNull(document);
        // A point or an outline written into a condition is the same stored statement about a
        // position as an anchor on the document, and is refused in the same words. An object, an
        // area or another saved filter is kept: it names something looked up afresh, under the
        // runner's access, every time the filter runs.
        if (document.Anchor is not null || SpatialConditions.HoldsPosition(document))
        {
            return [new FilterProblem(FilterProblemCodes.AnchorNotStored, "/anchor", null, null, null,
                "A saved filter never stores a place to measure from.")];
        }

        return FilterValidation.Validate(document, authorVocabularies);
    }

    /// <summary>
    /// Whether this reader can run the stored document as it stands.
    /// </summary>
    /// <remarks>
    /// Null for a document that could not even be parsed, which reads exactly like one naming a
    /// field the reader may not use: unreadable, for them, with nothing more said.
    /// </remarks>
    public static bool IsReadableBy(
        FilterDocument? document, IReadOnlyDictionary<string, WorldVocabulary> readerVocabularies) =>
        document is not null
        && document.Anchor is null
        && !SpatialConditions.HoldsPosition(document)
        && FilterValidation.Validate(document, readerVocabularies).Count == 0;

    /// <summary>
    /// Every identity value the document names, grouped by where the choices for its field come
    /// from (<see cref="FieldDescriptor.Options"/>), so a reader can tell which of them still exist.
    /// </summary>
    /// <remarks>
    /// Collected only from fields the vocabulary declares with an option source. A document that
    /// names an undeclared field is unreadable anyway, and guessing a source for it would be a
    /// second opinion about a field nobody may ask about.
    /// </remarks>
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> References(
        FilterDocument document, IReadOnlyDictionary<string, WorldVocabulary> vocabularies)
    {
        var found = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var scope in document.Scope)
        {
            if (scope.Where is null || !vocabularies.TryGetValue(scope.World, out var vocabulary))
            {
                continue;
            }

            Collect(scope.Where, vocabulary, found);
        }

        return found.ToDictionary(
            pair => pair.Key, pair => (IReadOnlySet<string>)pair.Value, StringComparer.Ordinal);
    }

    /// <summary>The conditions a document asks of one world, or null when it is not about that world.</summary>
    public static WorldScope? ScopeFor(FilterDocument document, string world) =>
        document.Scope.FirstOrDefault(s => string.Equals(s.World, world, StringComparison.Ordinal));

    private static void Collect(
        FilterNode node, WorldVocabulary vocabulary, Dictionary<string, HashSet<string>> found)
    {
        if (node is ConditionNode condition)
        {
            if (vocabulary.Field(condition.Field) is { Options: { } options })
            {
                if (!found.TryGetValue(options, out var values))
                {
                    found[options] = values = new HashSet<string>(StringComparer.Ordinal);
                }

                foreach (var value in condition.Values.OfType<IdValue>())
                {
                    values.Add(value.Value);
                }
            }

            return;
        }

        foreach (var child in node.Children)
        {
            if (child is not null)
            {
                Collect(child, vocabulary, found);
            }
        }
    }
}

/// <summary>What a saved filter reads as to one reader.</summary>
/// <remarks>
/// Decided per reader, because a shared filter is run under the runner's own access. Never folded
/// into "matches nothing": a filter that has stopped meaning anything has to look different from
/// one that simply found no rows, or nobody would ever notice it had broken.
/// </remarks>
public enum SavedFilterState
{
    /// <summary>The reader can run it, and everything it names still exists.</summary>
    Ready = 0,

    /// <summary>
    /// The reader cannot run it: it names a field they may not ask about or that no longer exists,
    /// or it was written in a form this version cannot read. One state for all of those, so the
    /// refusal never tells a reader which.
    /// </summary>
    Unreadable = 1,

    /// <summary>
    /// It can be run, but a type or tag it names has been removed from the installation since, so
    /// that condition can match nothing.
    /// </summary>
    Stale = 2,
}
