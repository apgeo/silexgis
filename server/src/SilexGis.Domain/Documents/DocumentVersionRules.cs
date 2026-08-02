// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Documents;

/// <summary>
/// The version-sequence semantics of a document, as pure rules over the version set:
/// how versions are numbered, which one is current, and what a well-formed set looks
/// like. The write service loads state, delegates here and persists; the integrity
/// verifier re-derives the same answers to prove the write path held. Keeping the rules
/// in one place is what stops "which version is current" from having two answers.
/// </summary>
public static class DocumentVersionRules
{
    /// <summary>A document's first version is number 1; numbers never restart.</summary>
    public const int FirstVersionNumber = 1;

    /// <summary>The subset of a version row the rules reason about.</summary>
    public readonly record struct VersionState(Guid Id, int VersionNumber, bool IsCurrent);

    /// <summary>
    /// The number a new version takes: one past the highest issued so far. Deliberately
    /// not "count + 1" — deleting a superseded version must not make the next upload
    /// reuse a number the audit trail already refers to.
    /// </summary>
    public static int NextVersionNumber(IEnumerable<VersionState> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);

        var highest = FirstVersionNumber - 1;
        foreach (var version in versions)
        {
            if (version.VersionNumber > highest)
            {
                highest = version.VersionNumber;
            }
        }

        return highest + 1;
    }

    /// <summary>
    /// The version the document currently serves, or null when the set is empty.
    /// Throws when more than one claims it: that is a corrupted document, and guessing
    /// which one to serve would hide the corruption behind a plausible answer.
    /// </summary>
    public static VersionState? Current(IEnumerable<VersionState> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);

        VersionState? current = null;
        foreach (var version in versions.Where(v => v.IsCurrent))
        {
            if (current is not null)
            {
                throw new InvalidOperationException("A document has more than one current version.");
            }

            current = version;
        }

        return current;
    }

    /// <summary>
    /// Whether a new version may be stacked onto this one. Only the current version
    /// accepts one: uploading onto a superseded version would silently discard whatever
    /// replaced it.
    /// </summary>
    public static bool MayStackOnto(VersionState version) => version.IsCurrent;

    /// <summary>
    /// Everything wrong with a document's version set — empty when it is well formed.
    /// A set is well formed when it is non-empty, exactly one version is current, and
    /// version numbers are unique and at least <see cref="FirstVersionNumber"/>. Numbers
    /// may have gaps: deleting a superseded version leaves one, deliberately.
    /// </summary>
    public static IReadOnlyList<string> Validate(IReadOnlyCollection<VersionState> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);

        var problems = new List<string>();
        if (versions.Count == 0)
        {
            problems.Add("the document has no versions");
            return problems;
        }

        var currentCount = versions.Count(v => v.IsCurrent);
        if (currentCount != 1)
        {
            problems.Add($"{currentCount} versions are current, expected exactly 1");
        }

        if (versions.Select(v => v.VersionNumber).Distinct().Count() != versions.Count)
        {
            problems.Add("version numbers repeat");
        }

        if (versions.Any(v => v.VersionNumber < FirstVersionNumber))
        {
            problems.Add($"version numbers below {FirstVersionNumber}");
        }

        return problems;
    }
}
