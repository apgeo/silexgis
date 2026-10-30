// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Architecture.Tests;

/// <summary>
/// An access row names other rows — an object, a club, a set, a cabinet, an account — mostly
/// without a foreign key. One list says what can be named and how, and the removal of rules with
/// a row that goes for good, the search for rules left naming nothing, and the integrity report
/// are all built on it. These tests are what stop that list falling behind the model.
/// </summary>
/// <remarks>
/// A reference the list does not know is not removed when its row goes and is not found
/// afterwards either: it fails silently in both directions. So every vocabulary that decides what
/// an access row can point at is swept here, and a new member fails until it is placed.
/// </remarks>
public class AccessAnchorCoverageTests
{
    [Fact]
    public void Every_domain_that_admits_an_object_rule_has_an_arm_naming_its_table()
    {
        var domains = Enum.GetValues<AccessDomain>()
            .Where(d => AccessEntryRules.AllowsObjectScope(d) && d != AccessDomain.Features)
            .ToList();

        // The fixture half: the rule admits object scope somewhere, the trip among them.
        domains.ShouldContain(AccessDomain.TripLogs);

        var arms = AccessAnchors.Arms.Where(a => a.Column == AccessReferenceColumn.ObjectScope)
            .Select(a => a.Domain).ToList();
        domains.Except(arms).ShouldBeEmpty(
            "these domains admit an object-scoped rule, but nothing says which table its scope id names, "
            + "so the rule would outlive its object unnoticed. Add an arm to AccessAnchors.Arms");
        arms.Except(domains).ShouldBeEmpty("an arm for a domain that admits no object rule");
        arms.Count.ShouldBe(arms.Distinct().Count(), "one arm per domain; list several targets on it instead");
    }

    [Fact]
    public void Every_protected_type_is_a_target_of_the_object_arm_of_the_domain_that_governs_it()
    {
        using var context = new DesignTimeDbContextFactory().CreateDbContext([]);
        var governed = context.Model.GetEntityTypes()
            .Select(e => e.ClrType)
            .Where(t => typeof(IProtectedEntity).IsAssignableFrom(t) && !t.IsAbstract)
            .Select(t => (Type: t, Domain: AccessDomains.Of((IProtectedEntity)RuntimeHelpers.GetUninitializedObject(t))))
            .Where(x => x.Domain != AccessDomain.Features)
            .ToList();

        // The fixture half: the album is governed by the document domain, which is the case an arm
        // with one target would get wrong.
        governed.ShouldContain((typeof(Album), AccessDomain.Documents));

        var missing = governed
            .Where(x => !AccessAnchors.Arms.Any(a =>
                a.Column == AccessReferenceColumn.ObjectScope && a.Domain == x.Domain && a.Targets.Contains(x.Type)))
            .Select(x => $"{x.Type.Name} ({x.Domain})")
            .ToList();
        missing.ShouldBeEmpty(
            "an object rule in this domain is matched against these rows by the evaluator, so a rule naming "
            + "one of them is not dangling while the row exists — list the type on its domain's arm: "
            + string.Join(", ", missing));
    }

    /// <summary>The scope kinds whose anchor is not an FK-less id, and where they are covered instead.</summary>
    private static readonly IReadOnlyDictionary<AccessScopeKind, string> ScopesWithoutAnIdArm =
        new Dictionary<AccessScopeKind, string>
        {
            [AccessScopeKind.All] = "names nothing",
            [AccessScopeKind.Own] = "names nothing; the owner is a column of the row evaluated",
            [AccessScopeKind.Subtree] = "names a feature through the restricting scope_feature_id key (the feature arm)",
            [AccessScopeKind.Object] = "names a row of the entry's own domain (the object arms)",
        };

    [Fact]
    public void Every_scope_kind_is_either_an_arm_or_a_listed_exception()
    {
        var arms = AccessAnchors.Arms.Where(a => a.Column == AccessReferenceColumn.ScopeId)
            .Select(a => a.ScopeKind).ToList();
        var unplaced = Enum.GetValues<AccessScopeKind>()
            .Where(k => !arms.Contains(k) && !ScopesWithoutAnIdArm.ContainsKey(k))
            .ToList();
        unplaced.ShouldBeEmpty(
            "a scope kind that names a row by id needs an arm in AccessAnchors.Arms, or a line here saying "
            + "why it names nothing: " + string.Join(", ", unplaced));
        arms.Intersect(ScopesWithoutAnIdArm.Keys).ShouldBeEmpty("listed as an exception and given an arm");
        arms.ShouldContain(AccessScopeKind.Cabinet);
        AccessAnchors.Arms.ShouldContain(a => a.Column == AccessReferenceColumn.ScopeFeature);
    }

    [Fact]
    public void Every_subject_kind_has_an_arm_for_direct_rules_and_one_for_ruleset_members()
    {
        foreach (var kind in Enum.GetValues<AccessSubjectKind>())
        {
            AccessAnchors.Arms.ShouldContain(
                a => a.Column == AccessReferenceColumn.Subject && a.SubjectKind == kind,
                $"no arm for rules whose subject is a {kind}");
            AccessAnchors.Arms.ShouldContain(
                a => a.Column == AccessReferenceColumn.PermissionMember && a.SubjectKind == kind,
                $"no arm for ruleset members that are a {kind}");
        }
    }

    [Fact]
    public void Every_target_is_a_mapped_table_keyed_by_one_uuid()
    {
        using var context = new DesignTimeDbContextFactory().CreateDbContext([]);
        foreach (var target in AccessAnchors.Arms.SelectMany(a => a.Targets).Distinct())
        {
            var entity = context.Model.FindEntityType(target).ShouldNotBeNull($"{target.Name} is not mapped");
            var key = entity.FindPrimaryKey().ShouldNotBeNull();
            key.Properties.Count.ShouldBe(1, $"{target.Name} has a composite key");
            key.Properties[0].ClrType.ShouldBe(typeof(Guid), $"{target.Name} is not keyed by a uuid");
        }
    }

    /// <summary>
    /// A database cascade removes rows without the change tracker seeing them, so a rule naming a
    /// row deleted that way is reached only if the cascade is declared. The model is walked from
    /// every row a rule can name, backwards over every cascading key, and what is found has to be
    /// exactly what is declared.
    /// </summary>
    [Fact]
    public void Every_cascade_that_removes_a_named_row_is_declared()
    {
        using var context = new DesignTimeDbContextFactory().CreateDbContext([]);
        var found = new HashSet<(Type Principal, Type Dependent, string Key)>();
        var pending = new Queue<Type>(AccessAnchors.Arms.SelectMany(a => a.Targets).Distinct());
        var visited = new HashSet<Type>();
        while (pending.TryDequeue(out var dependent))
        {
            if (!visited.Add(dependent))
            {
                continue;
            }

            foreach (var key in context.Model.FindEntityType(dependent)!.GetForeignKeys()
                         .Where(k => k.DeleteBehavior is DeleteBehavior.Cascade or DeleteBehavior.ClientCascade))
            {
                found.Add((key.PrincipalEntityType.ClrType, dependent, key.Properties.Single().Name));
                pending.Enqueue(key.PrincipalEntityType.ClrType);
            }
        }

        // The fixture half: the cascade this was written against is found.
        found.ShouldContain((typeof(Infrastructure.Identity.SilexGisUser), typeof(MapView), nameof(MapView.OwnerUserId)));

        var declared = AccessAnchors.Cascades.Select(c => (c.Principal, c.Dependent, c.ForeignKey)).ToHashSet();
        found.Except(declared).Select(Describe).ShouldBeEmpty(
            "a database cascade removes rows an access rule can name, and the cleanup does not follow it — "
            + "declare it in AccessAnchors.Cascades");
        declared.Except(found).Select(Describe).ShouldBeEmpty("declared, but the model has no such cascade");

        static string Describe((Type Principal, Type Dependent, string Key) c) =>
            $"{c.Principal.Name} -> {c.Dependent.Name}.{c.Key}";
    }

    /// <summary>
    /// The places that delete rows a rule can name with a statement of their own — which the
    /// interceptor never sees — and why each is safe. Each must ask the cleanup itself.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (int Statements, string Reason)> SetBasedDeletes =
        new Dictionary<string, (int, string)>
        {
            ["server/src/SilexGis.Infrastructure/Features/FeatureWriteService.cs"] =
                (1, "the subtree purge deletes features in one statement after removing their rules"),
            ["server/src/SilexGis.Infrastructure/Jobs/DocumentPurgeHandler.cs"] =
                (1, "the purge deletes a document in one statement after removing the rules on it and its files"),
        };

    [Fact]
    public void Every_set_based_delete_of_a_row_a_rule_can_name_asks_the_cleanup_first()
    {
        using var context = new DesignTimeDbContextFactory().CreateDbContext([]);
        var sources = AccessAnchors.Arms.SelectMany(a => a.Targets)
            .Concat(AccessAnchors.Cascades.Select(c => c.Principal))
            .ToHashSet();
        var sets = typeof(SilexGisDbContext).GetProperties()
            .Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
            .ToDictionary(p => p.Name, p => p.PropertyType.GetGenericArguments()[0]);
        sets.ShouldContainKey(nameof(SilexGisDbContext.Documents));

        // The first set a statement reads is the one a set-based delete removes rows from.
        var firstSet = new Regex($@"(?<!AccessDomain|VersionedTable|AttachedEntityType)\.(?<set>{string.Join('|', sets.Keys)})\b");
        var tables = context.Model.GetEntityTypes()
            .Where(e => sources.Contains(e.ClrType))
            .Select(e => e.GetTableName()!)
            .ToList();
        var rawDelete = new Regex(
            $@"\bdelete\s+from\s+""?(?:{string.Join('|', tables.Select(Regex.Escape))})\b", RegexOptions.IgnoreCase);

        var found = new Dictionary<string, int>();
        var offenders = new List<string>();
        foreach (var (file, source) in TripSoftDeleteGuardTests.Sources())
        {
            var (code, literals) = CSharpText.Split(source);
            var statements = code.Split(';').Count(statement =>
                statement.Contains(".ExecuteDelete", StringComparison.Ordinal)
                && firstSet.Match(statement) is { Success: true } m
                && sources.Contains(sets[m.Groups["set"].Value]));
            if (statements > 0)
            {
                found[file] = statements;
            }

            offenders.AddRange(literals.Where(l => rawDelete.IsMatch(l)).Select(_ => $"{file}: a raw DELETE"));
        }

        foreach (var (file, statements) in found)
        {
            if (!SetBasedDeletes.TryGetValue(file, out var listed))
            {
                offenders.Add($"{file}: {statements} statement(s), not listed");
            }
            else if (listed.Statements != statements)
            {
                offenders.Add($"{file}: {statements} statement(s), listed as {listed.Statements}");
            }
        }

        offenders.AddRange(SetBasedDeletes.Keys.Where(f => !found.ContainsKey(f))
            .Select(f => $"{f}: listed, but deletes nothing that way any more"));
        offenders.AddRange(SetBasedDeletes.Keys.Where(found.ContainsKey)
            .Where(f => !TripSoftDeleteGuardTests.Sources().Single(s => s.File == f).Source
                .Contains($"{nameof(AccessAnchorCleanup)}.{nameof(AccessAnchorCleanup.RemoveRowsNamingAsync)}", StringComparison.Ordinal))
            .Select(f => $"{f}: deletes by statement but never asks {nameof(AccessAnchorCleanup)}"));

        offenders.ShouldBeEmpty(
            "a statement that deletes rows by itself passes no interceptor, so the access rules naming those "
            + "rows would be left behind. Remove them through the change tracker instead, or ask "
            + "AccessAnchorCleanup and save before the statement and list the file here: "
            + string.Join("; ", offenders));
    }

    /// <summary>
    /// The write-time gate asks, before a rule is saved, whether its anchor exists and what the
    /// author holds over it. Both answers must cover every table the anchor list says a rule may
    /// name: a table the gate does not look in refuses a legitimate rule as naming nothing, and a
    /// gate that admits what it does not know lets a rule on a missing row through.
    /// </summary>
    [Fact]
    public void Every_table_a_rule_names_by_id_has_a_reader_in_the_write_gate()
    {
        var targets = AccessAnchors.Arms
            .Where(a => a.Column is AccessReferenceColumn.ObjectScope or AccessReferenceColumn.ScopeId)
            .SelectMany(a => a.Targets)
            .Distinct()
            .ToList();

        // The fixture half: the second tables of three domains, which the gate once never looked
        // in, and a scope kind's table.
        targets.ShouldContain(typeof(Album));
        targets.ShouldContain(typeof(SavedFilter));
        targets.ShouldContain(typeof(TripRecording));
        targets.ShouldContain(typeof(Cabinet));

        var readers = SilexGis.Api.Features.Permissions.AccessEntryMapping.AnchorReaders;
        var unread = targets.Where(t => !readers.ContainsKey(t)).Select(t => t.Name).ToList();
        unread.ShouldBeEmpty(
            "a rule may be anchored on these tables, but the write gate cannot tell whether such a row "
            + "exists, so the rule is refused as naming nothing. Add a reader to AccessEntryMapping.AnchorReaders: "
            + string.Join(", ", unread));
        readers.Keys.Except(targets).Select(t => t.Name).ShouldBeEmpty(
            "a reader for a table no arm of the anchor list names");
    }

    [Fact]
    public void Every_anchor_table_with_access_columns_has_them_read_by_the_write_gate()
    {
        var readers = SilexGis.Api.Features.Permissions.AccessEntryMapping.AnchorReaders;

        // The fixture half: one table of each shape.
        readers.Keys.ShouldContain(typeof(Album));
        readers.Keys.ShouldContain(typeof(StoredFile));

        var unreadColumns = readers
            .Where(r => typeof(IProtectedEntity).IsAssignableFrom(r.Key) && r.Value.ColumnsAsync is null)
            .Select(r => r.Key.Name)
            .ToList();
        unreadColumns.ShouldBeEmpty(
            "these rows carry an owner, a club and an audience, and an author's right to write a rule on "
            + "one is largely made of them; facts naming only the id refuse the row's own owner: "
            + string.Join(", ", unreadColumns));
        readers
            .Where(r => !typeof(IProtectedEntity).IsAssignableFrom(r.Key) && r.Value.ColumnsAsync is not null)
            .Select(r => r.Key.Name)
            .ShouldBeEmpty("a column reader for a table that has no access columns");
    }
}
