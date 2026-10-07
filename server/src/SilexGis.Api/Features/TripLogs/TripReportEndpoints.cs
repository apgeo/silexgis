// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
/// What a download of a write-up may bring with it, as the published contract describes it.
/// </summary>
/// <remarks>
/// Named for the contract and bound by nothing: the route reads its own form. A form bound as a
/// parameter makes the framework expect one, and a request that brings no body at all — which
/// here is a perfectly good request for the plain document — is then answered as a fault.
/// </remarks>
public sealed class TripReportDownloadForm
{
    /// <summary>
    /// A PNG or JPEG of the map the caller's own browser drew. Leave the part out to send none.
    /// </summary>
    /// <remarks>
    /// A property rather than a constructor parameter so that the contract publishes it as
    /// something a request may leave out, which is what it is.
    /// </remarks>
    public IFormFile? Map { get; init; }
}

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
/// <para>
/// A download may bring one thing with it that was not built here: a picture of a map, drawn by
/// the caller's own browser out of what the trip's page had already been given. It goes into
/// that caller's download and into nothing else. The server draws no map of its own, because a
/// map drawn here would have to ask where things are, and that would be a second reading of
/// the trip beside the one whose disclosure is tested. And the picture never goes into a copy
/// filed against the trip: it shows what one reader may see — exact positions included — and a
/// filed copy is opened by every later reader of the trip. In a download it goes back to the
/// person it came from.
/// </para>
/// </remarks>
internal static class TripReportEndpoints
{
    /// <summary>The name of the multipart part a download's picture of a map arrives in.</summary>
    public const string MapPartName = "map";

    /// <summary>The part named for the picture was sent with nothing in it.</summary>
    public const string MapEmptyCode = "trip_report.map_empty";

    /// <summary>The picture is more bytes than this installation takes.</summary>
    public const string MapTooLargeCode = "trip_report.map_too_large";

    /// <summary>The bytes are neither a PNG nor a JPEG.</summary>
    public const string MapFormatUnsupportedCode = "trip_report.map_format_unsupported";

    /// <summary>The picture is more pixels than this installation takes.</summary>
    public const string MapTooManyPixelsCode = "trip_report.map_too_many_pixels";

    /// <summary>The bytes begin like a picture and cannot be read as one.</summary>
    public const string MapUnreadableCode = "trip_report.map_unreadable";

    /// <summary>A picture was sent with a request to file the write-up, which takes none.</summary>
    public const string MapNotFiledCode = "trip_report.map_not_filed";

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
            id, templateId, db, access, ctx, ctx, userAccessor, protection, thumbnails, writer, null, ct);
        return built.Problem is { } problem
            ? problem
            : TypedResults.File(built.Bytes!, writer.ContentType, built.FileName!);
    }

    /// <summary>
    /// Maps the download that may bring a picture of a map with it.
    /// </summary>
    /// <remarks>
    /// Mapped here rather than in the trip's own route table because the route's transport limit
    /// is the write-up's own setting: the web server has to let through a request a little larger
    /// than the picture's bound, so that a picture over the bound reaches the check that can say
    /// so. Metadata is fixed when a route is mapped, long before a request exists, which is why
    /// the setting is read from the container here.
    /// </remarks>
    public static void MapDownloadWithMap(RouteGroupBuilder trips)
    {
        var maxRequestBytes = ((IEndpointRouteBuilder)trips).ServiceProvider
            .GetRequiredService<IOptions<ReportOptions>>().Value.MaxMapRequestBytes;

        trips.MapPost("/{id:guid}/report/download", DownloadWithMapAsync)
            // Said rather than inferred, because the form is read by the route and not bound: a
            // body of any other kind is turned away before the route runs, and a request with
            // no body at all is let through to be answered with the plain document.
            .Accepts<TripReportDownloadForm>(isOptional: true, "multipart/form-data")
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(maxRequestBytes))
            .WithSummary(
                "The trip written up as a document, with a picture of a map the caller drew "
                + "placed where the write-up says where the trip went.")
            .WithDescription(
                "Multipart, with one optional part named 'map': a PNG or JPEG the caller's own "
                + "browser drew out of what the trip's page was already given. The picture is "
                + "checked, redrawn here and placed in this one answer; it is stored nowhere. "
                + "Without the part the answer is the plain download's. The layout is chosen "
                + "by the same query parameter the plain download takes.");
    }

    /// <summary>
    /// The write-up as a file the caller keeps, carrying the picture of a map they sent with the
    /// request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same document as the plain download in every word — built from the caller's own
    /// reading, refused in the same way to somebody who may not read the trip — with one thing
    /// added that this application did not draw. So the picture is treated as what it is,
    /// somebody's upload: bounded in bytes and in pixels, accepted only as one of two plain
    /// raster formats judged by its own first bytes, decoded and drawn again before it is
    /// placed, and held for this one answer.
    /// </para>
    /// <para>
    /// What the picture shows is not checked and cannot be. That is why it is captioned with
    /// whose view it is and when, and why it is taken here and nowhere else.
    /// </para>
    /// </remarks>
    public static async Task<Results<FileContentHttpResult, ProblemHttpResult>> DownloadWithMapAsync(
        Guid id,
        Guid? templateId,
        HttpRequest request,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
        ThumbnailService thumbnails,
        IDocumentWriter writer,
        IOptions<ReportOptions> options,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var built = await BuildAsync(
            id, templateId, db, access, ctx, ctx, userAccessor, protection, thumbnails, writer,
            () => ReadMapAsync(request, options.Value, ct),
            ct);
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
    /// <para>
    /// For the same reason a filed copy takes no picture, and a request that brings one is
    /// refused rather than quietly served without it. A picture of a map is drawn by its
    /// sender's browser from what that one person may see, and here it would be handed to
    /// everybody who reads the trip afterwards. The refusal is made on this side because a
    /// rule that only the page keeps is a rule any other caller of this route may skip.
    /// </para>
    /// </remarks>
    public static async Task<Results<Ok<TripReportSavedDto>, ProblemHttpResult>> KeepAsync(
        Guid id,
        Guid? templateId,
        HttpRequest request,
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

        // Asked only of somebody who could have filed the write-up at all, so that everybody
        // else is refused exactly as they were before this route knew what a picture was.
        if (await PictureRefusalAsync(request, ct) is { } pictureRefused)
        {
            return pictureRefused;
        }

        // The reading the filed copy is built from: one that belongs to no account in particular,
        // so nothing in the file is there because of who happened to press the button. The trip
        // itself was read above, as this caller — a filed copy of a trip they may read is not a
        // trip anybody may read.
        var everyReader = await AccessContextResolver.ResolveForAnyAccountAsync(db, ct);
        // No picture, and no way to pass one: this is the copy every reader of the trip opens.
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
        var prefix = TripReportNaming.GeneratedPrefix(id);
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
    /// The picture a download brought, redrawn here; or why it was not taken; or neither, when
    /// the request brought none.
    /// </summary>
    private readonly record struct MapReading(byte[]? Image, ProblemHttpResult? Problem);

    /// <summary>
    /// Reads the picture a download brought and draws it again, or refuses it by name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A request that brings no body, or a form with no part of the right name in it, brought no
    /// picture, and that is not a refusal: it is a request for the plain document.
    /// </para>
    /// <para>
    /// The size in bytes is judged before a byte is read into memory, and the rest is the
    /// reader's: which format the bytes really are, how many pixels they declare, and whether
    /// they decode at all. The file name the part was sent with and the media type it declared
    /// are read by nothing — both are words the sender chose.
    /// </para>
    /// </remarks>
    private static async Task<MapReading> ReadMapAsync(
        HttpRequest request, ReportOptions options, CancellationToken ct)
    {
        if (!BringsAForm(request))
        {
            return new MapReading(null, null);
        }

        IFormFile? map;
        try
        {
            map = (await request.ReadFormAsync(ct)).Files.GetFile(MapPartName);
        }
        catch (Exception unreadable) when (IsABrokenForm(unreadable))
        {
            return new MapReading(null, BrokenForm());
        }

        if (map is null)
        {
            return new MapReading(null, null);
        }

        if (map.Length == 0)
        {
            return Refused(MapEmptyCode, "The picture of the map is empty.");
        }

        if (map.Length > options.MaxMapBytes)
        {
            return Refused(
                MapTooLargeCode,
                $"The picture of the map is larger than the {options.MaxMapBytes} bytes a write-up takes.");
        }

        var bytes = new byte[map.Length];
        await using (var content = map.OpenReadStream())
        {
            await content.ReadExactlyAsync(bytes, ct);
        }

        var picture = SuppliedPicture.Read(bytes, options.MaxMapPixels);
        return picture.Fault switch
        {
            SuppliedPictureFault.None => new MapReading(picture.Image, null),
            SuppliedPictureFault.Empty => Refused(MapEmptyCode, "The picture of the map is empty."),
            SuppliedPictureFault.NotAPicture => Refused(
                MapFormatUnsupportedCode, "A write-up takes the picture of its map as a PNG or a JPEG."),
            SuppliedPictureFault.TooManyPixels => Refused(
                MapTooManyPixelsCode,
                $"The picture of the map is {picture.Width} by {picture.Height} pixels, which is more "
                + $"than the {options.MaxMapPixels} a write-up takes."),
            _ => Refused(MapUnreadableCode, "The picture of the map could not be read."),
        };

        static MapReading Refused(string code, string detail) =>
            new(null, ApiProblems.BadRequest(code, detail));
    }

    /// <summary>
    /// The refusal for a request to file a write-up that brought a picture with it, or null when
    /// it brought none.
    /// </summary>
    /// <remarks>
    /// A picture can arrive two ways — as a part of a form, or as the body itself — and either is
    /// refused. A form is read to find out, because a form that carries no file is not a picture
    /// and refusing it as one would name the wrong fault; any file in it counts, whatever name it
    /// was sent under, since a picture under another name is still a picture somebody tried to
    /// file.
    /// </remarks>
    private static async Task<ProblemHttpResult?> PictureRefusalAsync(
        HttpRequest request, CancellationToken ct)
    {
        if (request.ContentType is { } declared
            && declared.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return NotFiled();
        }

        if (!BringsAForm(request))
        {
            return null;
        }

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(ct);
        }
        catch (Exception unreadable) when (IsABrokenForm(unreadable))
        {
            return BrokenForm();
        }

        return form.Files.Count > 0 || form.ContainsKey(MapPartName) ? NotFiled() : null;

        static ProblemHttpResult NotFiled() => ApiProblems.BadRequest(
            MapNotFiledCode,
            "A write-up filed against the trip takes no picture: it is opened by everybody who may "
            + "read the trip, and a picture shows what one reader may see. Download the write-up "
            + "to have the picture in your own copy.");
    }

    /// <summary>
    /// Whether a request brought a form to read: a body, and a content type that says it is one.
    /// </summary>
    /// <remarks>
    /// Both, because each alone misleads. A request can declare a form and send no body — which
    /// is nothing sent, and reading it as a form would fail on a body that was never there.
    /// </remarks>
    private static bool BringsAForm(HttpRequest request)
    {
        var body = request.HttpContext.Features.Get<IHttpRequestBodyDetectionFeature>();
        return body is not { CanHaveBody: false } && request.HasFormContentType;
    }

    /// <summary>
    /// Whether reading a form failed because the form itself is malformed or broke off.
    /// </summary>
    /// <remarks>
    /// A request the web server turned away — one too large to be read at all — is not one of
    /// these: it is left to the handler that answers those with the status that says what
    /// happened.
    /// </remarks>
    private static bool IsABrokenForm(Exception failure) =>
        failure is InvalidDataException or (IOException and not BadHttpRequestException);

    /// <summary>
    /// The refusal for a form that could not be read: the one the framework gives on the routes
    /// whose forms it binds itself, so the same broken request gets the same answer everywhere.
    /// </summary>
    private static ProblemHttpResult BrokenForm() => ApiProblems.BadRequest(
        BindingProblemHandler.BindingFailedCode, "The request body could not be read.");

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
    /// <param name="readMap">
    /// Reads the picture of a map the request may have brought, or null on a route that takes
    /// none. Called only once the trip has been found readable and the layout found, so a caller
    /// who is refused the document is told that and nothing about what they sent with the
    /// request.
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
        Func<Task<MapReading>>? readMap,
        CancellationToken ct)
    {
        // A picture shows what its sender may see, so it may only ever go into a document that
        // states its sender's own reading. Checked here, where the two meet, rather than left to
        // each caller to remember: the day somebody passes a picture into the copy that is built
        // for every reader of the trip, this stops it instead of a review having to.
        if (readMap is not null && !ReferenceEquals(ctx, reading))
        {
            throw new InvalidOperationException(
                "A picture goes only into the copy built from its sender's own reading.");
        }

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
        var body = await ReportTemplateReads.BodyForAsync(db, templateId, ReportTemplateKind.Trip, trip.TripTypeId, ct);
        if (body is null)
        {
            return new BuiltReport(null, null, ApiProblems.NotFound(ReportTemplateReads.NotFoundCode));
        }

        // Read here rather than trusted: a layout is checked when it is stored, and one that has
        // since become unreadable falls back to the shipped layout so a broken row cannot stop a
        // club producing its write-ups.
        var read = ReportTemplateFormat.Parse(body, ReportTemplateKind.Trip);
        var parts = read.Ok
            ? read.Parts
            : ReportTemplateFormat.Parse(ReportTemplateFormat.Default, ReportTemplateKind.Trip).Parts;

        TripReportMap? map = null;
        if (readMap is not null)
        {
            var sent = await readMap();
            if (sent.Problem is { } refused)
            {
                return new BuiltReport(null, null, refused);
            }

            if (sent.Image is { } picture)
            {
                // Named the way every other surface names a person, which is never by their
                // address: the caption travels with a file that is forwarded.
                var labels = await ProfileDirectory.ResolveLabelsAsync(db, user, [user.UserId], ct);
                map = new TripReportMap(
                    picture,
                    labels.GetValueOrDefault(user.UserId),
                    DateOnly.FromDateTime(DateTime.UtcNow));
            }
        }

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

        // Named, not placed. The identifiers are the list the trip read produced — which already
        // had taken out of it every cave this reading may not open and every cave it may not
        // place — and nothing here resolves a coordinate for any of them.
        //
        // The names are still fetched through the cave read rather than off the link rows. That
        // is not a second reading of the same rule but the only honest way to get a name at all,
        // and asking for it under the reader's own visibility means the two answers cannot come
        // apart: every identifier the trip handed over resolves, and a document that found one
        // that did not would leave the cave out rather than print the identifier in its place.
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
            await PlatesAsync(id, db, reading, thumbnails, ct),
            map);

        var fileName = $"{TripReportNaming.GeneratedPrefix(id)}{DateTime.UtcNow:yyyyMMdd}.{writer.Extension}";
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
