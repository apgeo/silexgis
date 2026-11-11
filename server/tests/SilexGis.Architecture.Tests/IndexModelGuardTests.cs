// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Architecture.Tests;

/// <summary>
/// The index model is the one part of the schema nothing fails without: a missing index answers
/// every query correctly and only slowly, so no behaviour test can notice it go. These tests stand
/// where it has gone missing before.
/// </summary>
/// <remarks>
/// <para>
/// The way it went missing is a property of EF rather than of any one configuration. A second
/// <c>HasIndex</c> over the same properties, without a model name, does not declare a second index:
/// EF hands back the builder of the first, so the unique, partial index meant to sit beside a plain
/// lookup replaces it and only renames it. The build, the migration and every query stay green.
/// </para>
/// <para>
/// So there are two tests. One reads the configuration sources for the shape that causes it, because
/// by the time the model is built the second declaration has left no trace of its own. The other
/// asks the built model the question that matters whatever the cause: can every foreign key be
/// looked up through an index whose predicate an equality on that key satisfies? A key without one
/// is read by a scan of the whole table on every join and, worse, once per row when a parent is
/// deleted and the database looks for the children to cascade to or to clear.
/// </para>
/// </remarks>
public class IndexModelGuardTests
{
    /// <summary>
    /// Foreign keys that are deliberately left without a usable index of their own, each with its
    /// reason. A new entry is a decision somebody made, not a line somebody added to pass.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> UnindexedOnPurpose =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["term_rule_sets(caving_group_id)"] =
                "a club keeps a handful of rule sets, so the scan is the plan either way; the partial "
                + "unique index on the key exists to keep one default per club, not for lookups",
            ["term_rule_sets(owner_user_id)"] =
                "the same table, and the same handful of rows per account",
        };

    [Fact]
    public void No_configuration_declares_two_unnamed_indexes_over_the_same_properties()
    {
        var configurations = TripSoftDeleteGuardTests.Sources()
            .Where(s => s.File.Contains("/Persistence/Configurations/", StringComparison.Ordinal))
            .ToList();

        // The fixture half: the scan really does read the configurations and really does find the
        // declarations it is meant to judge. A scan that parsed nothing would pass the line below.
        configurations.ShouldNotBeEmpty();
        var declarations = configurations.SelectMany(c => Declarations(c.File, c.Source)).ToList();
        declarations.Count.ShouldBeGreaterThan(100);
        declarations.ShouldContain(d => d.Named && d.Properties == "_.Geom");

        var collapsed = declarations
            .Where(d => !d.Named)
            .GroupBy(d => (d.File, d.Configuration, d.Properties))
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key.File} (configuration {g.Key.Configuration}): HasIndex({g.Key.Properties}) "
                + $"declared {g.Count()} times without a model name, so the later ones edit the first")
            .ToList();

        collapsed.ShouldBeEmpty(
            "Give every index after the first over the same properties a model name, as in "
            + "HasIndex(x => x.Foo, \"ux_table_foo\").HasDatabaseName(\"ux_table_foo\"):\n"
            + string.Join("\n", collapsed));
    }

    [Fact]
    public void Every_foreign_key_leads_an_index_its_equality_can_use()
    {
        using var context = new DesignTimeDbContextFactory().CreateDbContext([]);
        var model = context.Model;

        var keys = model.GetEntityTypes()
            .Where(e => e.GetTableName() is not null && e.GetViewName() is null && !e.IsOwned())
            .SelectMany(e => e.GetDeclaredForeignKeys().Select(fk => (Entity: e, Key: fk)))
            .ToList();

        // The fixture half: the walk finds the keys this was written against.
        keys.Count.ShouldBeGreaterThan(100);
        keys.ShouldContain(k => k.Entity.GetTableName() == "attachments"
            && k.Key.Properties.Select(p => p.GetColumnName()).SequenceEqual(new[] { "feature_id" }));

        var unindexed = new List<string>();
        foreach (var (entity, key) in keys)
        {
            var columns = key.Properties.Select(p => p.GetColumnName()).ToList();
            var name = $"{entity.GetTableName()}({string.Join(", ", columns)})";
            if (UnindexedOnPurpose.ContainsKey(name) || IsServed(entity, columns))
            {
                continue;
            }

            unindexed.Add(name);
        }

        unindexed.ShouldBeEmpty(
            "These foreign keys lead no index that an equality on them can use (an index whose "
            + "predicate, if any, is only that the key is not null). Declare one, named:\n"
            + string.Join("\n", unindexed));
    }

    [Fact]
    public void No_index_is_keyed_only_by_a_column_its_filter_says_is_null()
    {
        using var context = new DesignTimeDbContextFactory().CreateDbContext([]);
        var indexes = context.Model.GetEntityTypes()
            .SelectMany(e => e.GetDeclaredIndexes().Select(i => (Entity: e, Index: i)))
            .ToList();
        indexes.Count.ShouldBeGreaterThan(100);

        // Every key of such an index is the same null, so it answers no selective question and is
        // only paid for on every write; the rows somebody looks up by that column are the others.
        var nullKeyed = indexes
            .Where(x => x.Index.Properties.Count == 1 && x.Index.GetFilter() is { } filter
                && Regex.IsMatch(filter.Trim().Trim('(', ')'),
                    $@"^""?{Regex.Escape(x.Index.Properties[0].GetColumnName())}""?\s+is\s+null$",
                    RegexOptions.IgnoreCase))
            .Select(x => $"{x.Entity.GetTableName()}: {x.Index.GetDatabaseName()} ({x.Index.GetFilter()})")
            .ToList();

        nullKeyed.ShouldBeEmpty(string.Join("\n", nullKeyed));
    }

    /// <summary>
    /// Whether some key or index of the table leads with exactly these columns, in any order, and is
    /// either unfiltered or filtered only on those columns being present — the one predicate an
    /// equality on them implies, so the planner may answer the equality from it.
    /// </summary>
    private static bool IsServed(IEntityType entity, IReadOnlyList<string> columns)
    {
        var wanted = columns.ToHashSet(StringComparer.Ordinal);
        var root = entity.GetRootType();
        var candidates = entity.GetIndexes()
            .Concat(root.GetIndexes())
            .Select(i => (Columns: i.Properties.Select(p => p.GetColumnName()).ToList(), Filter: i.GetFilter()))
            .Concat(entity.GetKeys().Concat(root.GetKeys())
                .Select(k => (Columns: k.Properties.Select(p => p.GetColumnName()).ToList(), Filter: (string?)null)));

        return candidates.Any(c =>
            c.Columns.Count >= wanted.Count
            && c.Columns.Take(wanted.Count).ToHashSet(StringComparer.Ordinal).SetEquals(wanted)
            && (c.Filter is null || OnlyNotNull(c.Filter, wanted)));
    }

    private static bool OnlyNotNull(string filter, IReadOnlySet<string> columns)
    {
        var terms = Regex.Split(filter.Trim().Trim('(', ')'), @"\s+and\s+", RegexOptions.IgnoreCase);
        return terms.All(t =>
        {
            var match = Regex.Match(t.Trim().Trim('(', ')'), @"^""?(\w+)""?\s+is\s+not\s+null$", RegexOptions.IgnoreCase);
            return match.Success && columns.Contains(match.Groups[1].Value);
        });
    }

    private sealed record Declaration(string File, int Configuration, string Properties, bool Named);

    /// <summary>
    /// Every <c>HasIndex(...)</c> call of a configuration file, with its first argument normalised
    /// (the lambda's parameter renamed, whitespace removed) and whether a model name follows it.
    /// Each <c>Configure</c> method is its own scope, because two entities may each index a column
    /// of the same name.
    /// </summary>
    private static IEnumerable<Declaration> Declarations(string file, string source)
    {
        var scopes = source.Split("void Configure(");
        for (var scope = 1; scope < scopes.Length; scope++)
        {
            var text = scopes[scope];
            foreach (Match call in Regex.Matches(text, @"\bHasIndex\("))
            {
                var arguments = Arguments(text, call.Index + call.Length);
                if (arguments.Count == 0)
                {
                    continue;
                }

                yield return new Declaration(file, scope, Normalise(arguments[0]), arguments.Count > 1);
            }
        }
    }

    /// <summary>The top-level arguments of a call, read from just after its opening bracket.</summary>
    private static List<string> Arguments(string text, int start)
    {
        var arguments = new List<string>();
        var current = new StringBuilder();
        var depth = 0;
        var inString = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                current.Append(c);
                if (c == '"' && text[i - 1] != '\\')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    current.Append(c);
                    break;
                case '(' or '{' or '[':
                    depth++;
                    current.Append(c);
                    break;
                case ')' or '}' or ']' when depth > 0:
                    depth--;
                    current.Append(c);
                    break;
                case ')':
                    arguments.Add(current.ToString());
                    return arguments;
                case ',' when depth == 0:
                    arguments.Add(current.ToString());
                    current.Clear();
                    break;
                default:
                    current.Append(c);
                    break;
            }
        }

        return arguments;
    }

    private static string Normalise(string argument)
    {
        var compact = Regex.Replace(argument, @"\s+", "");
        var lambda = Regex.Match(compact, @"^(\w+)=>");
        if (!lambda.Success)
        {
            return compact;
        }

        var parameter = lambda.Groups[1].Value;
        var body = compact[lambda.Length..];
        return Regex.Replace(body, $@"\b{Regex.Escape(parameter)}\.", "_.");
    }
}
