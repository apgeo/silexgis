// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Features;
using static SilexGis.Domain.Features.FeatureHierarchyRules;

namespace SilexGis.Domain.Tests;

public class FeatureHierarchyRulesTests
{
    private static readonly Guid Range = Guid.CreateVersion7();   // mountain range
    private static readonly Guid AreaA = Guid.CreateVersion7();   // karst area A
    private static readonly Guid AreaB = Guid.CreateVersion7();   // overlapping karst area B
    private static readonly Guid CaveX = Guid.CreateVersion7();   // cave under BOTH areas (DAG)
    private static readonly Guid Entrance = Guid.CreateVersion7();
    private static readonly Guid Sector = Guid.CreateVersion7();

    private static List<Edge> DagEdges() =>
    [
        new Edge(Range, AreaA),
        new Edge(Range, AreaB),
        new Edge(AreaA, CaveX),
        new Edge(AreaB, CaveX),
        new Edge(CaveX, Entrance),
        new Edge(CaveX, Sector),
    ];

    [Fact]
    public void Ancestors_include_self_and_every_path()
    {
        var ancestors = AncestorsOf(Entrance, ParentsByChild(DagEdges()));

        ancestors.ShouldBe([Entrance, CaveX, AreaA, AreaB, Range], ignoreOrder: true);
    }

    [Fact]
    public void Ancestors_of_a_root_is_itself()
    {
        AncestorsOf(Range, ParentsByChild(DagEdges())).ShouldBe([Range]);
    }

    [Fact]
    public void Diamond_paths_do_not_duplicate_ancestors()
    {
        var ancestors = AncestorsOf(CaveX, ParentsByChild(DagEdges()));

        ancestors.Count.ShouldBe(4); // self + two areas + range (range counted once)
    }

    [Fact]
    public void Descendants_cover_the_whole_subtree_over_all_paths()
    {
        var descendants = DescendantsOf(AreaA, ChildrenByParent(DagEdges()));

        // Area A contains the cave and, through it, the entrance and sector.
        descendants.ShouldBe([AreaA, CaveX, Entrance, Sector], ignoreOrder: true);
    }

    [Fact]
    public void Self_edge_is_a_cycle()
    {
        WouldCreateCycle(CaveX, CaveX, ParentsByChild(DagEdges())).ShouldBeTrue();
    }

    [Fact]
    public void Making_a_descendant_the_parent_is_a_cycle()
    {
        // Sector is below CaveX; CaveX under Sector would loop.
        WouldCreateCycle(Sector, CaveX, ParentsByChild(DagEdges())).ShouldBeTrue();
        // Deep case: entrance under the range's parent-to-be.
        WouldCreateCycle(Entrance, Range, ParentsByChild(DagEdges())).ShouldBeTrue();
    }

    [Fact]
    public void Adding_a_second_parent_is_not_a_cycle()
    {
        // The DAG's whole point: CaveX under a *sibling* area is fine.
        var newArea = Guid.CreateVersion7();
        var edges = DagEdges().Append(new Edge(Range, newArea)).ToList();

        WouldCreateCycle(newArea, CaveX, ParentsByChild(edges)).ShouldBeFalse();
    }

    [Fact]
    public void Ancestor_computation_tolerates_corrupted_cyclic_state()
    {
        // The verifier must be able to run on broken data without hanging.
        List<Edge> cyclic = [new Edge(AreaA, CaveX), new Edge(CaveX, AreaA)];

        var ancestors = AncestorsOf(CaveX, ParentsByChild(cyclic));

        ancestors.ShouldBe([CaveX, AreaA], ignoreOrder: true);
    }

    [Fact]
    public void Effective_protection_is_any_protected_ancestor()
    {
        var ancestors = AncestorsOf(Entrance, ParentsByChild(DagEdges()));

        // Protecting area B (one of the two overlapping areas) protects the entrance.
        IsProtectedEffective(ancestors, id => id == AreaB).ShouldBeTrue();
        IsProtectedEffective(ancestors, id => id == Guid.Empty).ShouldBeFalse();
        // Self-protection counts.
        IsProtectedEffective(AncestorsOf(CaveX, ParentsByChild(DagEdges())), id => id == CaveX).ShouldBeTrue();
    }
}
