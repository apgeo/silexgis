// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Import;

/// <summary>How a term is compared against a candidate's text. Stored in the rule document by name.</summary>
public enum TermMatchMode
{
    /// <summary>The term appears anywhere. The loosest, and the reason ordering matters.</summary>
    Contains = 0,

    /// <summary>The term appears bounded by non-word characters (or the ends of the text).</summary>
    WholeWord = 1,

    /// <summary>The text starts with the term — the shape of a naming convention (<c>P. Ursilor</c>).</summary>
    Prefix = 2,

    /// <summary>The term is a regular expression, applied to the folded text.</summary>
    Regex = 3,
}

/// <summary>What a rule proposes a candidate should become.</summary>
public enum ImportTargetKind
{
    Cave = 0,

    /// <summary>An entrance. Needs a cave: either a new one created around it, or an existing one chosen in review.</summary>
    CaveEntrance = 1,

    /// <summary>A generic feature of the named type — sinkhole, spring, shaft, ….</summary>
    SurfaceFeature = 2,
}

/// <summary>Which of a candidate's texts a match was found in.</summary>
public enum CandidateField
{
    Name = 0,

    Description = 1,
}

/// <summary>What a rule removes from the name once its term has identified the candidate.</summary>
public enum TermStripMode
{
    /// <summary>Keep the name as the file wrote it.</summary>
    None = 0,

    /// <summary>Remove the matched term only where it opens the name (<c>P. Ursilor</c> → <c>Ursilor</c>).</summary>
    Leading = 1,

    /// <summary>Remove the matched term only where it closes the name (<c>Ursilor cave</c> → <c>Ursilor</c>).</summary>
    Trailing = 2,

    /// <summary>Remove the matched term wherever it sits.</summary>
    Anywhere = 3,
}

/// <summary>
/// One ordered detection rule: a list of terms per language, how they are compared, and what
/// a match proposes the candidate should become.
///
/// <para>
/// Rules live as a JSON array on their set rather than as rows. They are edited, ordered,
/// copied and exchanged between installations as one document — the file a club sends
/// another club is literally this array — and nothing queries an individual rule, so rows
/// would buy indexing nobody uses at the cost of a second representation to keep in step.
/// </para>
/// </summary>
public sealed record TermRule
{
    /// <summary>Stable within its set: what a saved decision and a batch's provenance name.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>A disabled rule is kept, ordered and shown — it simply claims nothing.</summary>
    public bool Enabled { get; init; } = true;

    public TermMatchMode MatchMode { get; init; } = TermMatchMode.WholeWord;

    /// <summary>Read the candidate's name. On by default: the label is where the term belongs.</summary>
    public bool MatchName { get; init; } = true;

    /// <summary>
    /// Also read the description. Off by default — a term buried in a note is much weaker
    /// evidence than the same term in the label, and turning it on is what makes a loose rule
    /// start claiming rows nobody expected.
    /// </summary>
    public bool MatchDescription { get; init; }

    /// <summary>
    /// Terms keyed by language tag (<c>ro</c>, <c>en</c>, …). <see cref="AnyLanguage"/> holds
    /// terms that belong to no language in particular — codes and abbreviations a GPS writes
    /// the same way whoever is holding it.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Terms { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();

    public ImportTargetKind Target { get; init; } = ImportTargetKind.SurfaceFeature;

    /// <summary>Feature-type code for <see cref="ImportTargetKind.SurfaceFeature"/> (e.g. <c>sinkhole</c>).</summary>
    public string? FeatureTypeCode { get; init; }

    /// <summary>Cave-type code for <see cref="ImportTargetKind.Cave"/> (e.g. <c>pit</c>).</summary>
    public string? CaveTypeCode { get; init; }

    /// <summary>Entrance-type code for <see cref="ImportTargetKind.CaveEntrance"/> (e.g. <c>natural</c>).</summary>
    public string? EntranceTypeCode { get; init; }

    public TermStripMode Strip { get; init; } = TermStripMode.None;

    /// <summary>The key under which language-independent terms are written.</summary>
    public const string AnyLanguage = "*";

    /// <summary>The terms this rule compares for a caller reading in the given languages.</summary>
    public IEnumerable<string> TermsFor(IReadOnlyCollection<string> languages)
    {
        foreach (var (language, terms) in Terms)
        {
            if (language == AnyLanguage
                || languages.Count == 0
                || languages.Contains(language, StringComparer.OrdinalIgnoreCase))
            {
                foreach (var term in terms)
                {
                    yield return term;
                }
            }
        }
    }
}

/// <summary>
/// The file a rule set travels as. Carries the set's own name so an import has something to
/// call it, and a version so a future change of shape can be recognised rather than guessed at.
/// </summary>
public sealed record TermRuleDocument
{
    /// <summary>The shape of this document. Bumped only when older files stop being readable.</summary>
    public int Version { get; init; } = CurrentVersion;

    public string? Name { get; init; }

    public string? Description { get; init; }

    public IReadOnlyList<TermRule> Rules { get; init; } = [];

    public const int CurrentVersion = 1;
}
