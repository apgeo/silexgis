// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Architecture.Tests;

/// <summary>
/// A deleted trip stays in its table, and what keeps it out of sight is a rule of the model. These
/// tests are what stop the next change from walking round that rule without noticing.
/// </summary>
/// <remarks>
/// <para>
/// There are exactly three ways a deleted trip can come back into view, and one test stands at
/// each. A table keyed by a trip that nobody remembered to hide with it. A statement written by
/// hand, which the model's rule never sees. And a read that switches the rule off — which is
/// sometimes right, and is therefore kept as a list of the places where it is, each with its
/// reason, so that a new one is a decision somebody made rather than a line somebody added.
/// </para>
/// <para>
/// The trip's tables are taken from the model rather than written down here, so a table added
/// next year is covered the day it is mapped.
/// </para>
/// </remarks>
public class TripSoftDeleteGuardTests
{
    // ---- 1. every table keyed by a trip is hidden with it

    /// <summary>
    /// The tables that hold a pointer at a trip and are deliberately not hidden with it.
    /// </summary>
    private static readonly IReadOnlyDictionary<Type, string> KeptInSight = new Dictionary<Type, string>
    {
        [typeof(ImportBatch)] =
            "the record of what one confirmation did; it outlives the trip a drop was filed under, "
            + "and its own page is where a deleted trip is pointed back to",
        [typeof(ImportBatchItem)] =
            "one line of that record; it has to go on naming a deleted trip, because the line is "
            + "how somebody undoing an import finds what to put back",
    };

    [Fact]
    public void Every_table_keyed_by_a_trip_is_hidden_while_its_trip_is_deleted()
    {
        using var context = new DesignTimeDbContextFactory().CreateDbContext([]);
        var trip = context.Model.FindEntityType(typeof(TripLog)).ShouldNotBeNull();
        trip.GetDeclaredQueryFilters().Any().ShouldBeTrue("the trip itself is not filtered");

        var keyedByATrip = KeyedByATrip(context.Model).ToList();

        // The fixture half: the model really does hold tables keyed by a trip, the ones this was
        // written against among them. A model walk that found none would pass every line below.
        keyedByATrip.Select(e => e.ClrType).ShouldContain(typeof(TripLogParticipant));
        keyedByATrip.Select(e => e.ClrType).ShouldContain(typeof(TripTrackingShare));
        keyedByATrip.Select(e => e.ClrType).ShouldContain(typeof(ExpeditionTrip));

        var unfiltered = keyedByATrip
            .Where(e => !e.GetDeclaredQueryFilters().Any())
            .Select(e => e.ClrType)
            .ToList();
        var unexplained = unfiltered.Where(type => !KeptInSight.ContainsKey(type)).Select(type => type.Name).ToList();
        unexplained.ShouldBeEmpty(
            "these tables are keyed by a trip and carry no filter, so their rows stay readable while "
            + "the trip is deleted. Hide them with the trip, the way a roster row is, or say here "
            + "why they must stay in sight: " + string.Join(", ", unexplained));

        // And the list does not outlive what it excuses.
        var stale = KeptInSight.Keys.Where(type => !unfiltered.Contains(type)).Select(type => type.Name).ToList();
        stale.ShouldBeEmpty("listed as kept in sight, but filtered or no longer keyed by a trip: " + string.Join(", ", stale));

        // A filter that is declared is not yet a filter that hides anything, and a key that may
        // be empty is where the difference lies. Such a key is followed by a join that keeps the
        // row whether or not it finds a trip, and it joins to the trips a reader may see — so a
        // deleted trip is simply not found, every column read through it is empty, and "its trip
        // is not deleted" is true of the very rows it was written to hide. That filter was here
        // once and hid nothing. What hides the row is saying that the trip must be there.
        var throughAnEmptiableKey = keyedByATrip
            .Where(e => e.GetDeclaredQueryFilters().Any())
            .SelectMany(e => e.GetForeignKeys()
                .Where(key => key.PrincipalEntityType.ClrType == typeof(TripLog) && !key.IsRequired)
                .Select(key => (Entity: e, Navigation: key.DependentToPrincipal?.Name)))
            .ToList();

        // The fixture half: there is such a table, the one this was written for.
        throughAnEmptiableKey.Select(x => x.Entity.ClrType).ShouldContain(typeof(TripInvitation));

        var vacuous = throughAnEmptiableKey
            .Where(x => x.Navigation is null
                || !x.Entity.GetDeclaredQueryFilters().Any(filter =>
                    filter.Expression?.ToString().Contains($".{x.Navigation} != null", StringComparison.Ordinal) == true))
            .Select(x => x.Entity.ClrType.Name)
            .ToList();
        vacuous.ShouldBeEmpty(
            "these tables reach their trip through a key that may be empty, and their filter does not "
            + "say the trip must be there (`x.TripLog != null && …`), so it is true of a deleted trip's "
            + "rows and hides none of them: " + string.Join(", ", vacuous));
    }

    // ---- 2. hand-written SQL states the condition itself

    /// <summary>
    /// The statements that name a trip's table without stating the condition, and why each may.
    /// Keyed by file and table. Empty on purpose: the one statement there is states it.
    /// </summary>
    private static readonly IReadOnlyDictionary<(string File, string Table), string> SeesDeletedRows =
        new Dictionary<(string File, string Table), string>();

    [Fact]
    public void Hand_written_sql_naming_a_trip_table_states_the_deleted_condition()
    {
        using var context = new DesignTimeDbContextFactory().CreateDbContext([]);
        var tables = TripTables(context.Model);
        tables.ShouldContain("trip_logs");
        tables.ShouldContain("trip_log_participants");
        var naming = new Regex($@"\b(?:{string.Join('|', tables.Select(Regex.Escape))})\b", RegexOptions.IgnoreCase);

        var seen = new List<string>();
        var offenders = new List<string>();
        var used = new HashSet<(string, string)>();
        foreach (var (file, source) in Sources())
        {
            foreach (var literal in CSharpText.Split(source).Literals)
            {
                var named = naming.Matches(literal).Select(m => m.Value.ToLowerInvariant()).Distinct().ToList();
                if (named.Count == 0 || IsOnlyAName(literal))
                {
                    // A literal that is nothing but the table's name is the mapping that names
                    // the table to the model — the thing the filter is declared against, not a
                    // statement that could bypass it.
                    continue;
                }

                seen.Add(file);
                if (literal.Contains("deleted_at", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var table in named)
                {
                    if (SeesDeletedRows.ContainsKey((file, table)))
                    {
                        used.Add((file, table));
                    }
                    else
                    {
                        offenders.Add($"{file} names {table}");
                    }
                }
            }
        }

        // The fixture half: the scan reads the statement that is known to exist. A scan pointed
        // at the wrong directory finds nothing and would otherwise pass for ever.
        seen.ShouldContain("server/src/SilexGis.Api/Common/ConcurrencySql.cs");

        offenders.ShouldBeEmpty(
            "a statement written by hand does not pass through the model's filter, so one that names "
            + "a trip's table has to say `deleted_at` itself (join the trip and state the condition), "
            + "or be listed here with the reason it must see deleted rows: " + string.Join("; ", offenders));

        var stale = SeesDeletedRows.Keys.Where(key => !used.Contains(key)).Select(key => $"{key.File} / {key.Table}").ToList();
        stale.ShouldBeEmpty("listed as seeing deleted rows, but no such statement is left: " + string.Join("; ", stale));
    }

    // ---- 3. every read past the filter is a listed decision

    /// <summary>
    /// The places that read a trip's tables with the model's filters switched off, how many
    /// statements each holds, and why it has to see a deleted trip's rows.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (int Statements, string Reason)> ReadsPastTheFilter =
        new Dictionary<string, (int, string)>
        {
            ["server/src/SilexGis.Api/Features/TripLogs/TripLogDeletionEndpoints.cs"] =
                (2, "the list of deleted trips, and finding the one being put back"),
            ["server/src/SilexGis.Api/Features/Import/ImportBatchEndpoints.cs"] =
                (1, "an import's own page says which of its trips are deleted and can be put back"),
            ["server/src/SilexGis.Api/Features/Cavers/CaverEndpoints.cs"] =
                (11, "a person is held in place by the rows of a deleted trip, and a merge has to move them "
                    + "— the roster, the answers, the reports, the captions and the party numbers"),
            ["server/src/SilexGis.Api/Features/Taxonomies/TripParticipantRoleEndpoints.cs"] =
                (1, "a role held on a deleted trip's roster is still in use"),
            ["server/src/SilexGis.Api/Features/Permissions/AccessEntryMapping.cs"] =
                (1, "a rule on a deleted trip is still judged against that trip's owner and audience"),
            ["server/src/SilexGis.Infrastructure/Trips/TripLogWriteService.cs"] =
                (1, "removing a trip for good removes its place in a camp"),
            ["server/src/SilexGis.Infrastructure/Trips/TripTypeWriteService.cs"] =
                (1, "a purpose a deleted trip names is still in use"),
            ["server/src/SilexGis.Infrastructure/Jobs/TripPurgeHandler.cs"] =
                (3, "the pass that removes deleted trips: selecting them, claiming one, loading it"),
            ["server/src/SilexGis.Infrastructure/Import/ImportCommitService.cs"] =
                (1, "an undo takes back the positions an import wrote, onto a deleted trip as onto a live one"),
            ["server/src/SilexGis.Infrastructure/Features/FeatureIntegrityVerifier.cs"] =
                (2, "a deleted trip still anchors the rules, files, tags and links kept on it"),
            ["server/src/SilexGis.Infrastructure/Permissions/PhotoPositionDisclosure.cs"] =
                (1, "a picture filed under a camp stays guarded by the places a deleted member trip names"),
            ["server/src/SilexGis.Api/Features/TripTracking/TripTrackingEndpoints.cs"] =
                (1, "a report taken off the log is hidden by the same filter as a deleted trip's rows; listing "
                    + "the removed ones to those who may write the log, putting one back and destroying one "
                    + "all start from a single set that asks past it and says again that the trip is not deleted"),
            ["server/src/SilexGis.Api/Features/TripTracking/PublishedLinksAdminEndpoints.cs"] =
                (1, "withdrawing every published link takes a deleted trip's links too, so that restoring "
                    + "the trip reopens no address an administrator was told had been taken back"),
        };

    [Fact]
    public void Every_read_of_a_trips_tables_past_the_filter_is_a_listed_decision()
    {
        using var context = new DesignTimeDbContextFactory().CreateDbContext([]);
        var family = new[] { context.Model.FindEntityType(typeof(TripLog))! }
            .Concat(KeyedByATrip(context.Model))
            .Select(e => e.ClrType)
            .ToHashSet();

        // The context's own set properties for those tables, and the generic spelling of the same.
        var sets = typeof(SilexGisDbContext).GetProperties()
            .Where(p => p.PropertyType.IsGenericType
                && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>)
                && family.Contains(p.PropertyType.GetGenericArguments()[0]))
            .Select(p => p.Name)
            .ToList();
        sets.ShouldContain(nameof(SilexGisDbContext.TripLogs));
        sets.ShouldContain(nameof(SilexGisDbContext.ExpeditionTrips));

        // A member access, so that the access domain and the versioned-table name that happen to
        // be spelt the same as a set are not mistaken for one.
        var mentions = new Regex(
            $@"(?<!AccessDomain)(?<!VersionedTable)\.(?:{string.Join('|', sets)})\b"
            + $@"|\bSet<(?:{string.Join('|', family.Select(type => type.Name))})>");

        var found = new Dictionary<string, int>();
        foreach (var (file, source) in Sources())
        {
            // Whole statements, because switching the filters off switches them off for every
            // table the statement reads: a query over caves that reaches a trip in a subquery
            // sees deleted trips exactly as one that starts from them.
            var statements = CSharpText.Split(source).Code.Split(';')
                .Count(statement => statement.Contains(".IgnoreQueryFilters(", StringComparison.Ordinal)
                    && mentions.IsMatch(statement));
            if (statements > 0)
            {
                found[file] = statements;
            }
        }

        var report = new List<string>();
        foreach (var (file, statements) in found)
        {
            if (!ReadsPastTheFilter.TryGetValue(file, out var listed))
            {
                report.Add($"{file}: {statements} statement(s), not listed");
            }
            else if (listed.Statements != statements)
            {
                report.Add($"{file}: {statements} statement(s), listed as {listed.Statements}");
            }
        }

        report.AddRange(ReadsPastTheFilter.Keys.Where(file => !found.ContainsKey(file))
            .Select(file => $"{file}: listed, but reads nothing past the filter any more"));

        report.ShouldBeEmpty(
            "a statement that reads a trip's tables with the filters off sees deleted trips. Where "
            + "that is intended, list the file here with the count and the reason; where it is not, "
            + "take the trip's tables out of the statement or leave the filters on: "
            + string.Join("; ", report));
    }

    // ---- the reader these scans rest on

    /// <summary>
    /// The scans above read C# as text, so they are only as good as the reader's idea of where a
    /// string starts and stops. These are the shapes that would fool a naive one: a quote inside
    /// a comment, a comment marker inside a string, a statement that spans lines in a raw string,
    /// and a hole in an interpolated string that holds a string of its own.
    /// </summary>
    [Fact]
    public void The_source_reader_tells_code_strings_and_comments_apart()
    {
        var source = string.Join('\n',
            "var a = \"select 1 // not a comment; from x\"; // \"not a string\"",
            "/* \"nor this\"; */ var b = @\"path\\\"\"quoted\"\" ;\";",
            "var c = \"\"\"",
            "    SELECT 1; FROM trip_logs",
            "    \"\"\";",
            "var d = $\"x{(y ? \"in;ner\" : \"other\")}z;\";",
            "var e = '\"'; var f = ';';",
            "db.TripLogs.IgnoreQueryFilters();");

        var (code, literals) = CSharpText.Split(source);

        literals.ShouldContain("select 1 // not a comment; from x");
        literals.ShouldContain("path\\\"\"quoted\"\" ;");
        literals.ShouldContain(literal => literal.Contains("FROM trip_logs", StringComparison.Ordinal));
        literals.ShouldContain("in;ner");
        literals.ShouldNotContain(literal => literal.Contains("not a string", StringComparison.Ordinal));
        literals.ShouldNotContain(literal => literal.Contains("nor this", StringComparison.Ordinal));

        // No text of a string or a comment is left in the code, and the code that is there is.
        code.ShouldNotContain("select");
        code.ShouldNotContain("not a string");
        code.ShouldNotContain("in;ner");
        code.ShouldContain("db.TripLogs.IgnoreQueryFilters()");
        // One statement per declaration and one for the last line: the semicolons inside strings,
        // comments and character literals did not split anything.
        code.Split(';').Count(statement => !string.IsNullOrWhiteSpace(statement)).ShouldBe(7);
    }

    // ---- shared pieces

    /// <summary>Every entity type that holds a foreign key to the trip.</summary>
    private static IEnumerable<IEntityType> KeyedByATrip(IModel model) =>
        model.GetEntityTypes()
            .Where(e => e.ClrType != typeof(TripLog)
                && e.GetForeignKeys().Any(key => key.PrincipalEntityType.ClrType == typeof(TripLog)));

    /// <summary>
    /// The trip's own table and every table keyed by it — the names a hand-written statement
    /// would have to spell.
    /// </summary>
    private static List<string> TripTables(IModel model) =>
        [.. new[] { model.FindEntityType(typeof(TripLog))! }
            .Concat(KeyedByATrip(model))
            .Where(e => !KeptInSight.ContainsKey(e.ClrType))
            .Select(e => e.GetTableName()!)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    private static bool IsOnlyAName(string literal) => Regex.IsMatch(literal, @"^\s*[A-Za-z_][A-Za-z0-9_]*\s*$");

    /// <summary>
    /// Every source file of the application, by its path from the top of the working tree. The
    /// migrations are left out: they are the schema's own history, and a statement in one runs
    /// once, against a table as it was.
    /// </summary>
    private static IEnumerable<(string File, string Source)> Sources()
    {
        var root = WorkingTree();
        var sources = Path.Combine(root, "server", "src");
        foreach (var path in Directory.EnumerateFiles(sources, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
            if (relative.Contains("/Migrations/", StringComparison.Ordinal)
                || relative.Contains("/bin/", StringComparison.Ordinal)
                || relative.Contains("/obj/", StringComparison.Ordinal))
            {
                continue;
            }

            yield return (relative, File.ReadAllText(path));
        }
    }

    /// <summary>
    /// The working tree, found by walking up from where the tests run rather than by counting
    /// directories: the assembly runs from a build output directory whose depth is not this
    /// test's business.
    /// </summary>
    private static string WorkingTree()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "server", "SilexGis.slnx")))
        {
            directory = directory.Parent;
        }

        directory.ShouldNotBeNull("Could not find the working tree above the test assembly.");
        return directory.FullName;
    }
}

/// <summary>
/// Reads C# source as far as these guards need it read: which characters are code, and which are
/// the text of a string.
/// </summary>
/// <remarks>
/// Not a parser, and deliberately not a compiler package pulled in for the purpose. It knows the
/// forms a literal takes — ordinary, verbatim, interpolated, and raw with any number of quotes —
/// and the two kinds of comment, which is everything that decides whether a given semicolon or a
/// given table name is code. A hole in an interpolated string is read as code in its own right,
/// so a string inside one is found and the string around it still ends where it should.
/// </remarks>
internal static class CSharpText
{
    /// <summary>
    /// The source with every comment removed and every literal reduced to an empty one, and the
    /// text of each string literal as written.
    /// </summary>
    public static (string Code, List<string> Literals) Split(string source)
    {
        var code = new StringBuilder(source.Length);
        var literals = new List<string>();
        var position = 0;
        ReadCode(source, ref position, code, literals, untilClosingBrace: false);
        return (code.ToString(), literals);
    }

    private static void ReadCode(
        string source, ref int position, StringBuilder code, List<string> literals, bool untilClosingBrace)
    {
        var depth = 0;
        while (position < source.Length)
        {
            var current = source[position];
            var next = position + 1 < source.Length ? source[position + 1] : '\0';

            if (current == '/' && next == '/')
            {
                while (position < source.Length && source[position] != '\n')
                {
                    position++;
                }

                continue;
            }

            if (current == '/' && next == '*')
            {
                var end = source.IndexOf("*/", position + 2, StringComparison.Ordinal);
                position = end < 0 ? source.Length : end + 2;
                code.Append(' ');
                continue;
            }

            if (current == '#' && StartsItsLine(source, position))
            {
                // A directive runs to the end of its line and is free text from there on — a
                // region's title may hold an apostrophe that is not the start of a character.
                while (position < source.Length && source[position] != '\n')
                {
                    position++;
                }

                continue;
            }

            if (current == '\'')
            {
                position++;
                while (position < source.Length && source[position] != '\'')
                {
                    position += source[position] == '\\' ? 2 : 1;
                }

                position++;
                code.Append("' '");
                continue;
            }

            if (StartsString(source, position, out var dollars, out var verbatim, out var quoteAt))
            {
                position = quoteAt;
                ReadString(source, ref position, literals, dollars, verbatim);
                code.Append("\"\"");
                continue;
            }

            if (untilClosingBrace)
            {
                if (current == '{')
                {
                    depth++;
                }
                else if (current == '}')
                {
                    if (depth == 0)
                    {
                        return;
                    }

                    depth--;
                }
            }

            code.Append(current);
            position++;
        }
    }

    private static bool StartsItsLine(string source, int position)
    {
        for (var before = position - 1; before >= 0 && source[before] != '\n'; before--)
        {
            if (!char.IsWhiteSpace(source[before]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether a string literal starts here, counting the markers in front of its quote.</summary>
    private static bool StartsString(string source, int position, out int dollars, out bool verbatim, out int quoteAt)
    {
        dollars = 0;
        verbatim = false;
        quoteAt = position;
        while (quoteAt < source.Length && (source[quoteAt] == '$' || source[quoteAt] == '@'))
        {
            if (source[quoteAt] == '$')
            {
                dollars++;
            }
            else
            {
                verbatim = true;
            }

            quoteAt++;
        }

        return quoteAt < source.Length && source[quoteAt] == '"';
    }

    private static void ReadString(string source, ref int position, List<string> literals, int dollars, bool verbatim)
    {
        var quotes = 0;
        while (position + quotes < source.Length && source[position + quotes] == '"')
        {
            quotes++;
        }

        if (quotes >= 3)
        {
            // Raw: it ends at the next run of as many quotes, and nothing inside is escaped. A
            // hole in one is left as text — it cannot hold a run of quotes that long.
            var start = position + quotes;
            var end = source.IndexOf(new string('"', quotes), start, StringComparison.Ordinal);
            end = end < 0 ? source.Length : end;
            literals.Add(source[start..end]);
            position = Math.Min(source.Length, end + quotes);
            while (position < source.Length && source[position] == '"')
            {
                position++;
            }

            return;
        }

        position++;
        var text = new StringBuilder();
        while (position < source.Length)
        {
            var current = source[position];
            var next = position + 1 < source.Length ? source[position + 1] : '\0';

            if (current == '"')
            {
                if (verbatim && next == '"')
                {
                    text.Append("\"\"");
                    position += 2;
                    continue;
                }

                position++;
                break;
            }

            if (!verbatim && current == '\\')
            {
                text.Append(current).Append(next);
                position += 2;
                continue;
            }

            if (dollars > 0 && current == '{')
            {
                if (next == '{')
                {
                    text.Append("{{");
                    position += 2;
                    continue;
                }

                // A hole: code, read as code, with whatever strings it holds found on the way.
                position++;
                var hole = new StringBuilder();
                ReadCode(source, ref position, hole, literals, untilClosingBrace: true);
                position++;
                text.Append('{').Append(hole).Append('}');
                continue;
            }

            text.Append(current);
            position++;
        }

        literals.Add(text.ToString());
    }
}
