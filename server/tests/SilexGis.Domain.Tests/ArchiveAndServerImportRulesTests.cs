// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Documents;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The two guards over content that arrives without a browser: an archive the server expands,
/// and a directory the server walks. Both are places where a mistake hands over whatever the
/// service account can reach, so both refuse by default and admit only what is named.
/// </summary>
public class ArchiveAndServerImportRulesTests
{
    [Fact]
    public void Directory_entries_are_skipped_rather_than_stored()
    {
        // The folders they describe are created from the paths of the files inside them, so an
        // archive that lists its directories and one that does not expand identically.
        ArchiveExpansionRules.IsStorable("1987/bulletins/march.pdf").ShouldBeTrue();
        ArchiveExpansionRules.IsStorable("1987/bulletins/").ShouldBeFalse();
        ArchiveExpansionRules.IsStorable(@"1987\bulletins\").ShouldBeFalse();
        ArchiveExpansionRules.IsStorable("").ShouldBeFalse();
        ArchiveExpansionRules.IsStorable("   ").ShouldBeFalse();
    }

    [Fact]
    public void A_nested_archive_is_recognised_but_stored_as_a_file_rather_than_expanded()
    {
        // Recognising it is what the upload path uses to decide whether to queue an expansion
        // at all; the expansion itself never asks, because it never recurses.
        ArchiveExpansionRules.IsArchive("club-archive.zip").ShouldBeTrue();
        ArchiveExpansionRules.IsArchive("CLUB-ARCHIVE.ZIP").ShouldBeTrue();
        ArchiveExpansionRules.IsArchive("survey.pdf").ShouldBeFalse();
        ArchiveExpansionRules.IsArchive(null).ShouldBeFalse();

        // It is still an entry worth storing when it turns up inside another archive.
        ArchiveExpansionRules.IsStorable("1987/inner.zip").ShouldBeTrue();
    }

    [Fact]
    public void An_archive_expanding_past_the_total_cap_is_abandoned_whole()
    {
        var limits = new ArchiveLimits(MaxTotalUncompressedBytes: 1000);

        ArchiveExpansionRules.AbandonReason(limits, 1000, 100, 100).ShouldBeNull();
        ArchiveExpansionRules.AbandonReason(limits, 1001, 100, 100)
            .ShouldBe(ArchiveExpansionRules.TooLargeCode);
    }

    [Fact]
    public void An_entry_expanding_far_beyond_its_stated_size_abandons_the_archive()
    {
        var limits = new ArchiveLimits(MaxCompressionRatio: 200);

        // A megabyte out of a kilobyte is not a document.
        ArchiveExpansionRules.AbandonReason(limits, 1_000_000, 1_000, 1_000_000)
            .ShouldBe(ArchiveExpansionRules.BombCode);

        // A scan compressing five to one is ordinary and must not be caught.
        ArchiveExpansionRules.AbandonReason(limits, 5_000_000, 1_000_000, 5_000_000).ShouldBeNull();
    }

    [Fact]
    public void The_ratio_test_does_not_fire_on_small_entries_where_a_ratio_means_nothing()
    {
        var limits = new ArchiveLimits(MaxCompressionRatio: 200);

        // 40 bytes expanding to 8 KB is a 200:1 ratio and also completely ordinary.
        ArchiveExpansionRules.AbandonReason(limits, 8_192, 40, 8_192).ShouldBeNull();
    }

    [Fact]
    public void An_entry_claiming_no_compressed_size_is_not_treated_as_infinitely_compressed()
    {
        var limits = new ArchiveLimits();

        // Dividing by the header's zero would be a crash, and treating it as a bomb would
        // refuse archives whose writers simply did not fill the field in.
        ArchiveExpansionRules.AbandonReason(limits, 1_000_000, 0, 1_000_000).ShouldBeNull();
    }

    [Fact]
    public void The_entry_cap_is_checked_before_an_entry_is_read_not_after()
    {
        var limits = new ArchiveLimits(MaxEntries: 2);

        ArchiveExpansionRules.WithinEntryCap(limits, 0).ShouldBeTrue();
        ArchiveExpansionRules.WithinEntryCap(limits, 1).ShouldBeTrue();
        ArchiveExpansionRules.WithinEntryCap(limits, 2).ShouldBeFalse();
    }

    [Fact]
    public void A_directory_inside_a_configured_root_is_allowed_and_one_beside_it_is_not()
    {
        var root = Path.GetFullPath(Path.Combine("srv", "archive"));
        string[] roots = [root];

        ServerImportPaths.IsWithinRoots(roots, root).ShouldBeTrue();
        ServerImportPaths.IsWithinRoots(roots, Path.Combine(root, "1987")).ShouldBeTrue();

        // The separator is what keeps a sibling from reading as a child.
        ServerImportPaths.IsWithinRoots(roots, root + "-old").ShouldBeFalse();
        ServerImportPaths.IsWithinRoots(roots, Path.GetFullPath(Path.Combine("srv", "other"))).ShouldBeFalse();
    }

    [Fact]
    public void An_installation_that_lists_no_roots_allows_nothing()
    {
        // The feature is off until an operator opts into it, which is the right default for
        // something that reads the server's own disk.
        ServerImportPaths.IsWithinRoots([], Path.GetFullPath("anywhere")).ShouldBeFalse();
        ServerImportPaths.IsWithinRoots(["", "   "], Path.GetFullPath("anywhere")).ShouldBeFalse();
    }

    [Fact]
    public void A_trailing_separator_on_either_side_names_the_same_directory()
    {
        var root = Path.GetFullPath(Path.Combine("srv", "archive"));
        string[] roots = [root + Path.DirectorySeparatorChar];

        ServerImportPaths.IsWithinRoots(roots, root).ShouldBeTrue();
        ServerImportPaths.IsWithinRoots(roots, root + Path.DirectorySeparatorChar).ShouldBeTrue();
    }

    [Fact]
    public void Any_of_several_roots_admits_a_path()
    {
        var photos = Path.GetFullPath(Path.Combine("srv", "photos"));
        var archive = Path.GetFullPath(Path.Combine("srv", "archive"));

        ServerImportPaths.IsWithinRoots([photos, archive], Path.Combine(archive, "1987")).ShouldBeTrue();
        ServerImportPaths.IsWithinRoots([photos, archive], Path.GetFullPath(Path.Combine("srv", "etc")))
            .ShouldBeFalse();
    }
}
