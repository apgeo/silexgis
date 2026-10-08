// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Expeditions;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Trips;

namespace SilexGis.Api.Features.Expeditions;

/// <summary>What was kept when a camp's write-up was filed against it.</summary>
public sealed record ExpeditionReportSavedDto(Guid DocumentId, Guid FileId, string FileName);

/// <summary>
/// A camp, written up as one document over the trips it gathered.
/// </summary>
/// <remarks>
/// <para>
/// The whole point of a camp: a fortnight is one thing with one report, rather than twenty
/// disconnected logs somebody has to read in sequence. So this reads day by day and team by team,
/// in its own vocabulary, on the same small language a club already edits for trips.
/// </para>
/// <para>
/// The document is built out of readings this caller already has — the camp's own read, the same
/// visibility walk over trips its listing and its map apply, and the roster read with the two
/// rights that governs. Nothing here reaches past one of those for a second answer: a builder with
/// its own copy of a disclosure rule is how a saved copy comes to state what the screen refuses to.
/// </para>
/// <para>
/// Nothing on this document is a coordinate anybody could not read on screen. The caves the camp's
/// trips named are named and never placed — no position is resolved for any of them — and the only
/// shape written down is the camp's own working area, which is exact for every reader of the camp
/// and carries the sentence saying so inside the same string, so no layout can print the one and
/// leave out the other.
/// </para>
/// </remarks>
internal static class ExpeditionReportEndpoints
{
    /// <summary>How many photographs a written-up camp carries.</summary>
    /// <remarks>
    /// More than a trip's, because a fortnight is more than an afternoon — and still a bulletin
    /// article rather than the archive, which is one click away in the galleries of its trips.
    /// </remarks>
    private const int MaxPlates = 24;

    /// <summary>
    /// Which rendering is placed in the document. One of the fixed set of sizes this application
    /// draws, carrying none of the upload's metadata — which for a photograph taken at a cave holds
    /// the position it was taken at.
    /// </summary>
    private const int PlateSize = 1200;

    public static RouteGroupBuilder MapExpeditionReportEndpoints(this RouteGroupBuilder api)
    {
        var camps = api.MapGroup("/expeditions/{id:guid}").WithTags("Expeditions");

        camps.MapGet("/report", DownloadAsync)
            .WithSummary(
                "The camp written up as one document, in the layout named or the club's chosen "
                + "one. Built from what this caller may read: a trip they may not open contributes "
                + "nothing to it. 'format=pdf' asks for it as a PDF, which an installation "
                + "without the document converter refuses.");
        camps.MapPost("/report", KeepAsync)
            .WithSummary(
                "Files the write-up against the camp, superseding the last one this route "
                + "produced. Built from the reading any account has, because everybody who may "
                + "read the camp reaches what is filed on it.");

        return api;
    }

    /// <summary>The write-up as a file the caller keeps.</summary>
    /// <param name="format">
    /// The format it is wanted in: nothing, or <c>docx</c>, for the document this application
    /// writes; <c>pdf</c> for that same document laid out by the installation's conversion
    /// service, where it runs one.
    /// </param>
    public static async Task<Results<FileContentHttpResult, ProblemHttpResult>> DownloadAsync(
        Guid id,
        Guid? templateId,
        string? format,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
        ThumbnailService thumbnails,
        IDocumentWriter writer,
        WriteUpPdfConverter pdf,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(pdf);
        var ctx = await accessAccessor.GetAsync(ct);
        var built = await BuildAsync(
            id, templateId, db, access, ctx, ctx, userAccessor, protection, thumbnails, writer,
            () => ReportDownloads.RefusalBeforeBuilding(format, pdf),
            ct);
        if (built.Problem is { } problem)
        {
            return problem;
        }

        return await ReportDownloads.AnswerAsync(built.Bytes!, built.FileName!, format, writer, pdf, ct);
    }

    /// <summary>
    /// The write-up, kept against the camp in the slot its report document lives in.
    /// </summary>
    /// <remarks>
    /// Filing it is a change to the camp, so it answers to who may change the camp rather than to
    /// who may read it, and to the right to file a document at all — being allowed to edit a camp
    /// is not by itself a way of putting documents into the archive.
    ///
    /// What is filed is deliberately not the copy the person filing it would download. A file
    /// attached to a camp is reachable by everybody who may read that camp, and a camp is routinely
    /// readable by a wider audience than the trips gathered into it — so the filed copy is built
    /// from the reading any account has. Building it from the filer's own reading would take a
    /// narrower audience's material and put it where a wider one collects it, which is the one
    /// thing a document that leaves the system must never do. Their own, fuller copy is a download
    /// away.
    /// </remarks>
    public static async Task<Results<Ok<ExpeditionReportSavedDto>, ProblemHttpResult>> KeepAsync(
        Guid id,
        Guid? templateId,
        string? format,
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
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(intake);
        ArgumentNullException.ThrowIfNull(ingest);
        ArgumentNullException.ThrowIfNull(fileStore);

        var ctx = await accessAccessor.GetAsync(ct);
        var camp = await db.Expeditions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (ctx is null || camp is null || !(await access.DecideAsync(ctx, AccessAction.Read, camp, ct)).Allowed)
        {
            return ApiProblems.NotFound(ExpeditionEndpoints.NotFoundCode);
        }

        if (!(await access.DecideAsync(ctx, AccessAction.Write, camp, ct)).Allowed)
        {
            return ApiProblems.Forbidden();
        }

        if (!CreateRules.MayCreate(ctx, AccessDomain.Documents, (Guid?)null))
        {
            return ApiProblems.Forbidden(CreateRules.ForbiddenCode);
        }

        // The filed copy is the word-processor document and nothing else: it is the one a club
        // edits afterwards, and it must not depend on a service an installation may switch
        // off. Asked only of somebody who could have filed the write-up at all.
        if (ReportDownloads.RefusalWhenFiling(format) is { } formatRefused)
        {
            return formatRefused;
        }

        var everyReader = await AccessContextResolver.ResolveForAnyAccountAsync(db, ct);
        var built = await BuildAsync(
            id, templateId, db, access, ctx, everyReader, userAccessor, protection, thumbnails, writer,
            null, ct);
        if (built.Problem is { } problem)
        {
            return problem;
        }

        using var bytes = new MemoryStream(built.Bytes!);
        var content = await intake.FromStreamAsync(bytes, built.FileName!, writer.ContentType, ct);

        // Allowed to be a duplicate on purpose: the document is built from fixed metrics and is
        // byte-identical when nothing about the camp changed, and regenerating an unchanged
        // write-up must file a report rather than quietly refuse one.
        var outcome = await ingest.RecordAsync(
            content,
            built.FileName!,
            ctx,
            new UploadDestination(
                AttachEntityType: AttachedEntityType.Expedition,
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
                "expedition.report_not_stored", outcome.Reason ?? "The report could not be stored.");
        }

        // Only what this endpoint produced before, recognised by the name it gives its own output.
        // A club's own written report sits in the same slot and is left exactly where it is:
        // producing a document is not a licence to remove one nobody was asked about.
        var prefix = ExpeditionReportNaming.GeneratedPrefix(id);
        var superseded = await db.Attachments
            .Where(a => a.EntityType == AttachedEntityType.Expedition
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

        return TypedResults.Ok(new ExpeditionReportSavedDto(
            outcome.DocumentId ?? Guid.Empty, outcome.Content.File.Id, built.FileName!));
    }

    /// <summary>The bytes of a write-up, or the refusal the camp's own read would have given.</summary>
    private readonly record struct BuiltReport(byte[]? Bytes, string? FileName, ProblemHttpResult? Problem);

    /// <summary>The field titles of a trip that has no purpose, and so no form that named any.</summary>
    private static readonly Dictionary<TripSectionKey, IReadOnlyDictionary<string, string>> NoSectionTitles = [];

    /// <param name="ctx">Who is asking. Decides whether there is a document at all, and nothing else.</param>
    /// <param name="reading">
    /// Whose reading the document states. The caller's own for a download; the reading any account
    /// has for a copy filed where a whole audience reaches it. Every question of who may see what —
    /// which trips are in it, which caves are named, who is listed, which photographs go in — is put
    /// to this one context and to nothing else.
    /// </param>
    /// <param name="formatRefusal">
    /// What is already known to be refused about the format the caller asked for, or null on a
    /// route that takes none. Asked only once the camp has been found readable and its layout
    /// found, so nobody learns from the format what the plain download would not tell them, and
    /// before anything is gathered, so a request that cannot be answered costs nothing.
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
        Func<ProblemHttpResult?>? formatRefusal,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var camp = await db.Expeditions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

        // The same refusal the page gets, letter for letter: a file that existed where a page said
        // nothing would be the answer the page declined to give.
        if (ctx is null || reading is null || user is null || camp is null
            || !(await access.DecideAsync(ctx, AccessAction.Read, camp, ct)).Allowed)
        {
            return new BuiltReport(null, null, ApiProblems.NotFound(ExpeditionEndpoints.NotFoundCode));
        }

        // A layout that was named and is not there — or is there and writes up trips rather than
        // camps — is a refusal rather than a quiet fall back to another one.
        var body = await ReportTemplateReads.BodyForAsync(
            db, templateId, ReportTemplateKind.Expedition, tripTypeId: null, ct);
        if (body is null)
        {
            return new BuiltReport(null, null, ApiProblems.NotFound(ReportTemplateReads.NotFoundCode));
        }

        if (formatRefusal?.Invoke() is { } unwanted)
        {
            return new BuiltReport(null, null, unwanted);
        }

        // Read here rather than trusted: a layout is checked when it is stored, and one that has
        // since become unreadable falls back to the shipped layout so a broken row cannot stop a
        // club producing its write-ups.
        var read = ReportTemplateFormat.Parse(body, ReportTemplateKind.Expedition);
        var parts = read.Ok
            ? read.Parts
            : ReportTemplateFormat.Parse(
                ReportTemplateFormat.ExpeditionDefault, ReportTemplateKind.Expedition).Parts;

        var trips = await TripsAsync(id, db, access, reading, protection, ct);
        var content = new ExpeditionReportContent(
            ExpeditionEndpoints.Map(camp),
            camp.CavingGroupId is { } groupId
                ? await db.CavingGroups.AsNoTracking()
                    .Where(x => x.Id == groupId).Select(x => x.Name).FirstOrDefaultAsync(ct)
                : null,
            trips.Trips,
            trips.People,
            await RosterAsync(id, db, reading, user, ct),
            await RosterPeopleAsync(id, db, reading, ct),
            await PlatesAsync(id, db, reading, thumbnails, ct),
            trips.WatchMinutes);

        var fileName = $"{ExpeditionReportNaming.GeneratedPrefix(id)}{DateTime.UtcNow:yyyyMMdd}.{writer.Extension}";
        return new BuiltReport(writer.Write(ExpeditionReportDocument.Blocks(content, parts)), fileName, null);
    }

    /// <summary>
    /// The member trips, through the same visibility walk the camp's own listing and map apply.
    /// </summary>
    /// <remarks>
    /// Narrowed in the statement rather than after it, for the reason every other listing here is:
    /// a filter applied to results is a filter somebody later forgets to apply. A trip this reading
    /// may not open contributes nothing at all — not a blank line, not a count of one — so the
    /// document cannot be used to find out that there is a trip.
    /// </remarks>
    private static async Task<ExpeditionReportTrips> TripsAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessService access,
        AccessContext reading,
        FeatureProtection protection,
        CancellationToken ct)
    {
        var memberTripIds = db.ExpeditionTrips.AsNoTracking()
            .Where(m => m.ExpeditionId == id)
            .Select(m => m.TripLogId);

        // Whole rows rather than a handful of columns, because what a trip wrote about itself is
        // decided over the trip — who may change it is a question about the row, not about a
        // projection of it. A trip that has been deleted is not among them: the trip table hides
        // those from every read that does not ask for them by name, and this one does not.
        var readableTrips = db.TripLogs.AsNoTracking()
            .VisibleTo(reading, AccessDomain.TripLogs)
            .Where(x => memberTripIds.Contains(x.Id));

        var rows = await readableTrips
            .ToListAsync(ct);
        if (rows.Count == 0)
        {
            return new ExpeditionReportTrips([], 0, null);
        }

        var tripIds = rows.Select(x => x.Id).ToList();

        // Each trip's own words, as this reading is told them. Asked of the one place that
        // decides it for the trip's own page and its own write-up, and asked with this reading
        // and nothing else — so a copy filed for everybody carries what a trip's own filed
        // write-up carries, whoever pressed the button.
        var told = await TripNarrativeReads.ForAsync(access, reading, rows, ct);

        // What each purpose calls the questions on its form, so an answer is printed under the
        // name the form that collected it gave it.
        var typeIds = rows.Where(x => x.TripTypeId is not null).Select(x => x.TripTypeId!.Value).Distinct().ToList();
        var sectionTitles = typeIds.Count == 0
            ? []
            : (await db.TripTypes.AsNoTracking().Where(t => typeIds.Contains(t.Id)).ToListAsync(ct))
                .ToDictionary(t => t.Id, TripNarrativeComposition.SectionTitles);

        // People, counted distinctly per trip: a roster row is a person and a role, so somebody who
        // led and surveyed is two rows and one person, and counting rows is wrong in a way that
        // looks right.
        var people = await db.TripLogParticipants.AsNoTracking()
            .Where(p => tripIds.Contains(p.TripLogId))
            .Select(p => new { p.TripLogId, p.CaverId })
            .Distinct()
            .ToListAsync(ct);
        var peopleByTrip = people
            .GroupBy(p => p.TripLogId)
            .ToDictionary(g => g.Key, g => g.Count());

        // How many people the camp comes to across the trips this reading may see — distinct by
        // person, so somebody on nine of the trips is one person. The camp's own roll-up counts it
        // the same way, and a document stating a different number for the same reading would read
        // as a fault in whoever produced it.
        var distinctPeople = people.Select(p => p.CaverId).Distinct().Count();

        // Which caves each trip is about, then which of those this reading may open, then which of
        // those it may be told the position of. Both gates, in that order, exactly as every other
        // surface over these links asks them. Readability is the first: naming a cave is a read of
        // the cave. Location protection is the second and separate one — a trip carries its own
        // exact geometry, so "this trip reached that cave" places a protected cave by proximity,
        // which is why the trip's own page, the camp's map and the leads board all withhold the
        // pairing from a reader without exact view. A write-up that named it would state on paper
        // what all three of them refuse on screen.
        var pairs = await TripRoleLinks.PairsForAsync(db, tripIds, FeatureKind.Cave, ct);
        var caveIds = pairs.Select(p => p.FeatureId).Distinct().ToList();
        var readable = caveIds.Count == 0
            ? []
            : await db.Features.AsNoTracking()
                .VisibleTo(reading, db.Features, db.FeatureSetMembers)
                .Where(f => caveIds.Contains(f.Id))
                .ToDictionaryAsync(f => f.Id, f => f.Name ?? string.Empty, ct);
        var redacted = await protection.RedactedLinkTargetIdsAsync(reading, [.. readable.Keys], ct);
        var caveNames = readable
            .Where(x => !redacted.Contains(x.Key))
            .ToDictionary(x => x.Key, x => x.Value);

        var trips = new List<ExpeditionReportTrip>(
            rows.Select(row => new ExpeditionReportTrip(
                row.Id,
                row.Title,
                row.TripDate,
                row.TripDateEnd,
                row.EntryTime,
                row.ExitTime,
                row.DepthReachedM,
                row.LengthSurveyedM,
                row.SurveyStations,
                row.RopeMetres,
                peopleByTrip.GetValueOrDefault(row.Id),
                [
                    .. pairs.Where(p => p.TripId == row.Id)
                        .Select(p => caveNames.GetValueOrDefault(p.FeatureId))
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Select(name => name!)
                        .Distinct(StringComparer.Ordinal),
                ],
                told[row.Id],
                row.TripTypeId is { } typeId && sectionTitles.TryGetValue(typeId, out var titles)
                    ? titles
                    : NoSectionTitles)));

        // The tracking logs of the same trips, through the same statement that selected them: the
        // narrowing to what this reading may open is inside the query that counts, not applied to
        // a list on the way in. Null rather than zero where no log has an entry an exit followed.
        var watch = await TripStatisticsQuery.WatchAsync(db, readableTrips.Select(x => x.Id), null, ct);

        return new ExpeditionReportTrips(
            trips, distinctPeople, watch.TimedPersonTrips == 0 ? null : watch.UndergroundMinutes);
    }

    /// <summary>
    /// The recorded stays, under the roster's own two rights.
    /// </summary>
    /// <remarks>
    /// The camp's roster answers to the right to read the camp <em>and</em> the right to read
    /// people, because a stay is a fortnight where a trip is an afternoon. A reading that does not
    /// hold the second gets no roster in the document — empty, exactly as if nobody had been
    /// recorded, so a write-up cannot become a way of finding out that there is one.
    /// </remarks>
    private static async Task<List<ExpeditionReportStay>> RosterAsync(
        Guid id, SilexGisDbContext db, AccessContext reading, UserContext user, CancellationToken ct)
    {
        if (!AccessEvaluator.Decide(reading, AccessDomain.Cavers, AccessAction.Read, null).Allowed)
        {
            return [];
        }

        var rows = await db.ExpeditionRoster.AsNoTracking()
            .Where(x => x.ExpeditionId == id)
            .OrderBy(x => x.FromDate).ThenBy(x => x.Id)
            .ToListAsync(ct);
        if (rows.Count == 0)
        {
            return [];
        }

        // Resolved rather than joined: what a person may be shown as is a rule with one home, and
        // an account's own label wins there so nobody appears twice under two names.
        var labels = await CaverDirectory.ResolveLabelsAsync(db, user, rows.Select(x => x.CaverId), ct);
        var roleIds = rows.Select(x => x.RoleId).Distinct().ToList();
        var roleNames = await db.ExpeditionRosterRoles.AsNoTracking()
            .Where(r => roleIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Name, ct);

        return
        [
            .. rows.Select(row => new ExpeditionReportStay(
                row.CaverId,
                labels.GetValueOrDefault(row.CaverId) ?? string.Empty,
                roleNames.GetValueOrDefault(row.RoleId),
                row.FromDate,
                row.ToDate,
                row.Note)),
        ];
    }

    /// <summary>How many people the roster comes to — distinct by person, never a count of rows.</summary>
    private static async Task<int> RosterPeopleAsync(
        Guid id, SilexGisDbContext db, AccessContext reading, CancellationToken ct) =>
        AccessEvaluator.Decide(reading, AccessDomain.Cavers, AccessAction.Read, null).Allowed
            ? await db.ExpeditionRoster.AsNoTracking()
                .Where(x => x.ExpeditionId == id)
                .Select(x => x.CaverId)
                .Distinct()
                .CountAsync(ct)
            : 0;

    /// <summary>
    /// The pictures, obtained the way the galleries of the camp's trips obtain them.
    /// </summary>
    /// <remarks>
    /// Through the photograph read rule, narrowed to the member trips as a query rather than a
    /// list, so the visibility walk that chose those trips stays inside one statement. What goes in
    /// is a rendering drawn by this application with every metadata profile removed, never the
    /// upload: an upload of a photograph taken at a cave carries the position it was taken at. For
    /// the same reason nothing here writes a picture's position into the document.
    /// </remarks>
    private static async Task<List<ExpeditionReportPlate>> PlatesAsync(
        Guid id,
        SilexGisDbContext db,
        AccessContext reading,
        ThumbnailService thumbnails,
        CancellationToken ct)
    {
        var memberTripIds = db.TripLogs.AsNoTracking()
            .VisibleTo(reading, AccessDomain.TripLogs)
            .Where(t => db.ExpeditionTrips.Any(m => m.ExpeditionId == id && m.TripLogId == t.Id))
            .Select(t => t.Id);

        // The trips are handed to the read rule rather than applied to its result. Reach through an
        // attachment cannot be composed into a query — it resolves a set of documents and hands the
        // answer back as a parameter — so asked over every picture in the installation it costs the
        // same whether a camp gathered two trips or forty. Asked over the pictures on those trips,
        // it costs what the camp is worth.
        var photographs = await PhotographReads.VisiblePhotographsOnTripsAsync(
            db, reading, memberTripIds, ct);

        var documents = await photographs
            .OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.Id)
            .Take(MaxPlates)
            .ToListAsync(ct);
        var rows = await PhotographReads.RowsAsync(db, documents, ct);

        var plates = new List<ExpeditionReportPlate>(rows.Count);
        foreach (var row in rows)
        {
            var rendering = await thumbnails.GetOrCreateAsync(
                row.File.Id, row.File.StoragePath, PlateSize, ct, row.File.OrientationQuarterTurns);
            var caption = new[]
            {
                string.IsNullOrWhiteSpace(row.Details?.Caption) ? row.Document.Title : row.Details!.Caption,
                row.PhotographerLabel ?? row.Details?.PhotographerName,
            }.Where(x => !string.IsNullOrWhiteSpace(x));

            plates.Add(new ExpeditionReportPlate(
                await File.ReadAllBytesAsync(rendering, ct), string.Join(" — ", caption)));
        }

        return plates;
    }
}
