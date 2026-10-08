// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Api.Features.TripTracking;
using SilexGis.Domain.Trips;

namespace SilexGis.Api.Tests;

/// <summary>
/// Holds the installation guide's table of why a published read is refused to the application's
/// own list of reasons.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it is for.</b> An operator with a link that opens nothing reads a word in the request
/// log and looks it up in that table. The words are produced in one place in the code and written
/// out by hand in the guide; nothing else connects the two. A reason added to the code and not to
/// the table is a word in a log that the guide does not explain, and a row whose word was renamed
/// in the code sends the operator looking for a line the server no longer writes.
/// </para>
/// <para>
/// <b>What it reads.</b> The words come from the application itself — every member of the list of
/// refusals, through the routine that words it for the log — not from a second list kept here.
/// The table is found by its header row and its reason column is read: a cell names a reason when
/// it opens with a word in code spelling, and names none when it opens with a remark in brackets,
/// which is how the table writes the causes that leave no reason line at all.
/// </para>
/// <para>
/// <b>What it does not read.</b> That what a row says about its reason is true, or that the route
/// a row names is the one that produces it. It checks that the two lists name the same words.
/// </para>
/// </remarks>
public class PublishedReadRefusalDocumentationTests
{
    /// <summary>The header row the table is found by, as the guide writes it.</summary>
    private const string Header = "| What happened | Reason | How to tell, and what to do |";

    [Fact]
    public void The_guide_explains_every_reason_a_published_read_is_refused_and_no_other()
    {
        var guide = File.ReadAllText(Path.Combine(WorkingTree(), "docs", "INSTALL.md"));
        var documented = ReasonsIn(guide);
        var produced = Enum.GetValues<PublishedReadRefusal>().Select(PublicTripDiagnostics.WordOf).ToList();

        // A table that was not found, or was found and read as empty, must not pass as "nothing
        // to disagree about".
        documented.ShouldNotBeEmpty("docs/INSTALL.md has no table headed: " + Header);
        produced.Count.ShouldBeGreaterThan(1);

        var (unexplained, unknown) = Differences(documented, produced);
        unexplained.ShouldBeEmpty(
            "The application refuses a published read for these reasons, and the table of causes in "
            + "docs/INSTALL.md has no row for them. Add a row for each:\n  " + string.Join("\n  ", unexplained) + "\n");
        unknown.ShouldBeEmpty(
            "The table of causes in docs/INSTALL.md names these as reasons, and the application writes no "
            + "such word. Correct the row, or open its reason cell with a remark in brackets if the cause "
            + "leaves no reason line:\n  " + string.Join("\n  ", unknown) + "\n");
    }

    /// <summary>
    /// The reading of the table, on a table made up for the purpose: which cells name a reason,
    /// which name none, and where the table ends.
    /// </summary>
    [Fact]
    public void A_reason_cell_names_its_leading_word_and_a_bracketed_remark_names_none()
    {
        var text = string.Join('\n',
            "Some words, with `not_a_reason` in them.",
            "",
            "| Other table | Reason |",
            "|---|---|",
            "| not this one | `elsewhere` |",
            "",
            Header,
            "|---|---|---|",
            "| A plain row | `alpha_one` | look at `something_else` |",
            "| A row that names a route | `beta_two`, on route `follow` only | nothing |",
            "| No reason line | *(none — every read is `served`)* | nothing |",
            "| A counter, not a reason | *(no reason line)* `some.counter` moves | nothing |",
            "| The same reason on a second row | `alpha_one` | nothing |",
            "",
            "| A table after it | Reason | x |",
            "|---|---|---|",
            "| not this one either | `after_the_end` | x |");

        ReasonsIn(text).ShouldBe(["alpha_one", "beta_two"]);
        ReasonsIn("No table here, only `words`.").ShouldBeEmpty();
    }

    /// <summary>
    /// The comparison sees a difference in either direction — which the real table, being right
    /// today, cannot show.
    /// </summary>
    [Fact]
    public void A_reason_missing_from_either_list_is_reported_on_its_own_side()
    {
        var same = Differences(documented: ["a", "b"], produced: ["b", "a"]);
        same.Unexplained.ShouldBeEmpty();
        same.Unknown.ShouldBeEmpty();

        var (unexplained, unknown) = Differences(
            documented: ["a", "renamed_in_the_code"], produced: ["a", "new_in_the_code"]);
        unexplained.ShouldBe(["new_in_the_code"]);
        unknown.ShouldBe(["renamed_in_the_code"]);
    }

    /// <summary>
    /// The reasons the table names, each once, in the table's order. Empty when the text holds no
    /// table under the header.
    /// </summary>
    private static List<string> ReasonsIn(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var start = Array.FindIndex(lines, line => line.Trim() == Header);
        if (start < 0)
        {
            return [];
        }

        var reasons = new List<string>();
        // The row under the header is the rule of dashes; the table ends at the first line that
        // is not a row.
        foreach (var line in lines.Skip(start + 2).TakeWhile(line => line.TrimStart().StartsWith('|')))
        {
            var cells = line.Split('|');
            if (cells.Length < 3)
            {
                continue;
            }

            var cell = cells[2].Trim();
            if (!cell.StartsWith('`'))
            {
                continue;
            }

            var end = cell.IndexOf('`', 1);
            if (end > 1 && cell[1..end] is var word && !reasons.Contains(word))
            {
                reasons.Add(word);
            }
        }

        return reasons;
    }

    private static (List<string> Unexplained, List<string> Unknown) Differences(
        IReadOnlyCollection<string> documented, IReadOnlyCollection<string> produced) =>
        (produced.Except(documented).Order(StringComparer.Ordinal).ToList(),
            documented.Except(produced).Order(StringComparer.Ordinal).ToList());

    /// <summary>
    /// The working tree, found by walking up from the assembly: the guide is the one beside the
    /// code under test, not a copy in a build output directory.
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
