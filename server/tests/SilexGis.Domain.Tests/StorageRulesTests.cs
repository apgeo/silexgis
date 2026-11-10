// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Scans;
using SilexGis.Domain.Storage;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The arithmetic storage is judged by: whether something fits under every ceiling and by how
/// much it does not, how full a cap or a volume is, and what a point cloud's processing step is
/// estimated to write before it starts.
/// </summary>
public class StorageRulesTests
{
    private static readonly StorageThresholds Default = StorageThresholds.Default;

    [Fact]
    public void Something_that_fits_under_every_ceiling_has_no_shortfall()
    {
        StorageRules.Shortfall(
            [new StorageLimit(StorageLimitKind.Installation, 1000, 400), StorageLimit.Disk(StorageLimitKind.StoreDisk, 5000)],
            600).ShouldBeNull();
        StorageRules.Shortfall([], long.MaxValue).ShouldBeNull();
    }

    [Fact]
    public void The_shortfall_named_is_the_ceiling_missing_most_with_the_missing_amount()
    {
        var shortfall = StorageRules.Shortfall(
            [
                new StorageLimit(StorageLimitKind.Account, 1000, 900),      // room 100: missing 400
                new StorageLimit(StorageLimitKind.Scans, 1000, 950),        // room 50: missing 450
                new StorageLimit(StorageLimitKind.Installation, 10_000, 0), // fits
            ],
            500).ShouldNotBeNull();

        shortfall.Kind.ShouldBe(StorageLimitKind.Scans);
        shortfall.NeededBytes.ShouldBe(500);
        shortfall.RoomBytes.ShouldBe(50);
        shortfall.MissingBytes.ShouldBe(450);
    }

    [Fact]
    public void On_a_tie_the_ceiling_listed_first_is_named()
    {
        StorageRules.Shortfall(
            [new StorageLimit(StorageLimitKind.Account, 100, 0), new StorageLimit(StorageLimitKind.Installation, 100, 0)],
            150).ShouldNotBeNull().Kind.ShouldBe(StorageLimitKind.Account);
    }

    [Fact]
    public void A_cap_lowered_below_what_is_held_leaves_no_room_rather_than_a_debt()
    {
        var shortfall = StorageRules.Shortfall([new StorageLimit(StorageLimitKind.Installation, 100, 300)], 10).ShouldNotBeNull();
        shortfall.RoomBytes.ShouldBe(0);
        shortfall.MissingBytes.ShouldBe(10);
    }

    [Fact]
    public void Exactly_the_room_left_fits_and_nothing_to_add_always_fits()
    {
        StorageRules.Shortfall([new StorageLimit(StorageLimitKind.Scans, 100, 40)], 60).ShouldBeNull();
        StorageRules.Shortfall([new StorageLimit(StorageLimitKind.Scans, 100, 40)], 61).ShouldNotBeNull().MissingBytes.ShouldBe(1);
        StorageRules.Shortfall([new StorageLimit(StorageLimitKind.Scans, 0, 40)], 0).ShouldBeNull();
    }

    [Theory]
    [InlineData(0, StorageLevel.Normal)]
    [InlineData(749, StorageLevel.Normal)]
    [InlineData(750, StorageLevel.Notice)]
    [InlineData(899, StorageLevel.Notice)]
    [InlineData(900, StorageLevel.Warning)]
    [InlineData(999, StorageLevel.Warning)]
    [InlineData(1000, StorageLevel.Full)]
    [InlineData(5000, StorageLevel.Full)]
    public void A_cap_is_at_the_level_its_use_has_reached(long used, StorageLevel level) =>
        StorageRules.LevelOf(used, 1000, Default).ShouldBe(level);

    [Fact]
    public void One_byte_short_of_a_threshold_has_not_crossed_it()
    {
        // 75 % of 1 TB is 750 GB exactly; one byte under is still normal, however it rounds.
        const long cap = 1_000_000_000_000;
        StorageRules.LevelOf(750_000_000_000 - 1, cap, Default).ShouldBe(StorageLevel.Normal);
        StorageRules.LevelOf(750_000_000_000, cap, Default).ShouldBe(StorageLevel.Notice);
        StorageRules.LevelOf(long.MaxValue - 1, long.MaxValue, Default).ShouldBe(StorageLevel.Warning);
    }

    [Fact]
    public void No_cap_is_never_past_a_threshold()
    {
        StorageRules.LevelOf(long.MaxValue, null, Default).ShouldBe(StorageLevel.Normal);
        StorageRules.LevelOf(long.MaxValue, 0, Default).ShouldBe(StorageLevel.Normal);
    }

    [Fact]
    public void Thresholds_set_the_wrong_way_round_or_out_of_range_are_made_sane()
    {
        new StorageThresholds(90, 75).Normalized().ShouldBe(new StorageThresholds(90, 90));
        new StorageThresholds(-5, 400).Normalized().ShouldBe(new StorageThresholds(1, 100));
        StorageRules.LevelOf(900, 1000, new StorageThresholds(90, 75)).ShouldBe(StorageLevel.Warning);
        StorageRules.LevelOf(899, 1000, new StorageThresholds(90, 75)).ShouldBe(StorageLevel.Normal);
    }

    [Theory]
    [InlineData(null, 100, StorageLevel.Normal)]
    [InlineData(0L, 100, StorageLevel.Full)]
    [InlineData(99L, 100, StorageLevel.Warning)]
    [InlineData(100L, 100, StorageLevel.Notice)]
    [InlineData(199L, 100, StorageLevel.Notice)]
    [InlineData(200L, 100, StorageLevel.Normal)]
    [InlineData(5L, 0, StorageLevel.Normal)]
    [InlineData(0L, 0, StorageLevel.Full)]
    public void A_volume_is_warned_about_against_the_free_space_wanted(long? free, long minFree, StorageLevel level) =>
        StorageRules.DiskLevelOf(free, minFree).ShouldBe(level);

    [Fact]
    public void A_volume_refuses_only_what_cannot_be_written_at_all()
    {
        StorageLimit.Disk(StorageLimitKind.WorkerDisk, 100).RoomBytes.ShouldBe(100);
        StorageLimit.Disk(StorageLimitKind.WorkerDisk, -1).RoomBytes.ShouldBe(0);
    }

    [Fact]
    public void The_worst_level_is_the_installation_s()
    {
        StorageRules.Worst([StorageLevel.Notice, StorageLevel.Normal, StorageLevel.Warning]).ShouldBe(StorageLevel.Warning);
        StorageRules.Worst([]).ShouldBe(StorageLevel.Normal);
    }

    [Fact]
    public void Tiling_is_estimated_from_the_estimate_row_of_the_voxel_chosen_or_of_the_full_cloud()
    {
        var estimate = new PointCloudEstimate(
            new PointCloudEstimateRow(null, 1000, 9000, 10),
            [new PointCloudEstimateRow(2, 400, 3000, 4), new PointCloudEstimateRow(5, 100, 800, 1)]);

        ScanStorageNeeds.For(Py3dtilesTileStep.StepId, 2000, estimate, voxelCm: null).ShouldBe(new ScanStepNeed(9000, 11_000));
        ScanStorageNeeds.For(Py3dtilesTileStep.StepId, 700, estimate, voxelCm: 5).ShouldBe(new ScanStepNeed(800, 1500));
    }

    [Fact]
    public void Without_an_estimate_for_it_tiling_is_taken_at_the_top_of_the_measured_range()
    {
        ScanStorageNeeds.For(Py3dtilesTileStep.StepId, 1000, null, null)
            .ShouldBe(new ScanStepNeed(1000 * ScanStorageNeeds.TileFactorWithoutEstimate, 1000 + (1000 * ScanStorageNeeds.TileFactorWithoutEstimate)));

        var noRow = new PointCloudEstimate(new PointCloudEstimateRow(null, null, null, null), []);
        ScanStorageNeeds.For(Py3dtilesTileStep.StepId, 1000, noRow, 3).OutputBytes.ShouldBe(1000 * ScanStorageNeeds.TileFactorWithoutEstimate);
        ScanStorageNeeds.For(Py3dtilesTileStep.StepId, long.MaxValue / 2, null, null).ShouldBe(new ScanStepNeed(long.MaxValue, long.MaxValue));
    }

    [Fact]
    public void Odometry_needs_twenty_two_times_the_recording_on_the_worker_and_keeps_about_its_size()
    {
        ScanStorageNeeds.For(HdmappingOdometryStep.StepId, 1000, null, null).ShouldBe(new ScanStepNeed(1000, 22_000));
        ScanStorageNeeds.For(HdmappingExportStep.StepId, 1000, null, null).ShouldBe(new ScanStepNeed(1000, 3000));
        ScanStorageNeeds.For(PointCloudCheckStep.StepId, 1000, null, null)
            .ShouldBe(new ScanStepNeed(ScanStorageNeeds.ReportBytes, 1000 + ScanStorageNeeds.ReportBytes));
        // The plan and profile pictures: bounded by their pixel limit, not by the cloud they read.
        ScanStorageNeeds.For(PointCloudRasterStep.StepId, 5_000_000_000, null, null)
            .ShouldBe(new ScanStepNeed(ScanStorageNeeds.PictureBytes, 5_000_000_000 + ScanStorageNeeds.PictureBytes));
        // A surface: bounded by its triangle cap, counted for the scan's copy and the wall mesh's two files.
        ScanStorageNeeds.For(MeshSurfaceStep.StepId, 5_000_000_000, null, null)
            .ShouldBe(new ScanStepNeed(ScanStorageNeeds.SurfaceBytes, 5_000_000_000 + ScanStorageNeeds.SurfaceBytes));
        (3L * MeshSurfaceStep.MaxTriangles * 50).ShouldBeLessThanOrEqualTo(ScanStorageNeeds.SurfaceBytes);

        // The whole run: session, export, check report and tiles kept; the odometry's peak on the worker.
        const long raw = 100_000_000;
        var run = ScanStorageNeeds.ForRun(
            [HdmappingOdometryStep.StepId, HdmappingExportStep.StepId, PointCloudCheckStep.StepId, Py3dtilesTileStep.StepId], raw, null, null, null);
        run.WorkerBytes.ShouldBe(raw * 22);
        run.OutputBytes.ShouldBe(raw + raw + ScanStorageNeeds.ReportBytes + (raw * ScanStorageNeeds.TileFactorWithoutEstimate));
        ScanStorageNeeds.For(HdmappingOdometryStep.StepId, long.MaxValue / 4, null, null).WorkerBytes.ShouldBe(long.MaxValue);
    }

    [Fact]
    public void Thinning_is_taken_as_large_as_the_cloud_and_the_estimate_as_a_small_report()
    {
        ScanStorageNeeds.For(PointCloudDownsampleStep.StepId, 5000, null, 2).ShouldBe(new ScanStepNeed(5000, 10_000));
        ScanStorageNeeds.For(PointCloudEstimateStep.StepId, 5000, null, null)
            .ShouldBe(new ScanStepNeed(ScanStorageNeeds.ReportBytes, 5000 + ScanStorageNeeds.ReportBytes));
    }

    [Fact]
    public void Only_a_climb_is_a_crossing_told_and_a_fall_back_makes_the_next_climb_one_again()
    {
        StorageRules.IsReportedCrossing(StorageLevel.Normal, StorageLevel.Notice).ShouldBeTrue();
        StorageRules.IsReportedCrossing(StorageLevel.Notice, StorageLevel.Warning).ShouldBeTrue();
        StorageRules.IsReportedCrossing(StorageLevel.Normal, StorageLevel.Full).ShouldBeTrue();

        // Staying put says nothing new, and neither does falling back.
        StorageRules.IsReportedCrossing(StorageLevel.Notice, StorageLevel.Notice).ShouldBeFalse();
        StorageRules.IsReportedCrossing(StorageLevel.Normal, StorageLevel.Normal).ShouldBeFalse();
        StorageRules.IsReportedCrossing(StorageLevel.Warning, StorageLevel.Notice).ShouldBeFalse();
        StorageRules.IsReportedCrossing(StorageLevel.Notice, StorageLevel.Normal).ShouldBeFalse();

        // Once the fall back is recorded, the same threshold climbed past again is told again.
        var recorded = StorageLevel.Notice;
        foreach (var now in new[] { StorageLevel.Normal, StorageLevel.Notice })
        {
            var told = StorageRules.IsReportedCrossing(recorded, now);
            told.ShouldBe(now == StorageLevel.Notice);
            recorded = now;
        }
    }

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(999L, "999 B")]
    [InlineData(1000L, "1.0 kB")]
    [InlineData(6_815_744L, "6.8 MB")]
    [InlineData(21_474_836_480L, "21.5 GB")]
    [InlineData(4_000_000_000_000L, "4.0 TB")]
    public void A_size_reads_in_decimal_units_the_same_in_every_language(long bytes, string expected) =>
        StorageRules.Describe(bytes).ShouldBe(expected);

    [Fact]
    public void A_percentage_is_rounded_down_so_a_threshold_is_never_reported_before_it_is_reached()
    {
        StorageRules.PercentOf(899, 1000).ShouldBe(89);
        StorageRules.PercentOf(900, 1000).ShouldBe(90);
        StorageRules.PercentOf(1500, 1000).ShouldBe(150);
        StorageRules.PercentOf(10, 0).ShouldBe(0);
        StorageRules.PercentOf(-5, 1000).ShouldBe(0);
    }
}
