// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Features;

namespace SilexGis.Domain.Tests;

public class FeatureDeletionRulesTests
{
    private static readonly Guid Area = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Cave = Guid.Parse("00000000-0000-0000-0000-00000000000c");
    private static readonly Guid Entrance = Guid.Parse("00000000-0000-0000-0000-00000000000e");
    private static readonly Guid Other = Guid.Parse("00000000-0000-0000-0000-00000000000f");

    private static readonly DateTimeOffset First = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Second = First.AddHours(1);

    [Fact]
    public void A_feature_is_not_its_own_container()
    {
        // The ancestor set carries the feature itself; a deleted feature is deleted, and that
        // must not read as "something above it is".
        FeatureDeletionRules.DeletedContainers(Cave, [Cave], _ => true).ShouldBeEmpty();
    }

    [Fact]
    public void Every_deleted_container_is_in_the_way_whichever_path_it_is_on()
    {
        var deleted = new HashSet<Guid> { Cave, Other };

        FeatureDeletionRules.DeletedContainers(Entrance, [Entrance, Cave, Area, Other], deleted.Contains)
            .ShouldBe([Cave, Other], ignoreOrder: true);
        FeatureDeletionRules.DeletedContainers(Entrance, [Entrance, Cave, Area], _ => false).ShouldBeEmpty();
    }

    [Fact]
    public void What_to_restore_first_is_the_outermost_deleted_container()
    {
        var ancestry = new Dictionary<Guid, Guid[]>
        {
            [Area] = [Area],
            [Cave] = [Cave, Area],
        };

        // The cave between is refused in its turn, so it is the area that is named.
        FeatureDeletionRules.OutermostDeleted([Cave, Area], id => ancestry[id]).ShouldBe([Area]);
        // With the area standing, the cave is the outermost.
        FeatureDeletionRules.OutermostDeleted([Cave], id => ancestry[id]).ShouldBe([Cave]);
    }

    [Fact]
    public void Two_deleted_containers_on_separate_paths_are_both_outermost()
    {
        var ancestry = new Dictionary<Guid, Guid[]> { [Area] = [Area], [Other] = [Other] };

        FeatureDeletionRules.OutermostDeleted([Area, Other], id => ancestry[id])
            .ShouldBe([Area, Other], ignoreOrder: true);
    }

    [Fact]
    public void Only_a_survey_line_is_not_restored_on_its_own()
    {
        FeatureDeletionRules.IsRestorableKind(FeatureKind.Cave).ShouldBeTrue();
        FeatureDeletionRules.IsRestorableKind(FeatureKind.CaveEntrance).ShouldBeTrue();
        FeatureDeletionRules.IsRestorableKind(FeatureKind.Generic).ShouldBeTrue();
        FeatureDeletionRules.IsRestorableKind(FeatureKind.Centerline).ShouldBeFalse();
    }

    [Fact]
    public void What_went_with_a_deletion_is_what_is_inside_it_and_carries_its_moment()
    {
        var line = Guid.NewGuid();
        var earlier = Guid.NewGuid();
        var elsewhere = Guid.NewGuid();
        var inner = Guid.NewGuid();
        (Guid, FeatureDeletionRules.TakenRow)[] rows =
        [
            (Area, new(FeatureKind.Generic, Second, [Area])),
            (Cave, new(FeatureKind.Cave, Second, [Cave, Area])),
            (Entrance, new(FeatureKind.CaveEntrance, Second, [Entrance, Cave, Area])),
            (inner, new(FeatureKind.Generic, Second, [inner, Area])),
            // A survey line is the cave's own drawing and is not counted.
            (line, new(FeatureKind.Centerline, Second, [line, Cave, Area])),
            // Deleted before the area was: a deletion of its own.
            (earlier, new(FeatureKind.CaveEntrance, First, [earlier, Cave, Area])),
            // Deleted at the same moment, somewhere else.
            (elsewhere, new(FeatureKind.Cave, Second, [elsewhere, Other])),
        ];

        FeatureDeletionRules.TakenWith(Area, Second, rows).ShouldBe(new FeatureDeletionRules.Taken(1, 2));
        FeatureDeletionRules.TakenWith(Cave, Second, rows).ShouldBe(new FeatureDeletionRules.Taken(1, 0));
        FeatureDeletionRules.TakenWith(Entrance, Second, rows).ShouldBe(new FeatureDeletionRules.Taken(0, 0));
    }
}
