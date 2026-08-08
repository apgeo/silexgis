// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Import;

/// <summary>
/// What makes a rule document acceptable. Checked when a set is saved rather than when it
/// runs: a set is exchanged as a file between installations, so the first thing that reads a
/// club's rules may be somebody else's server, and a rule that cannot be evaluated must be
/// refused at the door rather than silently claim nothing at import time.
/// </summary>
public static class TermRuleValidation
{
    /// <summary>A set this size is already unmanageable by hand; the ceiling is a guard, not a design.</summary>
    public const int MaxRules = 500;

    public const int MaxTermsPerRule = 200;

    public const int MaxTermLength = 200;

    public const int MaxRuleNameLength = 200;

    /// <summary>Problems with the document, as sentences. Empty means it can be saved.</summary>
    public static IReadOnlyList<string> Validate(TermRuleDocument document)
    {
        var errors = new List<string>();
        var rules = document.Rules;

        if (rules.Count > MaxRules)
        {
            errors.Add($"A rule set holds at most {MaxRules} rules.");
            return errors;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            var label = string.IsNullOrWhiteSpace(rule.Name) ? rule.Id : rule.Name;

            if (string.IsNullOrWhiteSpace(rule.Id))
            {
                errors.Add("Every rule needs an id.");
            }
            else if (!ids.Add(rule.Id))
            {
                // Ids are what a saved decision and a batch's provenance point at, so a
                // duplicate would make "which rule created this cave" unanswerable.
                errors.Add($"Rule id '{rule.Id}' is used more than once.");
            }

            if (string.IsNullOrWhiteSpace(rule.Name))
            {
                errors.Add($"Rule '{rule.Id}' needs a name.");
            }
            else if (rule.Name.Length > MaxRuleNameLength)
            {
                errors.Add($"The name of rule '{label}' is longer than {MaxRuleNameLength} characters.");
            }

            if (!Enum.IsDefined(rule.MatchMode))
            {
                errors.Add($"Rule '{label}' names an unknown match mode.");
            }

            if (!Enum.IsDefined(rule.Target))
            {
                errors.Add($"Rule '{label}' names an unknown target.");
            }

            if (!Enum.IsDefined(rule.Strip))
            {
                errors.Add($"Rule '{label}' names an unknown strip mode.");
            }

            if (!rule.MatchName && !rule.MatchDescription)
            {
                errors.Add($"Rule '{label}' reads no field: choose the name, the description, or both.");
            }

            ValidateTerms(rule, label, errors);
            ValidateTarget(rule, label, errors);
        }

        return errors;
    }

    private static void ValidateTerms(TermRule rule, string label, List<string> errors)
    {
        var total = 0;
        foreach (var (language, terms) in rule.Terms)
        {
            if (string.IsNullOrWhiteSpace(language))
            {
                errors.Add($"Rule '{label}' has a term list under a blank language.");
            }

            total += terms.Count;
            foreach (var term in terms)
            {
                if (string.IsNullOrWhiteSpace(term))
                {
                    errors.Add($"Rule '{label}' has an empty term.");
                    continue;
                }

                if (term.Length > MaxTermLength)
                {
                    errors.Add($"Rule '{label}' has a term longer than {MaxTermLength} characters.");
                    continue;
                }

                if (rule.MatchMode == TermMatchMode.Regex && !TermMatcher.IsValidRegex(term))
                {
                    errors.Add($"Rule '{label}': '{term}' is not a usable regular expression.");
                }
            }
        }

        if (total == 0)
        {
            errors.Add($"Rule '{label}' has no terms.");
        }
        else if (total > MaxTermsPerRule)
        {
            errors.Add($"Rule '{label}' has more than {MaxTermsPerRule} terms.");
        }
    }

    private static void ValidateTarget(TermRule rule, string label, List<string> errors)
    {
        // A rule names the taxonomy code its target needs and no other: a cave rule carrying a
        // feature-type code is a rule somebody re-pointed and half-edited, and importing it
        // would create caves of a type nobody chose.
        //
        // Every applicable complaint is reported rather than the first one. A half-edited rule
        // usually has both problems — the code it should not carry and the one it now needs —
        // and fixing them one round trip at a time is how an editor comes to feel broken.
        if (rule.Target == ImportTargetKind.Cave
            && (rule.FeatureTypeCode is not null || rule.EntranceTypeCode is not null))
        {
            errors.Add($"Rule '{label}' proposes a cave but names an entrance or feature type.");
        }

        if (rule.Target == ImportTargetKind.CaveEntrance
            && (rule.FeatureTypeCode is not null || rule.CaveTypeCode is not null))
        {
            errors.Add($"Rule '{label}' proposes an entrance but names a cave or feature type.");
        }

        if (rule.Target == ImportTargetKind.SurfaceFeature)
        {
            if (rule.CaveTypeCode is not null || rule.EntranceTypeCode is not null)
            {
                errors.Add($"Rule '{label}' proposes a surface feature but names a cave or entrance type.");
            }

            if (string.IsNullOrWhiteSpace(rule.FeatureTypeCode))
            {
                errors.Add($"Rule '{label}' proposes a surface feature without saying which kind.");
            }
        }
    }
}
