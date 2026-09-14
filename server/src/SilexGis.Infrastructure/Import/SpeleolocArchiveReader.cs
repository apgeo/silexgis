// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.IO.Compression;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;

namespace SilexGis.Infrastructure.Import;

/// <summary>Raised when the uploaded archive cannot be read as a device export at all.</summary>
public sealed class SpeleolocArchiveException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>One recording in the archive, as the device wrote it.</summary>
/// <param name="DocumentCount">
/// How many documents were filed under the recording. A count, never the files: what somebody
/// photographed underground is theirs, and an importer that opened them to say so would be reading
/// the archive's private half to fill in a number.
/// </param>
public sealed record SpeleolocArchiveTrip(
    Guid Id,
    Guid CaveId,
    string? CaveTitle,
    string Title,
    string? Description,
    string? Log,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    Guid? DeviceUserId,
    int DocumentCount,
    int PointCount);

/// <summary>
/// One scan: a marked place and the moment somebody stood at it. Never a coordinate and never a
/// track — the device records a discrete event, which is exactly why this import has to settle
/// which station the place is rather than plotting anything.
/// </summary>
public sealed record SpeleolocArchivePoint(
    Guid Id,
    Guid? PlaceId,
    string? PlaceTitle,
    double? PlaceDepthM,
    bool PlaceIsEntrance,
    DateTimeOffset ScannedAt,
    string? Notes,
    Guid? DeviceUserId);

/// <summary>
/// Reads a SpeleoLoc export archive, every time it is asked, and never more of it than the trip
/// history needs.
///
/// <para>
/// Three properties are the whole of this class.
/// </para>
/// <para>
/// It reads one entry. An export archive carries the device's database, the media somebody chose
/// to include, and — from builds made for it — a file of stored server credentials. Only the
/// database is ever looked at, by name; nothing else is opened, copied, listed back to a caller or
/// written anywhere. That is not tidiness, it is the reason the whole archive can be handed to an
/// importer at all.
/// </para>
/// <para>
/// It stages nothing between the upload and the import. The archive is opened again on every
/// preview and again at the confirmation, because the reviewer can change what the rows mean —
/// which recording, which model, which person a device account stands for — and a staged parse
/// nobody re-ran is a review of an archive nobody has any more.
/// </para>
/// <para>
/// It treats the file as hostile. The connection is read-only and the session is put in
/// query-only mode; the database is copied out of the zip under a name this application chose, so
/// an entry called <c>../../etc/anything</c> is a name that is never used; and the copy is stopped
/// by counting bytes rather than by believing the length the archive states about itself.
/// </para>
/// <para>
/// It is bounded at both ends, and both bounds exist because of the paragraph above this one.
/// Staging nothing means the entry is unpacked again on every request and nothing serialises them,
/// so the size ceiling is what one uploaded file can have written to the scratch filesystem
/// repeatedly rather than once — hence the entry is refused on the format's own first sixteen
/// bytes before a page of it is written, and hence the ceiling is sized against a phone's database
/// rather than against a disk. And every statement carries a <c>limit</c>, because a recording is
/// materialised whole before anything pages it: unbounded, the size of one request would be the
/// archive's choice. Past the limit a recording is refused rather than truncated — a list cut off
/// at a ceiling reads exactly like a complete one, and the point of the review is that somebody saw
/// every scan.
/// </para>
/// </summary>
public sealed class SpeleolocArchiveReader(IFileStore files, IOptions<ImportLimitOptions> limits)
{
    /// <summary>The database entry an export archive carries, at the archive's root.</summary>
    private const string DatabaseEntryName = "speleo_loc.sqlite";

    /// <summary>What the file format itself says it is, first sixteen bytes, including the terminator.</summary>
    private static readonly byte[] SqliteMagic = "SQLite format 3\0"u8.ToArray();

    /// <summary>
    /// The recordings the archive holds, newest first, and the device accounts that made them.
    /// </summary>
    public async Task<IReadOnlyList<SpeleolocArchiveTrip>> ListTripsAsync(
        StoredFile file, CancellationToken ct = default)
    {
        using var database = await OpenAsync(file, ct);
        return await GuardedAsync(() => TripsAsync(database.Connection, limits.Value.MaxScanRows, ct));
    }

    /// <summary>
    /// One recording and its scans, or null when the archive does not hold it. Null rather than an
    /// exception because a recording that has gone is a choice the reviewer has to remake, not a
    /// broken file.
    /// </summary>
    public async Task<(SpeleolocArchiveTrip Trip, IReadOnlyList<SpeleolocArchivePoint> Points)?> ReadTripAsync(
        StoredFile file, Guid tripId, CancellationToken ct = default)
    {
        // One open for both halves. The archive is unpacked to read it, and unpacking it twice to
        // answer one question would double the cost of every preview the reviewer asks for.
        using var database = await OpenAsync(file, ct);
        var ceiling = limits.Value.MaxScanRows;
        return await GuardedAsync(async () =>
        {
            var trip = (await TripsAsync(database.Connection, ceiling, ct)).FirstOrDefault(t => t.Id == tripId);
            if (trip is null)
            {
                return ((SpeleolocArchiveTrip, IReadOnlyList<SpeleolocArchivePoint>)?)null;
            }

            var points = new List<SpeleolocArchivePoint>();
            using var command = database.Connection.CreateCommand();
            command.CommandText = SpeleolocArchiveSql.PointsOfTrip;
            command.Parameters.AddWithValue("@trip", Bytes(tripId));
            // One past the ceiling, so that a recording at the ceiling is read and one beyond it is
            // known to be beyond it rather than silently cut off at the last row that fitted.
            command.Parameters.AddWithValue("@limit", (long)ceiling + 1);
            using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (points.Count >= ceiling)
                {
                    // Refused rather than truncated. The whole point of the review is that somebody
                    // sees every scan before any of it becomes a position, and a list cut off at a
                    // ceiling reads exactly like a complete one — so a recording nobody could review
                    // is a recording this import will not pretend to have read.
                    throw new SpeleolocArchiveException(
                        SpeleolocImportCodes.RecordingTooLarge,
                        $"That recording holds more than the {ceiling} scans one review reads.");
                }

                if (Uuid(reader, "point_uuid") is not { } id)
                {
                    continue;
                }

                points.Add(new SpeleolocArchivePoint(
                    id,
                    Uuid(reader, "place_uuid"),
                    Text(reader, "place_title"),
                    Number(reader, "place_depth"),
                    Count(reader, "place_is_entrance") != 0,
                    Moment(reader, "scanned_at") ?? DateTimeOffset.UnixEpoch,
                    Text(reader, "notes"),
                    Uuid(reader, "device_user_uuid")));
            }

            return (trip, (IReadOnlyList<SpeleolocArchivePoint>)points);
        });
    }

    private static async Task<IReadOnlyList<SpeleolocArchiveTrip>> TripsAsync(
        SqliteConnection connection, int ceiling, CancellationToken ct)
    {
        var counts = new Dictionary<Guid, int>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = SpeleolocArchiveSql.PointCounts;
            command.Parameters.AddWithValue("@limit", (long)ceiling + 1);
            using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (counts.Count >= ceiling)
                {
                    throw new SpeleolocArchiveException(
                        SpeleolocImportCodes.ArchiveTooLarge,
                        $"That archive holds more than the {ceiling} recordings one review lists.");
                }

                if (Uuid(reader, "trip_uuid") is { } id)
                {
                    counts[id] = Count(reader, "point_count");
                }
            }
        }

        var trips = new List<SpeleolocArchiveTrip>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = SpeleolocArchiveSql.Trips;
            command.Parameters.AddWithValue("@limit", (long)ceiling + 1);
            using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (trips.Count >= ceiling)
                {
                    throw new SpeleolocArchiveException(
                        SpeleolocImportCodes.ArchiveTooLarge,
                        $"That archive holds more than the {ceiling} recordings one review lists.");
                }

                if (Uuid(reader, "trip_uuid") is not { } id)
                {
                    // A recording with no identifier cannot be chosen, decided about, or pointed
                    // at by a batch line. Skipped rather than refused: one unusable row must not
                    // cost the reviewer the rest of the archive.
                    continue;
                }

                trips.Add(new SpeleolocArchiveTrip(
                    id,
                    Uuid(reader, "cave_uuid") ?? Guid.Empty,
                    Text(reader, "cave_title"),
                    Text(reader, "title") ?? string.Empty,
                    Text(reader, "description"),
                    Text(reader, "log"),
                    Moment(reader, "started_at") ?? DateTimeOffset.UnixEpoch,
                    Moment(reader, "ended_at"),
                    Uuid(reader, "device_user_uuid"),
                    Count(reader, "document_count"),
                    counts.GetValueOrDefault(id)));
            }
        }

        return trips;
    }

    // ---------- opening ----------

    /// <summary>
    /// The archive's database, copied out to a scratch file and opened read-only. The scratch copy
    /// exists because SQLite needs a seekable file it can read pages out of; the copy is deleted
    /// when the handle is.
    /// </summary>
    private async Task<OpenDatabase> OpenAsync(StoredFile file, CancellationToken ct)
    {
        var ceiling = limits.Value.MaxArchiveDatabaseBytes;
        var scratch = Path.Combine(
            Path.GetTempPath(), $"silexgis-speleoloc-{Guid.CreateVersion7():N}", DatabaseEntryName);
        Directory.CreateDirectory(Path.GetDirectoryName(scratch)!);

        try
        {
            await ExtractDatabaseAsync(file, scratch, ceiling, ct);
        }
        catch
        {
            Cleanup(scratch);
            throw;
        }

        SqliteConnection? connection = null;
        try
        {
            connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = scratch,
                Mode = SqliteOpenMode.ReadOnly,
                // Nothing here is shared between requests, and a shared cache would let one
                // review's read hold a lock another's is waiting on.
                Cache = SqliteCacheMode.Private,
                ForeignKeys = false,
            }.ToString());
            connection.Open();

            using (var guard = connection.CreateCommand())
            {
                // Read-only is already the open mode; this says it a second time at the session
                // level, and turns off the one thing a read still runs on somebody else's behalf —
                // the schema's own expressions, which in a hostile file are code this process
                // would otherwise evaluate while it reads.
                guard.CommandText = "pragma query_only = on; pragma trusted_schema = off";
                guard.ExecuteNonQuery();
            }

            RequireSchema(connection);
            return new OpenDatabase(connection, scratch);
        }
        catch (SqliteException e)
        {
            connection?.Dispose();
            Cleanup(scratch);
            throw new SpeleolocArchiveException(
                SpeleolocImportCodes.ArchiveUnreadable,
                $"The archive's database could not be opened ({e.SqliteErrorCode}).");
        }
        catch
        {
            connection?.Dispose();
            Cleanup(scratch);
            throw;
        }
    }

    /// <summary>
    /// Copies the device database out of the upload. The upload is either the archive, in which
    /// case one entry is taken from it by name, or the bare database itself — the device offers
    /// both and refusing the second would be refusing the simpler of the two for no reason.
    /// </summary>
    private async Task ExtractDatabaseAsync(StoredFile file, string scratch, long ceiling, CancellationToken ct)
    {
        string absolute;
        try
        {
            absolute = files.GetAbsolutePath(file.StoragePath);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException)
        {
            throw new SpeleolocArchiveException(
                SpeleolocImportCodes.ArchiveUnreadable, "The upload could not be located.");
        }

        if (!File.Exists(absolute))
        {
            throw new SpeleolocArchiveException(
                SpeleolocImportCodes.ArchiveUnreadable, "The upload could not be read.");
        }

        if (await LooksLikeDatabaseAsync(absolute, ct))
        {
            await using var plain = File.OpenRead(absolute);
            await CopyBoundedAsync(plain, scratch, ceiling, requireHeader: false, ct);
            return;
        }

        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(absolute);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            throw new SpeleolocArchiveException(
                SpeleolocImportCodes.ArchiveUnreadable,
                "The upload is neither a device archive nor a device database.");
        }

        using (archive)
        {
            // By name, at the root, and nothing else. The archive's other entries — photographs,
            // map tiles, and on some builds a file of stored server credentials — are not opened,
            // not listed and not copied. Matching by extension instead would read whichever of
            // them happened to be called .sqlite.
            var entry = archive.GetEntry(DatabaseEntryName)
                ?? throw new SpeleolocArchiveException(
                    SpeleolocImportCodes.ArchiveUnreadable,
                    $"The archive carries no {DatabaseEntryName}.");

            if (entry.Length > ceiling)
            {
                throw new SpeleolocArchiveException(
                    SpeleolocImportCodes.ArchiveTooLarge,
                    $"The archive's database is larger than {ceiling / (1024 * 1024)} MB.");
            }

            try
            {
                await using var stream = entry.Open();
                await CopyBoundedAsync(stream, scratch, ceiling, requireHeader: true, ct);
            }
            catch (Exception e) when (e is InvalidDataException or IOException)
            {
                // The directory at the end of a zip describes entries it does not contain the
                // bytes of. Opening the archive validated the directory; a compressed stream that
                // stops or disagrees with it fails here instead, and it is still a refusal about
                // the upload rather than a fault with no code on it.
                throw new SpeleolocArchiveException(
                    SpeleolocImportCodes.ArchiveUnreadable,
                    $"The archive's {DatabaseEntryName} could not be unpacked.");
            }
        }
    }

    /// <summary>
    /// Whether the upload is a bare database rather than an archive, decided by the format's own
    /// header rather than by the name it arrived under.
    /// </summary>
    private static async Task<bool> LooksLikeDatabaseAsync(string absolute, CancellationToken ct)
    {
        var header = new byte[SqliteMagic.Length];
        await using var stream = File.OpenRead(absolute);
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        return read == header.Length && header.AsSpan().SequenceEqual(SqliteMagic);
    }

    /// <summary>
    /// Writes the stream out, refusing anything past the ceiling as it arrives. The archive states
    /// its own uncompressed length and that statement was already checked; this is the half that
    /// does not depend on the statement being true.
    ///
    /// <para>
    /// <paramref name="requireHeader"/> makes the first sixteen bytes decide whether the rest is
    /// written at all, and it is the difference between a ceiling and a defence. A zip entry
    /// compresses half a gigabyte of zeros into a few hundred kilobytes and states its own
    /// uncompressed length honestly, so it passes every upload limit and every length check here;
    /// the byte-counting ceiling below does stop it, but only after the whole of it has been
    /// written to the scratch filesystem — once per request, for every caller who asks, with
    /// nothing serialising them. Asking the file what it says it is costs sixteen bytes and one
    /// comparison, and it is the only check here that happens before the write.
    /// </para>
    /// </summary>
    private static async Task CopyBoundedAsync(
        Stream source, string scratch, long ceiling, bool requireHeader, CancellationToken ct)
    {
        await using var target = File.Create(scratch);
        var buffer = new byte[81920];
        long total = 0;

        if (requireHeader)
        {
            var header = buffer.AsMemory(0, SqliteMagic.Length);
            var got = await source.ReadAtLeastAsync(header, SqliteMagic.Length, throwOnEndOfStream: false, ct);
            if (got != SqliteMagic.Length || !header.Span.SequenceEqual(SqliteMagic))
            {
                throw new SpeleolocArchiveException(
                    SpeleolocImportCodes.ArchiveUnreadable,
                    $"The archive's {DatabaseEntryName} is not a SQLite database.");
            }

            total = got;
            await target.WriteAsync(header, ct);
        }

        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > ceiling)
            {
                throw new SpeleolocArchiveException(
                    SpeleolocImportCodes.ArchiveTooLarge,
                    $"The archive's database is larger than {ceiling / (1024 * 1024)} MB.");
            }

            await target.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }

    /// <summary>
    /// Turns a read that fails part-way into the same refusal an unopenable archive gets.
    ///
    /// <para>
    /// Opening the database proves that the file is SQLite and that five tables exist under the
    /// names this import expects. It proves nothing about their <em>columns</em>, and nothing about
    /// the pages a scan has not reached yet. An export written by an older device schema — one
    /// without <c>log</c>, say — and a file whose first page is intact while its later ones are not
    /// both fail here rather than at the open, and without this they leave the slice as an
    /// unhandled fault with no stable code on it.
    /// </para>
    /// </summary>
    private static async Task<T> GuardedAsync<T>(Func<Task<T>> read)
    {
        try
        {
            return await read();
        }
        catch (SqliteException e)
        {
            throw new SpeleolocArchiveException(
                SpeleolocImportCodes.ArchiveUnreadable,
                $"The archive's database could not be read ({e.SqliteErrorCode}).");
        }
    }

    /// <summary>
    /// Refuses a database that is not a device export. A SQLite file is a container rather than a
    /// schema, so a stranger's database opens perfectly well and then answers "no such table" from
    /// four separate places — one refusal naming the format is worth four naming its tables.
    /// </summary>
    private static void RequireSchema(SqliteConnection connection)
    {
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = SpeleolocArchiveSql.PresentTables;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                present.Add(reader.GetString(0));
            }
        }

        var missing = SpeleolocArchiveSql.RequiredTables.Where(t => !present.Contains(t)).ToList();
        if (missing.Count > 0)
        {
            throw new SpeleolocArchiveException(
                SpeleolocImportCodes.ArchiveUnreadable,
                "The database is not a device export: it has no " + string.Join(", ", missing) + ".");
        }
    }

    private static void Cleanup(string scratch)
    {
        try
        {
            var directory = Path.GetDirectoryName(scratch);
            if (directory is not null)
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A scratch copy that outlives its read is litter, not a failure to report to the
            // person who asked for a preview.
        }
    }

    // ---------- reading one value ----------

    /// <summary>
    /// The 16-byte identifier the device minted, read from its hexadecimal form. The bytes are in
    /// the order the standard prints them, which is not the order this platform's own constructor
    /// assumes — the two differ in the first three fields, and a reader that took the default
    /// would produce identifiers that are wrong and still look plausible.
    /// </summary>
    private static Guid? Uuid(SqliteDataReader reader, string column)
    {
        var text = Text(reader, column);
        if (text is null || text.Length != 32)
        {
            return null;
        }

        try
        {
            return new Guid(Convert.FromHexString(text), bigEndian: true);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static byte[] Bytes(Guid id)
    {
        var bytes = new byte[16];
        id.TryWriteBytes(bytes, bigEndian: true, out _);
        return bytes;
    }

    private static string? Text(SqliteDataReader reader, string column)
    {
        var index = reader.GetOrdinal(column);
        if (reader.IsDBNull(index))
        {
            return null;
        }

        var value = reader.GetString(index);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static int Count(SqliteDataReader reader, string column)
    {
        var index = reader.GetOrdinal(column);
        return reader.IsDBNull(index) ? 0 : reader.GetInt32(index);
    }

    /// <summary>
    /// A number the device stored under a declared type SQLite does not enforce, read back from
    /// its text form and parsed here. Invariant culture, because what SQLite prints is invariant
    /// and a server running under a comma-decimal locale would otherwise read 87.4 as 874.
    /// </summary>
    private static double? Number(SqliteDataReader reader, string column) =>
        double.TryParse(Text(reader, column), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    /// <summary>
    /// A moment the device recorded as milliseconds since the epoch, UTC. Read as text and parsed
    /// as a 64-bit integer: every one of these is past the 32-bit range, and a reader that let the
    /// width be decided for it turns every timestamp in the archive into January 2038.
    /// </summary>
    private static DateTimeOffset? Moment(SqliteDataReader reader, string column) =>
        long.TryParse(Text(reader, column), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : null;

    /// <summary>The open database and the scratch copy it is reading, disposed together.</summary>
    private sealed class OpenDatabase(SqliteConnection connection, string scratch) : IDisposable
    {
        public SqliteConnection Connection { get; } = connection;

        public void Dispose()
        {
            Connection.Dispose();
            Cleanup(scratch);
        }
    }
}
