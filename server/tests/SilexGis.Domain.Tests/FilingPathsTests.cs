// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using static SilexGis.Domain.Documents.FilingPaths;

namespace SilexGis.Domain.Tests;

/// <summary>
/// What a path written by somebody else is allowed to become inside the filing tree.
///
/// <para>
/// Every refusal below is paired with the shape it is nearly identical to and that must be
/// allowed, because the failure mode of a path guard is not "it lets an attack through" — it
/// is "somebody tightened it until ordinary archives stopped importing, and nobody noticed
/// until a club handed one over".
/// </para>
/// </summary>
public class FilingPathsTests
{
    [Fact]
    public void A_relative_path_becomes_its_folders_with_the_file_name_dropped()
    {
        FolderSegmentsOf("1987/bulletins/march.pdf").ShouldBe(new[] { "1987", "bulletins" });

        // A bare file name contributes no folders at all, which is what an ordinary drop is.
        FolderSegmentsOf("march.pdf").ShouldBeEmpty();
        FolderSegmentsOf(null).ShouldBeEmpty();
        FolderSegmentsOf("   ").ShouldBeEmpty();
    }

    [Fact]
    public void Both_separators_are_separators_whatever_machine_wrote_the_path()
    {
        // An archive written on Windows and expanded on Linux: reading the backslashes as
        // ordinary characters would turn the whole thing into one very long file name, which
        // is exactly where a traversal hides.
        FolderSegmentsOf(@"1987\bulletins\march.pdf").ShouldBe(new[] { "1987", "bulletins" });
        FolderSegmentsOf(@"1987/bulletins\march.pdf").ShouldBe(new[] { "1987", "bulletins" });

        FileNameOf(@"1987\bulletins\march.pdf").ShouldBe("march.pdf");
        FileNameOf("1987/bulletins/march.pdf").ShouldBe("march.pdf");
        FileNameOf("march.pdf").ShouldBe("march.pdf");
    }

    [Fact]
    public void A_traversal_is_refused_rather_than_rewritten()
    {
        FolderSegmentsOf("../../../etc/passwd").ShouldBeNull();
        FolderSegmentsOf("1987/../../../etc/passwd").ShouldBeNull();
        FolderSegmentsOf(@"..\..\windows\system32\config").ShouldBeNull();

        // Not every dot is an escape: a folder whose name merely begins with dots is a folder.
        FolderSegmentsOf("..hidden/march.pdf").ShouldBe(new[] { "..hidden" });
        FolderSegmentsOf("1987/..2/march.pdf").ShouldBe(new[] { "1987", "..2" });
    }

    [Fact]
    public void A_rooted_path_names_a_place_rather_than_a_position_and_is_refused()
    {
        FolderSegmentsOf("/etc/passwd").ShouldBeNull();
        FolderSegmentsOf(@"\\server\share\file.pdf").ShouldBeNull();
        FolderSegmentsOf(@"C:\Windows\win.ini").ShouldBeNull();

        // Refused on every platform, not only the one whose spelling it is: the machine that
        // wrote the archive is not the machine reading it.
        FolderSegmentsOf("c:relative.pdf").ShouldBeNull();
    }

    [Fact]
    public void Doubled_and_trailing_separators_name_the_same_place_as_single_ones()
    {
        FolderSegmentsOf("1987//bulletins///march.pdf").ShouldBe(new[] { "1987", "bulletins" });
        FolderSegmentsOf("./1987/./bulletins/march.pdf").ShouldBe(new[] { "1987", "bulletins" });
    }

    [Fact]
    public void A_path_deeper_than_the_tree_can_hold_is_refused()
    {
        var deep = string.Join('/', Enumerable.Range(0, MaxSegments).Select(i => $"level{i}")) + "/file.pdf";
        FolderSegmentsOf(deep)!.Count.ShouldBe(MaxSegments);

        var deeper = string.Join('/', Enumerable.Range(0, MaxSegments + 1).Select(i => $"level{i}")) + "/file.pdf";
        FolderSegmentsOf(deeper).ShouldBeNull();
    }

    [Fact]
    public void A_folder_name_that_could_not_be_a_cabinet_refuses_the_whole_path()
    {
        FolderSegmentsOf("bulle\ntins/march.pdf").ShouldBeNull();
        FolderSegmentsOf(new string('x', MaxSegmentLength + 1) + "/march.pdf").ShouldBeNull();

        // The boundary itself is allowed — an off-by-one here refuses real folders.
        FolderSegmentsOf(new string('x', MaxSegmentLength) + "/march.pdf")
            .ShouldBe(new[] { new string('x', MaxSegmentLength) });
    }

    [Fact]
    public void Surrounding_whitespace_is_trimmed_because_a_shelf_name_is_what_is_read_not_what_is_typed()
    {
        FolderSegmentsOf(" 1987 / bulletins /march.pdf").ShouldBe(new[] { "1987", "bulletins" });

        // A segment that is nothing but whitespace names no folder, so it is skipped rather
        // than becoming a shelf with a blank label nobody can select.
        FolderSegmentsOf("1987/   /march.pdf").ShouldBe(new[] { "1987" });
    }

    [Fact]
    public void A_filable_name_is_present_bounded_and_free_of_separators()
    {
        IsFilableName("Club archive").ShouldBeTrue();
        IsFilableName("1987").ShouldBeTrue();
        IsFilableName("Peștera Mare").ShouldBeTrue();

        IsFilableName(null).ShouldBeFalse();
        IsFilableName("").ShouldBeFalse();
        IsFilableName("   ").ShouldBeFalse();
        IsFilableName("a/b").ShouldBeFalse();
        IsFilableName(@"a\b").ShouldBeFalse();
        IsFilableName("a\tb").ShouldBeFalse();
        IsFilableName(new string('x', MaxSegmentLength + 1)).ShouldBeFalse();
    }
}
