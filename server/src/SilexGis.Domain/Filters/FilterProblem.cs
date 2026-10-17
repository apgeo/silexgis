// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Filters;

/// <summary>
/// One reason a filter cannot be run, in a form a client can act on.
/// </summary>
/// <param name="Code">
/// What is wrong, from the closed list in <see cref="FilterProblemCodes"/>. The one member a
/// client words the problem from; it never depends on what the caller's world holds beyond what
/// the caller sent.
/// </param>
/// <param name="Path">
/// Where in the document the problem is, as a JSON Pointer into the document that was sent
/// (<c>/scope/0/where/of/1</c>): the scope entry, then the node, with a negation's single child at
/// <c>/of</c>. Empty for a problem about the document as a whole.
/// </param>
/// <param name="Field">The field key the condition named, as the caller sent it; null off a condition.</param>
/// <param name="Op">The comparison the condition asked for; null off a condition.</param>
/// <param name="Limit">The ceiling or count the problem is about, when it is about one.</param>
/// <param name="Detail">An English sentence for a log or a person reading the raw answer.</param>
public sealed record FilterProblem(
    string Code,
    string Path,
    string? Field,
    FilterOp? Op,
    int? Limit,
    string Detail);

/// <summary>
/// The reasons a filter is refused. Stable: a client words each in its reader's language.
/// </summary>
public static class FilterProblemCodes
{
    public const string NewerVersion = "filter.newer_version";
    public const string Unreadable = "filter.unreadable";
    public const string NoScope = "filter.no_scope";
    public const string TooManyWorlds = "filter.too_many_worlds";
    public const string WorldRepeated = "filter.world_repeated";

    /// <summary>
    /// A world the caller may not ask about, whether or not it exists — one code for both, so the
    /// refusal cannot tell one from the other.
    /// </summary>
    public const string UnknownWorld = "filter.unknown_world";

    public const string SortUnsupported = "filter.sort_unsupported";
    public const string TooManyNodes = "filter.too_many_nodes";
    public const string TooDeep = "filter.too_deep";
    public const string EmptyGroup = "filter.empty_group";

    /// <summary>
    /// A field the world does not offer this caller: one it does not have, one it has and withholds,
    /// and a typed property asked without the type that declares it, all alike.
    /// </summary>
    public const string UnknownField = "filter.unknown_field";

    public const string OpUnsupported = "filter.op_unsupported";
    public const string TakesNoValue = "filter.takes_no_value";
    public const string ValueCount = "filter.value_count";
    public const string ValueMissing = "filter.value_missing";
    public const string TooManyValues = "filter.too_many_values";
    public const string ValueKind = "filter.value_kind";
    public const string TextTooLong = "filter.text_too_long";
    public const string NumberOutOfRange = "filter.number_out_of_range";
    public const string RangeReversed = "filter.range_reversed";
    public const string AnchorNotStored = "filter.anchor_not_stored";
}
