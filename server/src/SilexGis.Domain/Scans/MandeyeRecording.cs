// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Surveys;

namespace SilexGis.Domain.Scans;

/// <summary>What one file of a MANDEYE recording folder is, by its name.</summary>
public enum MandeyeFileKind
{
    /// <summary><c>lidarNNNN.laz</c>: one chunk of the lidar's points, in the sensor's own frame.</summary>
    LidarChunk = 0,

    /// <summary><c>imuNNNN.csv</c>: the IMU log of the same chunk, required beside its lidar chunk.</summary>
    ImuChunk = 1,

    /// <summary><c>lidarNNNN.sn</c>: the serial numbers of the sensors that wrote the chunk, one per line.</summary>
    SerialNumbers = 2,

    /// <summary><c>gnssNNNN.gnss</c>: the GNSS receiver's log of the chunk, when the scanner has one.</summary>
    Gnss = 3,

    /// <summary>A <c>.nmea</c> file: a GNSS log in plain NMEA sentences.</summary>
    Nmea = 4,

    /// <summary><c>calibration.json</c>: how the sensors of a multi-sensor scanner sit relative to one another.</summary>
    Calibration = 5,
}

/// <summary>One file of a recording as its name classifies it, and the chunk it belongs to when it belongs to one.</summary>
public sealed record MandeyeFileName(MandeyeFileKind Kind, int? Chunk);

/// <summary>
/// What an inspection found of a raw recording, stored with the scan's facts. Measurements of the
/// recording only: no position, no point and no sensor serial number.
/// </summary>
/// <param name="ChunkCount">How many lidar/IMU chunk pairs the recording holds.</param>
/// <param name="SensorCount">How many lidar sensors wrote it, from the calibration or the serial numbers; null when neither says.</param>
/// <param name="HasCalibration">Whether the recording carries a calibration of its sensors.</param>
/// <param name="HasGnss">
/// Whether any GNSS log holds anything. A recording that does carries earth positions of its
/// track, and is treated as georeferenced: it is a location.
/// </param>
/// <param name="DurationSeconds">From the first to the last IMU sample, in seconds; null when the logs did not say.</param>
/// <param name="PointCount">The points of every lidar chunk together, by their headers.</param>
/// <param name="ImuHasHeader">Whether the IMU logs open with a line naming their columns (later firmware) or go straight to the samples.</param>
/// <param name="Bytes">The size of the recording's files together.</param>
public sealed record MandeyeRecordingFacts(
    int ChunkCount,
    int? SensorCount,
    bool HasCalibration,
    bool HasGnss,
    double? DurationSeconds,
    long PointCount,
    bool ImuHasHeader,
    long Bytes);

/// <summary>What reading the first and last lines of one IMU log found.</summary>
/// <param name="HasHeader">Whether the first line names the columns.</param>
/// <param name="FirstTimestamp">The first sample's time stamp, in the scanner's nanoseconds.</param>
/// <param name="LastTimestamp">The last sample's time stamp, in the scanner's nanoseconds.</param>
public sealed record MandeyeImuSpan(bool HasHeader, long FirstTimestamp, long LastTimestamp);

/// <summary>
/// The rules of a raw MANDEYE recording: which files a recording folder may hold, how they pair
/// up, and how the small text files that describe it are read. Points are never read here: the
/// lidar chunks are read only as far as their LAS headers, by <see cref="LasHeaderReader"/>.
/// </summary>
/// <remarks>
/// <para>
/// A recording is a folder the scanner writes in chunks of a few seconds each: a lidar chunk
/// <c>lidarNNNN.laz</c> and its IMU log <c>imuNNNN.csv</c>, which the odometry cannot do without,
/// and beside them optionally the sensors' serial numbers (<c>lidarNNNN.sn</c>), a GNSS log
/// (<c>gnssNNNN.gnss</c> or <c>*.nmea</c>) and, for a scanner of several sensors,
/// <c>calibration.json</c>. Camera pictures (<c>CAMERA_*</c> folders) are not part of what is
/// uploaded: nothing on this server uses them yet.
/// </para>
/// <para>
/// Every file is declared before any byte is sent, so a folder that cannot be processed is
/// refused before an hour of upload is spent on it. An empty file — a GNSS log of a scanner
/// that heard no satellite — carries nothing and is left out by the device, as every declared
/// file holds at least one byte.
/// </para>
/// </remarks>
public static partial class MandeyeRecording
{
    /// <summary>The name of a multi-sensor scanner's calibration file.</summary>
    public const string CalibrationName = "calibration.json";

    /// <summary>The extensions a recording's files may carry, lower-case, with the dot.</summary>
    public static readonly IReadOnlyList<string> Extensions = [".laz", ".csv", ".sn", ".gnss", ".nmea", ".json"];

    /// <summary>
    /// The columns an IMU log starts with, in order: the time stamp, the gyroscope and the
    /// accelerometer. Later firmware writes them as a header line and adds more after them (the
    /// IMU's id, a Unix time stamp); earlier firmware writes no header, and the same seven
    /// numbers lead every line.
    /// </summary>
    public static readonly IReadOnlyList<string> ImuColumns = ["timestamp", "gyroX", "gyroY", "gyroZ", "accX", "accY", "accZ"];

    [GeneratedRegex(@"^lidar(\d{1,6})\.laz$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LidarPattern();

    [GeneratedRegex(@"^imu(\d{1,6})\.csv$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImuPattern();

    [GeneratedRegex(@"^lidar(\d{1,6})\.sn$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SerialPattern();

    [GeneratedRegex(@"^gnss(\d{1,6})\.gnss$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GnssPattern();

    /// <summary>What a file name is in a recording, or null for a name a recording does not hold.</summary>
    public static MandeyeFileName? Classify(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        if (Chunk(LidarPattern(), name) is { } lidar)
        {
            return new MandeyeFileName(MandeyeFileKind.LidarChunk, lidar);
        }

        if (Chunk(ImuPattern(), name) is { } imu)
        {
            return new MandeyeFileName(MandeyeFileKind.ImuChunk, imu);
        }

        if (Chunk(SerialPattern(), name) is { } serial)
        {
            return new MandeyeFileName(MandeyeFileKind.SerialNumbers, serial);
        }

        if (Chunk(GnssPattern(), name) is { } gnss)
        {
            return new MandeyeFileName(MandeyeFileKind.Gnss, gnss);
        }

        if (SurveyUploadFiles.ExtensionOf(name) == ".nmea")
        {
            return new MandeyeFileName(MandeyeFileKind.Nmea, null);
        }

        return string.Equals(name, CalibrationName, StringComparison.OrdinalIgnoreCase)
            ? new MandeyeFileName(MandeyeFileKind.Calibration, null)
            : null;
    }

    private static int? Chunk(Regex pattern, string name) =>
        pattern.Match(name) is { Success: true } match
            ? int.Parse(match.Groups[1].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture)
            : null;

    /// <summary>
    /// The first thing wrong with a recording's declared file list, or null when it may be
    /// accepted: names a recording holds, each once, all declared as the archive (every file of
    /// a recording is its original), each within the per-file cap, together within the
    /// recording's cap, no more files than allowed, and every lidar chunk paired with its IMU log.
    /// </summary>
    public static SurveyUploadFileProblem? Problem(
        IReadOnlyList<SurveyUploadFileClaim>? files, long maxFileBytes, long maxTotalBytes, int maxFiles)
    {
        if (files is null || files.Count == 0)
        {
            return new SurveyUploadFileProblem("A recording upload declares the files of the recording folder.");
        }

        if (files.Count > maxFiles)
        {
            return new SurveyUploadFileProblem($"A recording may declare at most {maxFiles} files.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lidar = new HashSet<int>();
        var imu = new HashSet<int>();
        long total = 0;
        foreach (var file in files)
        {
            if (!SurveyUploadFiles.IsValidName(file.Name))
            {
                return new SurveyUploadFileProblem(
                    $"A file name must be 1 to {SurveyUploadFiles.MaxNameLength} characters with no path, no control "
                    + "characters and none of / \\ : * ? \" < > |, and must not end with a dot or a space.",
                    file.Name);
            }

            var name = file.Name!;
            if (!seen.Add(name))
            {
                return new SurveyUploadFileProblem("Two files of one upload have the same name.", name);
            }

            if (Classify(name) is not { } classified)
            {
                return new SurveyUploadFileProblem(
                    "This file is not part of a MANDEYE recording. A recording holds lidarNNNN.laz with imuNNNN.csv, "
                    + "and optionally lidarNNNN.sn, gnssNNNN.gnss, .nmea files and calibration.json.",
                    name);
            }

            if (file.Role != SurveyUploadFileRole.Archive)
            {
                return new SurveyUploadFileProblem("Every file of a recording is declared as the archive.", name);
            }

            if (file.Size <= 0 || file.Size > maxFileBytes)
            {
                return new SurveyUploadFileProblem($"Each file is between 1 byte and {maxFileBytes} bytes.", name);
            }

            total += file.Size;
            if (classified.Kind == MandeyeFileKind.LidarChunk)
            {
                lidar.Add(classified.Chunk!.Value);
            }
            else if (classified.Kind == MandeyeFileKind.ImuChunk)
            {
                imu.Add(classified.Chunk!.Value);
            }
        }

        if (total > maxTotalBytes)
        {
            return new SurveyUploadFileProblem($"A recording is at most {maxTotalBytes} bytes together.");
        }

        if (lidar.Count == 0)
        {
            return new SurveyUploadFileProblem("A recording holds at least one lidar chunk with its IMU log.");
        }

        var withoutImu = lidar.Except(imu).Order().FirstOrDefault(-1);
        if (withoutImu >= 0)
        {
            return new SurveyUploadFileProblem(
                "Every lidar chunk comes with the IMU log of the same number.",
                string.Create(CultureInfo.InvariantCulture, $"imu{withoutImu:D4}.csv"));
        }

        var withoutLidar = imu.Except(lidar).Order().FirstOrDefault(-1);
        if (withoutLidar >= 0)
        {
            return new SurveyUploadFileProblem(
                "Every IMU log comes with the lidar chunk of the same number.",
                string.Create(CultureInfo.InvariantCulture, $"lidar{withoutLidar:D4}.laz"));
        }

        return null;
    }

    /// <summary>
    /// Reads the first and last sample lines of an IMU log: whether it opens with the column
    /// names, and the time stamps it spans. Null, with the reason, when the log is not one the
    /// odometry can read — a header that does not start with the expected columns, or a sample
    /// line without seven numbers.
    /// </summary>
    /// <param name="firstLines">The start of the log: at least its first two lines when it has a header.</param>
    /// <param name="lastLine">The log's last non-empty line.</param>
    public static MandeyeImuSpan? ReadImu(IReadOnlyList<string> firstLines, string? lastLine, out string? problem)
    {
        problem = null;
        var lines = firstLines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (lines.Count == 0)
        {
            problem = "an IMU log is empty";
            return null;
        }

        var hasHeader = lines[0].Any(char.IsLetter);
        if (hasHeader)
        {
            var names = Split(lines[0]);
            if (names.Length < ImuColumns.Count
                || !ImuColumns.Select((c, i) => string.Equals(c, names[i], StringComparison.OrdinalIgnoreCase)).All(x => x))
            {
                problem = "an IMU log's header does not start with the columns " + string.Join(" ", ImuColumns);
                return null;
            }
        }

        var firstSample = hasHeader ? (lines.Count > 1 ? lines[1] : null) : lines[0];
        if (firstSample is null)
        {
            problem = "an IMU log holds no sample";
            return null;
        }

        if (Timestamp(firstSample) is not { } first || Timestamp(lastLine ?? firstSample) is not { } last)
        {
            problem = "an IMU log's samples are not lines of at least seven numbers";
            return null;
        }

        return new MandeyeImuSpan(hasHeader, first, last);
    }

    /// <summary>The time stamp leading a sample line, when the line holds at least the seven expected numbers.</summary>
    private static long? Timestamp(string line)
    {
        var parts = Split(line);
        if (parts.Length < ImuColumns.Count)
        {
            return null;
        }

        for (var i = 1; i < ImuColumns.Count; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                return null;
            }
        }

        return long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var stamp) ? stamp : null;
    }

    private static string[] Split(string line) =>
        line.Split([' ', '\t', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// The sensor serial numbers a <c>.sn</c> file names: one line per sensor, its index and its
    /// serial number. Kept only to be counted; a serial number identifies a device, not a cave.
    /// </summary>
    public static IReadOnlyList<string> ReadSerials(string text) =>
        [.. text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => Split(line))
            .Where(parts => parts.Length >= 2)
            .Select(parts => parts[1])];

    /// <summary>
    /// How many sensors a calibration file describes: the entries of its <c>calibration</c>
    /// object, one per sensor serial number. Null when the file is not that shape.
    /// </summary>
    public static int? SensorsInCalibration(ReadOnlySpan<byte> json)
    {
        try
        {
            using var document = JsonDocument.Parse(json.ToArray());
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("calibration", out var calibration)
                && calibration.ValueKind == JsonValueKind.Object
                ? calibration.EnumerateObject().Count()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The IMU time stamps are the scanner's clock in nanoseconds.</summary>
    public const double ImuTicksPerSecond = 1e9;

    /// <summary>The recording's duration from the IMU spans of its chunks, in seconds; null when no span says.</summary>
    public static double? DurationSeconds(IEnumerable<MandeyeImuSpan> spans)
    {
        var all = spans.ToList();
        if (all.Count == 0)
        {
            return null;
        }

        var first = all.Min(s => s.FirstTimestamp);
        var last = all.Max(s => s.LastTimestamp);
        return last > first ? Math.Round((last - first) / ImuTicksPerSecond, 3) : null;
    }

    /// <summary>The bytes of a small text file read as UTF-8, for the serial numbers and the like.</summary>
    public static string Text(ReadOnlySpan<byte> bytes) => Encoding.UTF8.GetString(bytes);
}
