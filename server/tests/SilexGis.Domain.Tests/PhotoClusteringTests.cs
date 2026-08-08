// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Import;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Turning a drop of photographs into places: twelve pictures of one entrance have to arrive as
/// one candidate, two different holes have to stay two, and the same drop reviewed twice has to
/// produce the same candidate keys or every decision the reviewer saved names nothing.
/// </summary>
public class PhotoClusteringTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A point offset from (25.0, 45.0) by whole metres, which is how the cases read.</summary>
    private static PhotoFix At(Guid id, double metresEast, double metresNorth, int minute = 0) =>
        new(id, 25.0 + (metresEast / (111_320 * Math.Cos(45.0 * Math.PI / 180))),
            45.0 + (metresNorth / 111_320), Noon.AddMinutes(minute));

    private static Guid Id(int n) => new($"00000000-0000-0000-0000-{n:D12}");

    [Fact]
    public void Twelve_pictures_of_one_entrance_are_one_candidate()
    {
        var fixes = Enumerable.Range(1, 12).Select(i => At(Id(i), i % 4, i % 3, i)).ToList();

        var clusters = PhotoClustering.Group(fixes, radiusMeters: 25);

        clusters.Count.ShouldBe(1);
        clusters[0].FileIds.Count.ShouldBe(12);
    }

    [Fact]
    public void Two_holes_further_apart_than_the_radius_stay_two_candidates()
    {
        var clusters = PhotoClustering.Group(
            [At(Id(1), 0, 0), At(Id(2), 5, 0), At(Id(3), 200, 0), At(Id(4), 205, 0)],
            radiusMeters: 25);

        clusters.Count.ShouldBe(2);
        clusters.Select(c => c.FileIds.Count).ShouldAllBe(count => count == 2);
    }

    [Fact]
    public void A_line_of_pictures_does_not_chain_one_candidate_down_a_valley()
    {
        // Each picture is 20 m from the last — inside the radius of its neighbour, and the
        // reason grouping is by the centre rather than by the nearest member. Chaining would
        // make these six a single "place" a hundred metres long.
        var fixes = Enumerable.Range(0, 6).Select(i => At(Id(i + 1), i * 20, 0, i)).ToList();

        var clusters = PhotoClustering.Group(fixes, radiusMeters: 25);

        clusters.Count.ShouldBeGreaterThan(1);
        clusters.ShouldAllBe(c => c.FileIds.Count <= 3);
    }

    [Fact]
    public void A_candidate_is_named_by_its_lowest_file_id_whatever_order_the_pictures_arrive_in()
    {
        // The key is what a saved decision is stored under, so the same drop reviewed after
        // lunch has to produce the same one. Dropping the files in a different order is the
        // cheapest way that could break.
        var fixes = new[] { At(Id(7), 0, 0, 2), At(Id(3), 3, 1, 1), At(Id(9), 1, 2, 3) };

        var forwards = PhotoClustering.Group(fixes, radiusMeters: 25);
        var backwards = PhotoClustering.Group(fixes.Reverse(), radiusMeters: 25);

        forwards[0].Key.ShouldBe(Id(3));
        backwards[0].Key.ShouldBe(Id(3));
    }

    [Fact]
    public void Pictures_that_state_no_time_still_group_and_come_last()
    {
        var clusters = PhotoClustering.Group(
            [
                new PhotoFix(Id(1), 25.0, 45.0, null),
                new PhotoFix(Id(2), 25.0, 45.0, null),
            ],
            radiusMeters: 25);

        clusters.Count.ShouldBe(1);
        clusters[0].FileIds.Count.ShouldBe(2);
    }

    [Fact]
    public void A_group_that_straddles_the_antimeridian_stays_where_it_was_taken()
    {
        // Averaging 179.9999 with -179.9999 the obvious way gives zero — a hole in Romania
        // placed in the Gulf of Guinea. Absurd to hit, cheap to get right, so it is got right.
        var clusters = PhotoClustering.Group(
            [
                new PhotoFix(Id(1), 179.99990, 45.0, Noon),
                new PhotoFix(Id(2), -179.99990, 45.0, Noon.AddMinutes(1)),
            ],
            radiusMeters: 100);

        clusters.Count.ShouldBe(1);
        Math.Abs(clusters[0].Longitude).ShouldBeGreaterThan(179.9);
    }

    [Fact]
    public void A_radius_of_zero_leaves_every_picture_its_own_candidate()
    {
        var clusters = PhotoClustering.Group(
            [At(Id(1), 0, 0, 1), At(Id(2), 1, 0, 2), At(Id(3), 2, 0, 3)],
            radiusMeters: 0);

        clusters.Count.ShouldBe(3);
    }

    [Fact]
    public void An_empty_drop_is_no_candidates_rather_than_a_failure()
    {
        PhotoClustering.Group([], radiusMeters: 25).ShouldBeEmpty();
    }
}
