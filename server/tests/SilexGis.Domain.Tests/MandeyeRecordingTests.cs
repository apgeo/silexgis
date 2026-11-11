// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Text;
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Scans;
using SilexGis.Domain.Surveys;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The rules of a raw MANDEYE recording: which names a folder may hold, how the declaration is
/// judged before any byte is sent, how the IMU logs, serial numbers and calibration are read, and
/// how the facts make a recording with GNSS a location. Every file here is invented in the test.
/// </summary>
public sealed class MandeyeRecordingTests
{
    private const long Cap = 4L * 1024 * 1024 * 1024;

    [Theory]
    [InlineData("lidar0000.laz", MandeyeFileKind.LidarChunk, 0)]
    [InlineData("LIDAR0012.LAZ", MandeyeFileKind.LidarChunk, 12)]
    [InlineData("imu0012.csv", MandeyeFileKind.ImuChunk, 12)]
    [InlineData("lidar0003.sn", MandeyeFileKind.SerialNumbers, 3)]
    [InlineData("gnss0003.gnss", MandeyeFileKind.Gnss, 3)]
    [InlineData("track.nmea", MandeyeFileKind.Nmea, null)]
    [InlineData("calibration.json", MandeyeFileKind.Calibration, null)]
    public void A_recording_file_is_classified_by_its_name(string name, MandeyeFileKind kind, int? chunk)
    {
        MandeyeRecording.Classify(name).ShouldBe(new MandeyeFileName(kind, chunk));
    }

    [Theory]
    [InlineData("lidar.laz")]
    [InlineData("scan0000.laz")]
    [InlineData("imu0000.txt")]
    [InlineData("picture.jpg")]
    [InlineData("status.json")]
    [InlineData("")]
    public void A_name_a_recording_does_not_hold_is_not_classified(string name)
    {
        MandeyeRecording.Classify(name).ShouldBeNull();
    }

    private static SurveyUploadFileClaim Claim(string name, long size = 100) =>
        new(name, size, SurveyUploadFileRole.Archive);

    private static readonly SurveyUploadFileClaim[] TwoChunks =
    [
        Claim("lidar0000.laz"), Claim("imu0000.csv"), Claim("lidar0000.sn"), Claim("gnss0000.gnss"),
        Claim("lidar0001.laz"), Claim("imu0001.csv"), Claim("calibration.json"),
    ];

    [Fact]
    public void A_folder_of_paired_chunks_with_the_optional_files_is_accepted()
    {
        SurveyUploadFiles.Problem(SurveyOrigin.Mandeye, TwoChunks, Cap, 5000, 20 * Cap).ShouldBeNull();
    }

    [Fact]
    public void A_lidar_chunk_without_its_imu_log_is_refused_naming_the_missing_log()
    {
        var problem = SurveyUploadFiles.Problem(
            SurveyOrigin.Mandeye, [Claim("lidar0000.laz"), Claim("imu0000.csv"), Claim("lidar0001.laz")], Cap, 5000, 20 * Cap);

        problem.ShouldNotBeNull();
        problem.File.ShouldBe("imu0001.csv");
    }

    [Fact]
    public void An_imu_log_without_its_lidar_chunk_is_refused()
    {
        var problem = SurveyUploadFiles.Problem(
            SurveyOrigin.Mandeye, [Claim("lidar0000.laz"), Claim("imu0000.csv"), Claim("imu0002.csv")], Cap, 5000, 20 * Cap);

        problem.ShouldNotBeNull();
        problem.File.ShouldBe("lidar0002.laz");
    }

    [Fact]
    public void A_folder_with_no_lidar_chunk_is_refused()
    {
        SurveyUploadFiles.Problem(SurveyOrigin.Mandeye, [Claim("calibration.json")], Cap, 5000, 20 * Cap).ShouldNotBeNull();
    }

    [Fact]
    public void A_file_a_recording_does_not_hold_is_refused_by_name()
    {
        var problem = SurveyUploadFiles.Problem(
            SurveyOrigin.Mandeye, [.. TwoChunks, Claim("photo.jpg")], Cap, 5000, 20 * Cap);

        problem.ShouldNotBeNull();
        problem.File.ShouldBe("photo.jpg");
    }

    [Fact]
    public void Every_file_of_a_recording_is_declared_as_the_archive()
    {
        var problem = SurveyUploadFiles.Problem(
            SurveyOrigin.Mandeye,
            [Claim("lidar0000.laz"), new SurveyUploadFileClaim("imu0000.csv", 10, SurveyUploadFileRole.Other)],
            Cap, 5000, 20 * Cap);

        problem.ShouldNotBeNull();
        problem.File.ShouldBe("imu0000.csv");
    }

    [Fact]
    public void The_caps_on_one_file_on_the_whole_and_on_the_count_are_each_held()
    {
        SurveyUploadFiles.Problem(SurveyOrigin.Mandeye, [Claim("lidar0000.laz", 101), Claim("imu0000.csv")], 100, 5000, 1000)
            .ShouldNotBeNull().File.ShouldBe("lidar0000.laz");
        SurveyUploadFiles.Problem(SurveyOrigin.Mandeye, [Claim("lidar0000.laz", 100), Claim("imu0000.csv", 100)], 100, 5000, 199)
            .ShouldNotBeNull();
        SurveyUploadFiles.Problem(SurveyOrigin.Mandeye, TwoChunks, Cap, 6, 20 * Cap).ShouldNotBeNull();
        SurveyUploadFiles.Problem(SurveyOrigin.Mandeye, [Claim("lidar0000.laz", 0), Claim("imu0000.csv")], Cap, 5000, 20 * Cap)
            .ShouldNotBeNull().File.ShouldBe("lidar0000.laz");
    }

    [Fact]
    public void Two_names_that_differ_only_in_case_are_one_file()
    {
        SurveyUploadFiles.Problem(
            SurveyOrigin.Mandeye, [Claim("lidar0000.laz"), Claim("imu0000.csv"), Claim("IMU0000.CSV")], Cap, 5000, 20 * Cap)
            .ShouldNotBeNull();
    }

    [Fact]
    public void An_imu_log_with_a_header_is_read_for_its_span()
    {
        var span = MandeyeRecording.ReadImu(
            ["timestamp gyroX gyroY gyroZ accX accY accZ imuId timestampUnix", "1000000000 0.1 0.2 0.3 0.0 0.0 1.0 0 1700000000"],
            "3500000000 0.1 0.2 0.3 0.0 0.0 1.0 0 1700000002",
            out var problem);

        problem.ShouldBeNull();
        span.ShouldBe(new MandeyeImuSpan(true, 1_000_000_000, 3_500_000_000));
    }

    [Fact]
    public void An_imu_log_without_a_header_is_read_from_its_first_sample()
    {
        var span = MandeyeRecording.ReadImu(["2000 -0.01 0.02 0.0 -0.4 0.04 0.9 0"], "9000 -0.01 0.02 0.0 -0.4 0.04 0.9 0", out var problem);

        problem.ShouldBeNull();
        span.ShouldBe(new MandeyeImuSpan(false, 2000, 9000));
    }

    [Theory]
    [InlineData("time gyroX gyroY gyroZ accX accY accZ", "1 2 3 4 5 6 7")]
    [InlineData("1 2 3", null)]
    [InlineData("1 2 3 4 5 6 x", null)]
    public void An_imu_log_the_odometry_cannot_read_is_refused_with_a_reason(string firstLine, string? secondLine)
    {
        string[] lines = secondLine is null ? [firstLine] : [firstLine, secondLine];
        MandeyeRecording.ReadImu(lines, null, out var problem).ShouldBeNull();
        problem.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Serial_numbers_and_a_calibration_are_counted_as_sensors()
    {
        MandeyeRecording.ReadSerials("0 SENSOR0000000A\n1 SENSOR0000000B\n").Count.ShouldBe(2);
        MandeyeRecording.SensorsInCalibration(
            """{"calibration":{"SENSOR0000000A":{"identity":"true"},"SENSOR0000000B":{"order":"ROW"}},"imuToUse":"SENSOR0000000A"}"""u8)
            .ShouldBe(2);
        MandeyeRecording.SensorsInCalibration("""{"something":"else"}"""u8).ShouldBeNull();
        MandeyeRecording.SensorsInCalibration("not json"u8).ShouldBeNull();
    }

    [Fact]
    public void The_duration_runs_from_the_first_to_the_last_imu_sample_of_all_chunks()
    {
        MandeyeRecording.DurationSeconds(
            [new MandeyeImuSpan(false, 5_000_000_000, 9_000_000_000), new MandeyeImuSpan(false, 1_000_000_000, 4_000_000_000)])
            .ShouldBe(8.0);
        MandeyeRecording.DurationSeconds([]).ShouldBeNull();
    }

    [Fact]
    public void A_recording_with_gnss_is_a_location_and_one_without_is_not()
    {
        var header = LasHeaderReader.Read(Header(pointCount: 500), out var refusal);
        refusal.ShouldBeNull();

        var without = PointCloudFacts.FromRecording(header!, Facts(gnss: false));
        var with = PointCloudFacts.FromRecording(header!, Facts(gnss: true));

        without.IsGeoreferenced.ShouldBeFalse();
        with.IsGeoreferenced.ShouldBeTrue();
        with.Crs.ShouldBeNull();
        with.PointCount.ShouldBe(1500);

        // The facts survive the round trip through the stored document, and an export's header
        // facts written later keep what the recording said.
        var stored = PointCloudFacts.FromJson(with.ToJson())!;
        stored.Recording.ShouldBe(with.Recording);
        PointCloudFacts.FromHeader(header!).WithProcessingOf(stored).IsGeoreferenced.ShouldBeTrue();
    }

    private static MandeyeRecordingFacts Facts(bool gnss) =>
        new(ChunkCount: 3, SensorCount: 1, HasCalibration: false, HasGnss: gnss, DurationSeconds: 12.5,
            PointCount: 1500, ImuHasHeader: false, Bytes: 4096);

    /// <summary>A LAS 1.2 header of point format 1, LAZ-compressed, in a sensor's own frame.</summary>
    internal static byte[] Header(uint pointCount)
    {
        var b = new byte[227];
        "LASF"u8.CopyTo(b);
        b[24] = 1;
        b[25] = 2;
        Encoding.ASCII.GetBytes("MANDEYE TEST").CopyTo(b, 26);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(94), 227);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(96), 227);
        b[104] = 1 | 0x80;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(105), 28);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(107), pointCount);
        for (var i = 0; i < 3; i++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(b.AsSpan(131 + (8 * i)), 0.001);
        }

        double[] extent = [10, -10, 10, -10, 3, -3];
        for (var i = 0; i < extent.Length; i++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(b.AsSpan(179 + (8 * i)), extent[i]);
        }

        return b;
    }
}
