// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Scans;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Scans;

/// <summary>One archived file of a raw recording: its name as uploaded, where its bytes are, and their size.</summary>
public sealed record ArchivedRecordingFile(string Name, string StoragePath, Guid FileId, long SizeBytes, string Sha256 = "");

/// <summary>
/// What a raw recording's run does in the API process before any worker is asked: it inspects
/// the recording — the chunks paired, every lidar chunk's header read, the IMU logs' columns and
/// time span, the sensors counted, GNSS noted — and records the facts on the scan's model.
/// </summary>
/// <remarks>
/// <para>
/// Nothing but headers and the first and last lines of small logs is read: a recording is
/// gigabytes, and what the inspection has to establish is that the odometry will be able to read
/// it and what it will be given, not what the points say.
/// </para>
/// <para>
/// The model is made here, before there is a point cloud of record, so the scan has a card and
/// its facts a home from the moment the recording is inspected. Until the export replaces it, the
/// model is shown from the recording's first lidar chunk: a file of the recording itself, in the
/// sensor's frame, which is what a scan without processing honestly is.
/// </para>
/// </remarks>
public static class MandeyeUploadRun
{
    // The most of a small text file read — serial numbers, a calibration. Each is a few hundred
    // bytes; a file far larger is not what its name says, and reading it whole would only cost.
    private const int SmallFileCap = 1024 * 1024;

    // How much of an IMU log's end is read to find its last line: a line is under a hundred
    // characters, so this holds several whatever the log's line endings.
    private const int TailBytes = 4096;

    /// <summary>
    /// The recording's facts, or a pipeline failure naming what makes it one the odometry cannot
    /// read. The message names a file only by the role it plays, never by anything it holds.
    /// </summary>
    public static async Task<PointCloudFacts> InspectAsync(
        IReadOnlyList<ArchivedRecordingFile> files,
        Func<string, CancellationToken, Task<Stream>> open,
        CancellationToken ct)
    {
        var classified = files
            .Select(f => (File: f, Name: MandeyeRecording.Classify(f.Name)))
            .ToList();
        if (classified.Any(c => c.Name is null))
        {
            throw Invalid("it holds a file that is not part of a MANDEYE recording");
        }

        var lidar = classified.Where(c => c.Name!.Kind == MandeyeFileKind.LidarChunk)
            .OrderBy(c => c.Name!.Chunk).ToList();
        var imu = classified.Where(c => c.Name!.Kind == MandeyeFileKind.ImuChunk)
            .OrderBy(c => c.Name!.Chunk).ToList();
        if (lidar.Count == 0
            || !lidar.Select(c => c.Name!.Chunk).SequenceEqual(imu.Select(c => c.Name!.Chunk)))
        {
            throw Invalid("every lidar chunk comes with the IMU log of the same number, and at least one pair is needed");
        }

        LasHeader? first = null;
        long points = 0;
        foreach (var (file, _) in lidar)
        {
            await using var stream = await open(file.StoragePath, ct);
            var header = LasHeaderReader.Read(stream, out var refusal)
                ?? throw Invalid("a lidar chunk is not a LAS or LAZ file this server can read: "
                    + (refusal?.Detail ?? "its header could not be read"));
            first ??= header;
            points += header.PointCount;
        }

        var spans = new List<MandeyeImuSpan>();
        foreach (var (file, _) in imu)
        {
            await using var stream = await open(file.StoragePath, ct);
            var (head, last) = await HeadAndTailAsync(stream, ct);
            var span = MandeyeRecording.ReadImu(head, last, out var problem)
                ?? throw Invalid(problem ?? "an IMU log cannot be read");
            spans.Add(span);
        }

        var serials = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (file, _) in classified.Where(c => c.Name!.Kind == MandeyeFileKind.SerialNumbers))
        {
            await using var stream = await open(file.StoragePath, ct);
            serials.UnionWith(MandeyeRecording.ReadSerials(MandeyeRecording.Text(await SmallAsync(stream, ct))));
        }

        int? calibrated = null;
        var calibration = classified.FirstOrDefault(c => c.Name!.Kind == MandeyeFileKind.Calibration).File;
        if (calibration is not null)
        {
            await using var stream = await open(calibration.StoragePath, ct);
            calibrated = MandeyeRecording.SensorsInCalibration(await SmallAsync(stream, ct));
        }

        var recording = new MandeyeRecordingFacts(
            ChunkCount: lidar.Count,
            SensorCount: calibrated ?? (serials.Count > 0 ? serials.Count : null),
            HasCalibration: calibration is not null,
            // Every declared file holds at least one byte, so a GNSS log that was sent is one
            // that heard something. Only the .gnss logs count: they carry the lidar clock a
            // placement needs, while a .nmea-only recording could not be placed from its fixes.
            HasGnss: classified.Any(c => c.Name!.Kind is MandeyeFileKind.Gnss && c.File.SizeBytes > 0),
            DurationSeconds: MandeyeRecording.DurationSeconds(spans),
            PointCount: points,
            ImuHasHeader: spans[0].HasHeader,
            Bytes: files.Sum(f => f.SizeBytes));

        return PointCloudFacts.FromRecording(first!, recording);
    }

    /// <summary>
    /// Records the facts on the upload's scan model, making the model on the first run, shown
    /// from the recording's first lidar chunk until the export replaces it.
    /// </summary>
    public static async Task<SurveyModel> RecordAsync(
        SilexGisDbContext db, SurveyUpload upload, IReadOnlyList<ArchivedRecordingFile> files, PointCloudFacts facts, CancellationToken ct)
    {
        var firstChunk = files
            .Select(f => (File: f, Name: MandeyeRecording.Classify(f.Name)))
            .Where(c => c.Name?.Kind == MandeyeFileKind.LidarChunk)
            .OrderBy(c => c.Name!.Chunk)
            .First().File;
        // A rerun of a recording that was already exported keeps showing the export: the model's
        // file stays the export and the recording's facts are merged into the facts that describe
        // it, instead of pointing the scan back at one raw sensor-frame chunk.
        var exportStored = await db.ScanArtifacts.AnyAsync(
            a => a.SurveyUploadId == upload.Id && a.StepId == HdmappingExportStep.StepId && a.Kind == ScanArtifactKind.PointCloudLaz, ct);
        var formats = SurveyModelKinds.FormatsOf(SurveyModelKind.PointCloud);
        var existing = exportStored
            ? await db.SurveyModels.FirstOrDefaultAsync(m => m.SurveyUploadId == upload.Id && formats.Contains(m.Format), ct)
            : null;
        var keptFileId = existing?.FileId;
        var keptFormat = existing?.Format;
        var keptFacts = existing is null ? null : PointCloudFacts.FromJson(existing.PointCloud);
        var (model, made) = await PointCloudUploadRun.ModelOfAsync(db, upload, firstChunk.FileId, SurveyModelFormat.Laz, facts, ct);
        if (existing is not null && keptFileId is { } shown && keptFacts is not null)
        {
            model.FileId = shown;
            model.Format = keptFormat ?? model.Format;
            model.PointCloud = (keptFacts with { Recording = facts.Recording }).ToJson();
        }

        // Every file of the recording is an artefact the upload made, so the odometry step can
        // name what it read the way every other step does, and an earlier run's session is reused
        // only while the recording is the same files.
        var known = await db.ScanArtifacts
            .Where(a => a.SurveyUploadId == upload.Id && a.StepId == ScanSteps.UploadStepId && a.Kind == ScanArtifactKind.RawRecording)
            .ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        foreach (var file in files)
        {
            var artefact = known.FirstOrDefault(a => a.FileId == file.FileId);
            if (artefact is not null)
            {
                artefact.SurveyModelId = model.Id;
                continue;
            }

            db.ScanArtifacts.Add(new ScanArtifact
            {
                SurveyUploadId = upload.Id,
                SurveyModelId = model.Id,
                Kind = ScanArtifactKind.RawRecording,
                Format = FormatOf(file.Name),
                FileId = file.FileId,
                SizeBytes = file.SizeBytes,
                Sha256 = file.Sha256,
                StepId = ScanSteps.UploadStepId,
                Tool = ScanSteps.ApiTool,
                ParamsHash = ScanRecipes.ParamsHash("{}"),
                CreatedAt = now,
            });
        }

        await PointCloudUploadRun.SaveAsync(db, model, made, ct);
        return model;
    }

    /// <summary>
    /// What a refined session's run knows before the export: the facts of the scan it refines —
    /// the latest earlier version of the same survey that a run described — with that version's
    /// processing taken off, since none of it was done to this session. A failure when the survey
    /// holds no such version: the session is exported against nothing this server knows.
    /// </summary>
    /// <remarks>
    /// The recording's facts come across with the header's, GNSS included, so a session refined
    /// from a georeferenced recording stays one: its original and its products keep needing the
    /// exact location, as the version it came from did.
    /// </remarks>
    public static async Task<PointCloudFacts> RefinedFactsAsync(SilexGisDbContext db, SurveyUpload upload, CancellationToken ct)
    {
        var formats = SurveyModelKinds.FormatsOf(SurveyModelKind.PointCloud);
        var earlier = await (
            from model in db.SurveyModels.AsNoTracking()
            join version in db.SurveyUploads.AsNoTracking() on model.SurveyUploadId equals version.Id
            where version.CaveSurveyId == upload.CaveSurveyId
                && version.Id != upload.Id
                && version.VersionAt < upload.VersionAt
                && version.Source == SurveyOrigin.Mandeye
                && model.PointCloud != null
                && formats.Contains(model.Format)
            orderby version.VersionAt descending
            select model.PointCloud)
            .FirstOrDefaultAsync(ct);
        var facts = PointCloudFacts.FromJson(earlier)
            ?? throw new SurveyPipelineException(
                SurveyPipelineFailures.RecordingInvalid,
                "The refined session cannot be processed: the survey holds no earlier version of the scan it was refined from.");
        return facts with
        {
            TileBytes = null,
            TileCount = null,
            TilerVersion = null,
            DisplayVoxelM = null,
            DisplayPointCount = null,
            DisplayEveryNth = null,
            OriginShift = null,
            Estimate = null,
            Advice = null,
        };
    }

    /// <summary>
    /// Records a refined session's facts on the upload's scan model, making the model on the first
    /// run, and the session itself as the upload's own HDMapping session, so the export reads it
    /// the way it reads a session the odometry made. Until the export replaces it, the model is
    /// shown from the session's file — which the exact-location gate guards as the scanner's path —
    /// and a rerun after an export keeps showing the export.
    /// </summary>
    public static async Task<SurveyModel> RecordSessionAsync(
        SilexGisDbContext db, SurveyUpload upload, ArchivedRecordingFile session, PointCloudFacts facts, CancellationToken ct)
    {
        var exportStored = await db.ScanArtifacts.AnyAsync(
            a => a.SurveyUploadId == upload.Id && a.StepId == HdmappingExportStep.StepId && a.Kind == ScanArtifactKind.PointCloudLaz, ct);
        var formats = SurveyModelKinds.FormatsOf(SurveyModelKind.PointCloud);
        var existing = exportStored
            ? await db.SurveyModels.FirstOrDefaultAsync(m => m.SurveyUploadId == upload.Id && formats.Contains(m.Format), ct)
            : null;
        var keptFileId = existing?.FileId;
        var keptFormat = existing?.Format;
        var keptFacts = existing is null ? null : PointCloudFacts.FromJson(existing.PointCloud);
        var (model, made) = await PointCloudUploadRun.ModelOfAsync(db, upload, session.FileId, SurveyModelFormat.Laz, facts, ct);
        if (existing is not null && keptFileId is { } shown && keptFacts is not null)
        {
            model.FileId = shown;
            model.Format = keptFormat ?? model.Format;
            model.PointCloud = (keptFacts with { Recording = facts.Recording }).ToJson();
        }

        var known = await db.ScanArtifacts.FirstOrDefaultAsync(
            a => a.SurveyUploadId == upload.Id && a.StepId == ScanSteps.UploadStepId && a.FileId == session.FileId, ct);
        if (known is not null)
        {
            known.SurveyModelId = model.Id;
        }
        else
        {
            db.ScanArtifacts.Add(new ScanArtifact
            {
                SurveyUploadId = upload.Id,
                SurveyModelId = model.Id,
                Kind = ScanArtifactKind.HdmSession,
                Format = "zip",
                FileId = session.FileId,
                SizeBytes = session.SizeBytes,
                Sha256 = session.Sha256,
                StepId = ScanSteps.UploadStepId,
                Tool = ScanSteps.ApiTool,
                ParamsHash = ScanRecipes.ParamsHash("{}"),
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        await PointCloudUploadRun.SaveAsync(db, model, made, ct);
        return model;
    }

    /// <summary>A recording file's format as an artefact names it: its extension, lower case, without the dot.</summary>
    private static string FormatOf(string name)
    {
        var extension = Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
        return extension.Length is > 0 and <= 40 ? extension : "bin";
    }

    private static SurveyPipelineException Invalid(string why) =>
        new(SurveyPipelineFailures.RecordingInvalid, "The recording cannot be processed: " + why + ".");

    /// <summary>The first few lines of a log and its last non-empty line, read without reading the middle when the stream can seek.</summary>
    private static async Task<(IReadOnlyList<string> Head, string? Last)> HeadAndTailAsync(Stream stream, CancellationToken ct)
    {
        var head = new byte[TailBytes];
        var read = await stream.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, ct);
        var headLines = Lines(Encoding.UTF8.GetString(head, 0, read));
        if (read < head.Length)
        {
            // The whole log fitted: the head holds its last line too, unless the read cut a line.
            return (headLines, headLines.LastOrDefault());
        }

        string tailText;
        if (stream.CanSeek)
        {
            stream.Seek(Math.Max(read, stream.Length - TailBytes), SeekOrigin.Begin);
            var tail = new byte[TailBytes];
            var tailRead = await stream.ReadAtLeastAsync(tail, tail.Length, throwOnEndOfStream: false, ct);
            tailText = Encoding.UTF8.GetString(tail, 0, tailRead);
        }
        else
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            string? line;
            string? lastLine = null;
            while ((line = await reader.ReadLineAsync(ct)) is not null)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    lastLine = line;
                }
            }

            tailText = lastLine ?? "";
        }

        // The head's last line may be cut by the read; the lines before it are whole.
        var tailLines = Lines(tailText);
        return (headLines.Count > 1 ? [.. headLines.Take(headLines.Count - 1)] : headLines, tailLines.LastOrDefault());
    }

    private static List<string> Lines(string text) =>
        [.. text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => !string.IsNullOrWhiteSpace(l))];

    private static async Task<byte[]> SmallAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[SmallFileCap];
        var read = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, ct);
        return buffer[..read];
    }
}
