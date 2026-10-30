// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.RegularExpressions;
using NetArchTest.Rules;
using Shouldly;

namespace SilexGis.Architecture.Tests;

/// <summary>
/// A feature slice owns its endpoints, DTOs and helpers, and nothing outside it may reach in:
/// a type another slice borrows can no longer be changed for the sake of its own slice, and a
/// pair of slices that borrow from each other cannot be read, tested or moved apart. What two
/// slices both need belongs in Common (or Domain / Infrastructure), where it has no owner to
/// surprise.
///
/// The rule is read from the compiled assembly, not from the source text, so a fully qualified
/// name with no using directive, a base class or a generic argument counts as much as an import.
/// </summary>
public class SliceIsolationTests
{
    private const string FeaturesNamespace = "SilexGis.Api.Features";

    /// <summary>
    /// The borrowings that exist today, each with the reason it has not been moved to a shared
    /// place yet. The list only shrinks: a new entry is a decision to couple two slices and says
    /// why here, and an entry whose borrowing has gone fails the test so it is deleted.
    /// </summary>
    private static readonly Dictionary<(string From, string To), string> Allowed = new()
    {
    };

    [Fact]
    public void Feature_slices_do_not_reach_into_each_other()
    {
        var found = Borrowings();

        var unlisted = found.Where(pair => !Allowed.ContainsKey(pair)).Order().ToList();
        unlisted.ShouldBeEmpty(
            "a slice may not depend on another slice's types; move what both need to Common, or"
            + " list the pair in Allowed with the reason it cannot move yet: "
            + string.Join(", ", unlisted.Select(p => $"{p.From} -> {p.To}")));

        var stale = Allowed.Keys.Where(pair => !found.Contains(pair)).Order().ToList();
        stale.ShouldBeEmpty(
            "these pairs are listed as allowed but no longer depend on each other; delete them: "
            + string.Join(", ", stale.Select(p => $"{p.From} -> {p.To}")));
    }

    [Fact]
    public void Every_allowed_borrowing_says_why()
    {
        Allowed.Where(entry => string.IsNullOrWhiteSpace(entry.Value))
            .Select(entry => $"{entry.Key.From} -> {entry.Key.To}")
            .ShouldBeEmpty();
    }

    [Fact]
    public void The_slice_list_is_read_from_the_assembly_and_is_not_empty()
    {
        // A namespace change that moved every slice would make the rule above pass over nothing.
        Slices().Count.ShouldBeGreaterThan(40);
        Slices().ShouldContain("Caves");
        Slices().ShouldContain("Photos");
    }

    private static List<string> Slices() =>
        [.. typeof(Program).Assembly.GetTypes()
            .Select(t => t.Namespace)
            .OfType<string>()
            .Where(ns => ns.StartsWith(FeaturesNamespace + ".", StringComparison.Ordinal))
            .Select(ns => ns[(FeaturesNamespace.Length + 1)..].Split('.')[0])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

    /// <summary>
    /// The full names of the types a slice declares at its top level. A dependency is looked for
    /// by these names and not by the slice's namespace, because the matcher compares name
    /// beginnings and "Map" would otherwise also catch "MapLayers" and "MapViews".
    /// </summary>
    private static string[] TypesOf(string slice) =>
        [.. typeof(Program).Assembly.GetTypes()
            .Where(t => !t.IsNested && t.Namespace is { } ns
                && (ns == $"{FeaturesNamespace}.{slice}"
                    || ns.StartsWith($"{FeaturesNamespace}.{slice}.", StringComparison.Ordinal)))
            .Select(t => t.FullName!)];

    /// <summary>
    /// Every (from, to) pair of distinct slices where a type of the first depends on a type of
    /// the second. One scan per slice finds the slices that borrow at all; only those are
    /// scanned again, one other slice at a time, to name what they borrow from.
    /// </summary>
    private static HashSet<(string From, string To)> Borrowings()
    {
        var slices = Slices();
        var assembly = typeof(Program).Assembly;
        var owned = slices.ToDictionary(slice => slice, TypesOf);
        var found = new HashSet<(string, string)>();

        foreach (var slice in slices)
        {
            var inSlice = $"^{Regex.Escape(FeaturesNamespace + "." + slice)}(\\.|$)";
            var others = slices.Where(o => o != slice).ToList();

            var scan = Types.InAssembly(assembly)
                .That().ResideInNamespaceMatching(inSlice)
                .ShouldNot().HaveDependencyOnAny([.. others.SelectMany(o => owned[o])])
                .GetResult();
            if (scan.IsSuccessful)
            {
                continue;
            }

            foreach (var other in others)
            {
                var pair = Types.InAssembly(assembly)
                    .That().ResideInNamespaceMatching(inSlice)
                    .ShouldNot().HaveDependencyOnAny(owned[other])
                    .GetResult();
                if (!pair.IsSuccessful)
                {
                    found.Add((slice, other));
                }
            }
        }

        return found;
    }
}
