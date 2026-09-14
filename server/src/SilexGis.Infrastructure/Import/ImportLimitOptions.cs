// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Infrastructure.Import;

/// <summary>
/// How much of an uploaded survey file one review, and one confirmation, may take on.
/// </summary>
/// <remarks>
/// Both were constants, and both were wrong for the same reason: they were sized against a
/// hand-recorded list of cave entrances, and the files people actually upload are GPS units
/// emptied at the end of a season. A day's track log is tens of thousands of points on its own.
/// </remarks>
public sealed class ImportLimitOptions
{
    public const string SectionName = "Import";

    /// <summary>
    /// The most objects one confirmation creates.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not a storage limit. The write path recomputes the containment closure once per created
    /// object, so a confirmation of thousands is a slow request rather than a cheap one — and
    /// splitting it also splits the undo unit into something a person can reason about, which is
    /// worth something on its own the first time somebody imports the wrong file.
    /// </para>
    /// <para>
    /// Raised from the original thousand because a thousand is below what a single field season
    /// produces, and being told to "confirm them in smaller batches" ten times in a row is not a
    /// limit anybody experiences as protective.
    /// </para>
    /// <para>
    /// Now equal to <see cref="MaxScanRows"/>, so that a review somebody can see is a review they
    /// can confirm in one act. The reason the two differed no longer holds: the ceiling was set
    /// against a confirmation running inside the request, where a reverse proxy gave up at sixty
    /// seconds and took the whole batch back with it. Confirmation runs on the job queue, so what
    /// a large one costs is a job that takes a long time rather than one that cannot finish.
    /// </para>
    /// <para>
    /// What remains true, and is the reason to lower it: one confirmation is one transaction and
    /// one undo unit. A very large one holds a write transaction open for as long as it runs, and
    /// reverts as a single thing — which is what you want the first time somebody confirms the
    /// wrong file, and unwieldy if what you actually wanted was to undo part of it.
    /// </para>
    /// </remarks>
    public int MaxCommitItems { get; set; } = 150000;

    /// <summary>
    /// The most rows one scan reads out of an uploaded file to build the review list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A file larger than this is still imported whole and still drawn as a layer — only the
    /// review is bounded, and the reviewer is told it was, because a silently truncated candidate
    /// list reads exactly like a complete one.
    /// </para>
    /// <para>
    /// The device-archive reader takes the same figure and answers it differently: there is no
    /// "imported whole" half there — a recording either becomes a reviewed set of positions or does
    /// not — so past this it refuses the recording by name instead of truncating it. Same reasoning,
    /// opposite conclusion, because the two have different second halves.
    /// </para>
    /// <para>
    /// The scan is a bounded read of rows already stored, and the review it feeds is paged to the
    /// client, so the figure buys reviewable rows rather than rows on a screen: what it costs is
    /// the one scan that ranks and de-duplicates them, not the drawing of them. Raised because a
    /// season emptied off a GPS unit runs past the old figure and the part that was cut off is
    /// the part nobody knew to look for.
    /// </para>
    /// </remarks>
    public int MaxScanRows { get; set; } = 150000;

    /// <summary>
    /// The largest device database one review will unpack out of an export archive, in bytes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It bounds the <em>unpacked</em> size rather than the upload, and that is the point: a zip
    /// entry states its own uncompressed length and a hostile one states it wrongly, so the copy
    /// is stopped by counting bytes as they arrive rather than by believing the directory.
    /// </para>
    /// <para>
    /// Sized against what a phone's database actually is — a cave register with its places and a
    /// few seasons of scans is single-digit megabytes — rather than against what a disk would
    /// tolerate, because this figure is not a disk limit. Nothing is staged between the upload and
    /// the import, so the database is unpacked again on every listing, every preview and the
    /// confirmation, and concurrent requests are not serialised: whatever this permits is what one
    /// uploaded file can cause to be written to the scratch filesystem, repeatedly, on demand. A
    /// generous figure here was an amplifier with a ceiling, not a ceiling. The cheap half of the
    /// defence is in the reader, which refuses an entry whose first sixteen bytes do not say
    /// "SQLite" before it writes a page of it anywhere.
    /// </para>
    /// <para>
    /// Media and map tiles in the same archive are never unpacked at all, whatever this says: the
    /// import reads the database and nothing else.
    /// </para>
    /// </remarks>
    public long MaxArchiveDatabaseBytes { get; set; } = 64L * 1024 * 1024;
}
