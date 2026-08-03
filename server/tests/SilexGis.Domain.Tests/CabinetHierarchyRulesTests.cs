// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Documents;
using static SilexGis.Domain.Documents.CabinetHierarchyRules;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The cabinet tree's pure semantics: what a path is, what a move does to the paths below
/// it, and the two grounds on which a placement is refused. Every refusal here is paired
/// with the placement that is allowed, so a rule that refuses everything cannot pass.
/// </summary>
public class CabinetHierarchyRulesTests
{
    private static readonly Guid Archive = Guid.CreateVersion7();
    private static readonly Guid Bulletins = Guid.CreateVersion7();
    private static readonly Guid Nineteen87 = Guid.CreateVersion7();
    private static readonly Guid Surveys = Guid.CreateVersion7();

    [Fact]
    public void A_root_cabinets_path_is_its_own_id_and_a_child_extends_it()
    {
        var root = PathOf(Archive, null);
        root.ShouldBe(Archive.ToString());

        var child = PathOf(Bulletins, root);
        child.ShouldBe($"{Archive}{Separator}{Bulletins}");

        // An empty parent path means the same thing as none: a root.
        PathOf(Archive, string.Empty).ShouldBe(root);
    }

    [Fact]
    public void A_path_names_its_ancestors_root_first_so_a_breadcrumb_needs_no_walk()
    {
        var path = PathOf(Nineteen87, PathOf(Bulletins, PathOf(Archive, null)));

        IdsOf(path).ShouldBe(new[] { Archive, Bulletins, Nineteen87 });
        DepthOf(path).ShouldBe(3);

        IdsOf(null).ShouldBeEmpty();
        DepthOf(null).ShouldBe(0);
    }

    [Fact]
    public void Containment_is_compared_on_a_label_boundary_not_on_raw_text()
    {
        var archive = PathOf(Archive, null);
        var bulletins = PathOf(Bulletins, archive);

        IsWithin(bulletins, archive).ShouldBeTrue();
        IsWithin(archive, archive).ShouldBeTrue("a cabinet is inside its own subtree");
        IsWithin(archive, bulletins).ShouldBeFalse("a parent does not sit under its child");

        // The trap a plain prefix match falls into: "a.bc" is not below "a.b".
        IsWithin("a.bc", "a.b").ShouldBeFalse();
        IsWithin("a.b.c", "a.b").ShouldBeTrue();
    }

    [Fact]
    public void Moving_a_subtree_swaps_its_root_prefix_and_keeps_relative_positions()
    {
        var archive = PathOf(Archive, null);
        var bulletins = PathOf(Bulletins, archive);
        var year = PathOf(Nineteen87, bulletins);
        var surveys = PathOf(Surveys, null);

        var movedBulletins = PathOf(Bulletins, surveys);
        Rebase(bulletins, bulletins, movedBulletins).ShouldBe(movedBulletins);
        Rebase(year, bulletins, movedBulletins).ShouldBe($"{surveys}{Separator}{Bulletins}{Separator}{Nineteen87}");

        // Rebasing a path that is not in the moved subtree is a programming error, not a
        // silently wrong path.
        Should.Throw<ArgumentException>(() => Rebase(archive, bulletins, movedBulletins));
    }

    [Fact]
    public void A_cabinet_may_not_be_moved_inside_its_own_subtree()
    {
        var archive = PathOf(Archive, null);
        var bulletins = PathOf(Bulletins, archive);
        var surveys = PathOf(Surveys, null);

        ValidatePlacement(archive, bulletins).ShouldBe(CycleCode);
        ValidatePlacement(archive, archive).ShouldBe(CycleCode, "a cabinet is not its own parent");

        // The same move elsewhere is fine, and so is promoting a cabinet to a root.
        ValidatePlacement(bulletins, surveys).ShouldBeNull();
        ValidatePlacement(bulletins, null).ShouldBeNull();
    }

    [Fact]
    public void Nesting_stops_at_the_depth_cap_and_the_whole_moved_subtree_counts()
    {
        var deepest = string.Join(Separator, Enumerable.Range(0, MaxDepth).Select(_ => Guid.CreateVersion7()));
        DepthOf(deepest).ShouldBe(MaxDepth);

        // One below the cap still accepts a child; the deepest level does not.
        var oneAbove = string.Join(Separator, deepest.Split(Separator)[..(MaxDepth - 1)]);
        ValidatePlacement(null, oneAbove).ShouldBeNull();
        ValidatePlacement(null, deepest).ShouldBe(TooDeepCode);

        // A move carries its descendants, so the cap is judged on the deepest of them: a
        // two-level subtree fits under level 8 and not under level 9.
        ValidatePlacement(PathOf(Surveys, null), Prefix(deepest, MaxDepth - 2), subtreeHeight: 1).ShouldBeNull();
        ValidatePlacement(PathOf(Surveys, null), Prefix(deepest, MaxDepth - 1), subtreeHeight: 1).ShouldBe(TooDeepCode);
    }

    [Fact]
    public void Subtree_height_is_measured_from_the_deepest_descendant()
    {
        var archive = PathOf(Archive, null);
        var bulletins = PathOf(Bulletins, archive);
        var year = PathOf(Nineteen87, bulletins);

        HeightOf(archive, [archive]).ShouldBe(0, "a leaf carries nothing below it");
        HeightOf(archive, [archive, bulletins, year]).ShouldBe(2);
        HeightOf(bulletins, [bulletins, year]).ShouldBe(1);
    }

    private static string Prefix(string path, int levels) => string.Join(Separator, path.Split(Separator)[..levels]);
}
