// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.TripLogs;

/// <summary>What was kept when a write-up was filed against its trip.</summary>
public sealed record TripReportSavedDto(Guid DocumentId, Guid FileId, string FileName);

/// <summary>
/// A trip, written up as a document.
/// </summary>
/// <remarks>
/// <para>
/// The document is built from the answer the trip's own read gives this caller, through that
/// read and no other. That is the whole design: every rule about who may see what — which caves
/// are named, whether the account of what went wrong is included, whose name appears — is
/// applied once, where the screen applies it, and this renders what it is handed. A builder that
/// went to the tables for its own copy would be a second place those rules live, and two places
/// is how a saved copy comes to state what the screen refuses to.
/// </para>
/// <para>
/// A generated document is downloaded by somebody who is signed in and may read the trip. It is
/// not published at an address, and there is no route here anybody can reach without an account.
/// </para>
/// <para>
/// One reading, but not always the caller's: a copy filed against the trip is reachable by every
/// reader of that trip, so it is built from the reading any account has instead. Same path, same
/// single application of the rules — the question of whose reading is answered once, at the top,
/// rather than by a second builder.
/// </para>
/// </remarks>
internal static class TripReportEndpoints
{
    /// <summary>How many photographs a written-up trip carries.</summary>
    /// <remarks>
    /// A bulletin article is not the archive: the rest of the trip's pictures are one click away
    /// in its gallery, and a document nobody can mail is a document nobody circulates.
    /// </remarks>
    private const int MaxPlates = 12;

    /// <summary>
    /// Which rendering is placed in the document.
    /// </summary>
    /// <remarks>
    /// One of the fixed set of sizes this application draws. Large enough to be worth printing,
    /// and — because it is a rendering — carrying none of the upload's metadata, which for a
    /// photograph taken at a cave holds the position it was taken at.
    /// </remarks>
    private const int PlateSize = 1200;

    /// <summary>
    /// The write-up as a file the caller keeps.
    /// </summary>
    public static async Task<Results<FileContentHttpResult, ProblemHttpResult>> DownloadAsync(
        Guid id,
        Guid? templateId,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
        ThumbnailService thumbnails,
        IDocumentWriter writer,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var built = await BuildAsync(
            id, templateId, db, access, ctx, ctx, userAccessor, protection, thumbnails, writer, ct);
        return built.Problem is { } problem
            ? problem
            : TypedResults.File(built.Bytes!, writer.ContentType, built.FileName!);
    }

    /// <summary>
    /// The write-up, kept against the trip in the slot its report document lives in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Filing it is a change to the trip, so it answers to who may change the trip rather than to
    /// who may read it. A previously generated write-up is taken off the trip: the slot holds the
    /// current one, and two of them side by side would leave which one is current to whichever
    /// happened to be listed first. A report somebody filed by hand is left exactly where it is —
    /// producing a document is not a licence to remove one nobody was asked about.
    /// </para>
    /// <para>
    /// What is filed is deliberately <em>not</em> the copy the person filing it would download. A
    /// file attached to a trip is reachable by everybody who may read that trip, so the filed copy
    /// is built from the reading any account has — the safety account withheld, no cave the
    /// weakest reader could not read or place, and only photographs open to any account. Building
    /// it from the filer's own reading would take a narrower audience's material and put it where
    /// a wider one collects it, which is the one thing a document that leaves the system must
    /// never do. Their own, fuller copy is a download away.
    /// </para>
    /// </remarks>
    public static async Task<Results<Ok<TripReportSavedDto>, ProblemHttpResult>> KeepAsync(
        Guid id,
        Guid? templateId,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
        ThumbnailService thumbnails,
        IDocumentWriter writer,
        ContentIntake intake,
        UploadIngestService ingest,
        IFileStore fileStore,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var trip = await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (ctx is null || trip is null || !(await access.DecideAsync(ctx, AccessAction.Read, trip, ct)).Allowed)
        {
            return ApiProblems.NotFound("trip_log.not_found");
        }

        if (!(await access.DecideAsync(ctx, AccessAction.Write, trip, ct)).Allowed)
        {
            return ApiProblems.Forbidden();
        }

        // Keeping the write-up files a document, so it answers to the same right as filing one by
        // hand. Being allowed to change a trip is not by itself a way to put documents into the
        // archive, and a door that skipped this would be exactly that.
        if (!CreateRules.MayCreate(ctx, AccessDomain.Documents, (Guid?)null))
        {
            return ApiProblems.Forbidden(CreateRules.ForbiddenCode);
        }

        // The reading the filed copy is built from: one that belongs to no account in particular,
        // so nothing in the file is there because of who happened to press the button. The trip
        // itself was read above, as this caller — a filed copy of a trip they may read is not a
        // trip anybody may read.
        var everyReader = await AccessContextResolver.ResolveForAnyAccountAsync(db, ct);
        var built = await BuildAsync(
            id, templateId, db, access, ctx, everyReader, userAccessor, protection, thumbnails, writer, ct);
        if (built.Problem is { } problem)
        {
            return problem;
        }

        using var bytes = new MemoryStream(built.Bytes!);
        var content = await intake.FromStreamAsync(bytes, built.FileName!, writer.ContentType, ct);

        // Allowed to be a duplicate on purpose: the document is built from fixed metrics and is
        // byte-identical when nothing about the trip changed, and regenerating an unchanged
        // write-up must file a report rather than quietly refuse one.
        var outcome = await ingest.RecordAsync(
            content,
            built.FileName!,
            ctx,
            new UploadDestination(
                AttachEntityType: AttachedEntityType.TripLog,
                AttachEntityId: id,
                AttachRole: AttachmentRole.Report),
            null,
            allowDuplicate: true,
            ct);

        if (outcome.Outcome != UploadItemOutcome.Stored || outcome.Content is null)
        {
            // Bytes land before the write path decides whether they belong to a document at all;
            // anything that did not become one takes its bytes back out.
            await fileStore.DeleteAsync(content.StoragePath, CancellationToken.None);
            return ApiProblems.BadRequest(
                "trip_log.report_not_stored", outcome.Reason ?? "The report could not be stored.");
        }

        // Only what this endpoint produced before. A club's own written report sits in the same
        // slot, and it is reachable through the trip and nowhere else — taking it off the trip
        // would leave nobody but whoever uploaded it able to find it again, silently, because
        // somebody else generated a write-up.
        var prefix = GeneratedNamePrefix(id);
        var superseded = await db.Attachments
            .Where(a => a.EntityType == AttachedEntityType.TripLog
                && a.EntityId == id
                && a.Role == AttachmentRole.Report
                && a.FileId != outcome.Content.File.Id
                && db.StoredFiles.Any(f => f.Id == a.FileId && f.OriginalName.StartsWith(prefix)))
            .ToListAsync(ct);
        if (superseded.Count > 0)
        {
            db.Attachments.RemoveRange(superseded);
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.Ok(new TripReportSavedDto(
            outcome.DocumentId ?? Guid.Empty, outcome.Content.File.Id, built.FileName!));
    }

    /// <summary>The bytes of a write-up, or the refusal the trip's own read would have given.</summary>
    private readonly record struct BuiltReport(
        byte[]? Bytes, string? FileName, ProblemHttpResult? Problem);

    /// <summary>
    /// Builds the document from one reading of the trip.
    /// </summary>
    /// <param name="ctx">
    /// Who is asking. Decides whether there is a document at all, and nothing else.
    /// </param>
    /// <param name="reading">
    /// Whose reading the document states. The caller's own for a download; the reading any
    /// account has for a copy that is filed where a whole audience reaches it. Every question of
    /// who may see what — which caves are named, whether the account of what went wrong is
    /// included, which photographs go in — is put to this one context and to nothing else.
    /// </param>
    private static async Task<BuiltReport> BuildAsync(
        Guid id,
        Guid? templateId,
        SilexGisDbContext db,
        IAccessService access,
        AccessContext? ctx,
        AccessContext? reading,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
        ThumbnailService thumbnails,
        IDocumentWriter writer,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var trip = await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

        // The same refusal the screen gets, letter for letter: a file that existed where a page
        // said nothing would be the answer the page declined to give.
        if (ctx is null || reading is null || user is null || trip is null
            || !(await access.DecideAsync(ctx, AccessAction.Read, trip, ct)).Allowed)
        {
            return new BuiltReport(null, null, ApiProblems.NotFound("trip_log.not_found"));
        }

        // Which layout the document is written in. A layout that was named and is not there is a
        // refusal rather than a quiet fall back to another one: somebody asking for the club's
        // bulletin layout and being handed the shipped one would not be told.
        var body = await TripReportTemplateEndpoints.BodyForAsync(db, templateId, ct);
        if (body is null)
        {
            return new BuiltReport(
                null, null, ApiProblems.NotFound(TripReportTemplateEndpoints.NotFoundCode));
        }

        // Read here rather than trusted: a layout is checked when it is stored, and one that has
        // since become unreadable falls back to the shipped layout so a broken row cannot stop a
        // club producing its write-ups.
        var read = ReportTemplateFormat.Parse(body);
        var parts = read.Ok ? read.Parts : ReportTemplateFormat.Parse(ReportTemplateFormat.Default).Parts;

        // The one read of the trip, exactly as the page makes it. Everything the document says
        // about who may see what was decided here.
        var dto = (await TripLogEndpoints.MapWithChildrenAsync(db, access, protection, reading, user, [trip], ct))[0];

        var tripType = dto.TripTypeId is { } typeId
            ? await db.TripTypes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == typeId, ct)
            : null;
        var groupName = dto.OrganizingCavingGroupId is { } groupId
            ? await db.CavingGroups.AsNoTracking()
                .Where(x => x.Id == groupId).Select(x => x.Name).FirstOrDefaultAsync(ct)
            : null;

        // Named, not placed. The identifiers are the list the trip read produced, which is the
        // redacted one — a cave this reading may not place is not on it — and nothing here
        // resolves a coordinate for any of them.
        //
        // Naming one is still a read of the cave, and the trip's own list does not carry that
        // right: a cave nobody but its owner may open can be named on a trip half the club reads.
        // So the names come through the cave read, and a cave this reading cannot open keeps the
        // identifier the trip carried — which is exactly what the screen shows for it.
        var caveNames = dto.CaveIds.Count == 0
            ? []
            : await db.Features.AsNoTracking()
                .VisibleTo(reading, db.Features, db.FeatureSetMembers)
                .Where(f => dto.CaveIds.Contains(f.Id))
                .ToDictionaryAsync(f => f.Id, f => f.Name ?? string.Empty, ct);

        var roleIds = dto.Participants.Concat(dto.Proposers).Select(x => x.RoleId).Distinct().ToList();
        var roleNames = roleIds.Count == 0
            ? []
            : await db.TripParticipantRoles.AsNoTracking()
                .Where(r => roleIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, r => r.Name, ct);

        var content = new TripReportContent(
            dto,
            tripType?.Name,
            groupName,
            caveNames,
            roleNames,
            SectionTitles(tripType),
            await PlatesAsync(id, db, reading, thumbnails, ct));

        var fileName = $"{GeneratedNamePrefix(id)}{DateTime.UtcNow:yyyyMMdd}.{writer.Extension}";
        return new BuiltReport(writer.Write(TripReportDocument.Blocks(content, parts)), fileName, null);
    }

    /// <summary>
    /// What every generated write-up of this trip is called, up to the day it was produced.
    /// </summary>
    /// <remarks>
    /// One definition, because it is also how a previously generated write-up is recognised when
    /// a new one takes its place. Written down here rather than inferred, so the two can never
    /// come to mean different things.
    /// </remarks>
    private static string GeneratedNamePrefix(Guid tripId) => $"trip-report-{tripId.ToString("N")[..8]}-";

    /// <summary>
    /// The pictures, obtained the way the gallery obtains them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Through the photograph read rule, narrowed to this trip — the same composition the gallery
    /// listing is built from — so a document carries exactly the pictures the reading it is built
    /// from may see on the trip's own page and no others.
    /// </para>
    /// <para>
    /// What goes in is a rendering, drawn by this application with every metadata profile removed,
    /// and never the upload: an upload of a photograph taken at a cave carries the position it was
    /// taken at. For the same reason nothing here writes a picture's position into the document,
    /// which is why the question of who may be told a capture point does not arise.
    /// </para>
    /// </remarks>
    private static async Task<List<TripReportPlate>> PlatesAsync(
        Guid tripId,
        SilexGisDbContext db,
        AccessContext reading,
        ThumbnailService thumbnails,
        CancellationToken ct)
    {
        var photographs = (await PhotographReads.VisiblePhotographsAsync(db, reading, ct))
            .AttachedToTrip(db, tripId);

        var documents = await photographs
            .OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.Id)
            .Take(MaxPlates)
            .ToListAsync(ct);
        var rows = await PhotographReads.RowsAsync(db, documents, ct);

        var plates = new List<TripReportPlate>(rows.Count);
        foreach (var row in rows)
        {
            var rendering = await thumbnails.GetOrCreateAsync(
                row.File.Id, row.File.StoragePath, PlateSize, ct, row.File.OrientationQuarterTurns);
            var caption = new[]
            {
                string.IsNullOrWhiteSpace(row.Details?.Caption) ? row.Document.Title : row.Details!.Caption,
                row.PhotographerLabel ?? row.Details?.PhotographerName,
            }.Where(x => !string.IsNullOrWhiteSpace(x));

            plates.Add(new TripReportPlate(
                await File.ReadAllBytesAsync(rendering, ct), string.Join(" — ", caption)));
        }
        return plates;
    }

    /// <summary>
    /// What a purpose calls the questions in each of its three sections.
    /// </summary>
    /// <remarks>
    /// Read off the schema the answers were written against, so a document names a field the way
    /// the form that collected it did. A schema that says nothing about a key leaves the key
    /// itself, which is worse to read than a title and better than dropping the answer.
    /// </remarks>
    private static Dictionary<TripSectionKey, IReadOnlyDictionary<string, string>> SectionTitles(
        TripType? tripType)
    {
        var titles = new Dictionary<TripSectionKey, IReadOnlyDictionary<string, string>>();
        if (tripType is null)
        {
            return titles;
        }

        Add(TripSectionKey.FieldData, tripType.FieldDataSchema);
        Add(TripSectionKey.Logistics, tripType.LogisticsSchema);
        Add(TripSectionKey.Safety, tripType.SafetySchema);
        return titles;

        void Add(TripSectionKey key, string? schema)
        {
            if (string.IsNullOrWhiteSpace(schema))
            {
                return;
            }

            try
            {
                using var parsed = JsonDocument.Parse(schema);
                if (!parsed.RootElement.TryGetProperty("properties", out var properties)
                    || properties.ValueKind != JsonValueKind.Object)
                {
                    return;
                }

                var map = new Dictionary<string, string>();
                foreach (var property in properties.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Object
                        && property.Value.TryGetProperty("title", out var title)
                        && title.ValueKind == JsonValueKind.String
                        && title.GetString() is { Length: > 0 } text)
                    {
                        map[property.Name] = text;
                    }
                }

                titles[key] = map;
            }
            catch (JsonException)
            {
                // A schema that will not parse names nothing; the keys stand in for its titles.
            }
        }
    }
}
