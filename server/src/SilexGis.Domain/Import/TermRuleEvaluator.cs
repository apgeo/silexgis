// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Import;

/// <summary>The texts a candidate offers a rule to read.</summary>
public sealed record CandidateText(string? Name, string? Description);

/// <summary>One rule's claim on a candidate: which rule, where it sits in the set, and what matched.</summary>
public sealed record RuleClaim(TermRule Rule, int Order, CandidateField Field, TermMatch Match);

/// <summary>
/// What a rule set makes of one candidate. <see cref="Winner"/> is the claim that decides,
/// <see cref="Contenders"/> the ones it beat — kept so the review screen can say *why* a
/// candidate is proposed as what it is, and which other rule wanted it.
/// </summary>
public sealed record CandidateProposal(RuleClaim? Winner, IReadOnlyList<RuleClaim> Contenders)
{
    public static CandidateProposal None { get; } = new(null, []);

    public bool HasConflict => Contenders.Count > 0;
}

/// <summary>
/// Applies an ordered rule set to a candidate. Two decisions live here and nowhere else.
///
/// <para>
/// <b>Order decides.</b> When several rules match, the one earliest in the set wins and the
/// rest are reported as contenders. Nothing about the match is weighed — not how long the
/// term is, not how specific the mode is — because a rule set is something a club tunes by
/// dragging rows, and any cleverness here would make that dragging stop working.
/// </para>
/// <para>
/// <b>Name is read before description.</b> Within one rule, a match on the name beats a match
/// on the description; a term buried in a note is weaker evidence than the same term in the
/// label, and stripping only ever touches the name.
/// </para>
/// </summary>
public static class TermRuleEvaluator
{
    public static CandidateProposal Evaluate(
        IReadOnlyList<TermRule> orderedRules,
        CandidateText text,
        IReadOnlyCollection<string> languages)
    {
        List<RuleClaim>? claims = null;
        for (var order = 0; order < orderedRules.Count; order++)
        {
            var rule = orderedRules[order];
            if (!rule.Enabled)
            {
                continue;
            }

            var terms = rule.TermsFor(languages).ToList();
            if (terms.Count == 0)
            {
                continue;
            }

            var claim = ClaimOf(rule, order, terms, text);
            if (claim is not null)
            {
                (claims ??= []).Add(claim);
            }
        }

        return claims is null
            ? CandidateProposal.None
            : new CandidateProposal(claims[0], [.. claims.Skip(1)]);
    }

    /// <summary>Per-rule hit counts over a whole file — the dry run's answer (which rule claimed how many).</summary>
    public static IReadOnlyDictionary<string, int> HitCounts(IEnumerable<CandidateProposal> proposals)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var proposal in proposals)
        {
            if (proposal.Winner is { } winner)
            {
                counts[winner.Rule.Id] = counts.GetValueOrDefault(winner.Rule.Id) + 1;
            }
        }

        return counts;
    }

    private static RuleClaim? ClaimOf(TermRule rule, int order, List<string> terms, CandidateText text)
    {
        if (rule.MatchName && TermMatcher.Match(text.Name, terms, rule.MatchMode) is { } nameMatch)
        {
            return new RuleClaim(rule, order, CandidateField.Name, nameMatch);
        }

        if (rule.MatchDescription
            && TermMatcher.Match(text.Description, terms, rule.MatchMode) is { } descriptionMatch)
        {
            return new RuleClaim(rule, order, CandidateField.Description, descriptionMatch);
        }

        return null;
    }
}
