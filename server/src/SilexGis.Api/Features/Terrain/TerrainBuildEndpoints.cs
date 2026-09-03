// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Terrain;

namespace SilexGis.Api.Features.Terrain;

/// <summary>One raster a build was made from, and the credit the data it holds requires.</summary>
public sealed record TerrainBuildSourceDto(
    long Id,
    TerrainBuildSourceKind Kind,
    string Reference,
    string Attribution,
    string? Licence);

/// <summary>
/// A build as it appears in the list: what it covers, how far it got, what it costs on disk, and
/// whether it is the terrain the scene draws.
/// </summary>
/// <param name="Extent">The ground the build covers, as drawn (WGS84).</param>
/// <param name="SurveyHeightOffsetM">
/// Metres to add to a surveyed altitude so it sits on the ground this build draws. Served rather
/// than left to the caller to work out, because the rule that decides it has exactly one home and
/// a second copy of it in a browser would be a forty-metre error nothing on screen could explain.
/// </param>
public sealed record TerrainBuildDto(
    Guid Id,
    GeoJsonGeometry Extent,
    int RequestedMaxDepth,
    TerrainBuildStatus Status,
    TerrainBuildPhase Phase,
    int Progress,
    string? Message,
    string? ErrorCode,
    long? SizeBytes,
    string? PyramidVersion,
    TerrainHeightDatum HeightDatum,
    double GeoidHeightM,
    double SurveyHeightOffsetM,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// One build in full: everything the list carries, plus the rasters it was made from and the tail
/// of what the tools said.
/// </summary>
/// <remarks>
/// The sources and the log tail are here and not in the list because both are unbounded per row —
/// a national lidar build can name hundreds of tiles, and a page of fifty builds would carry
/// megabytes of tool output nobody asked for.
/// </remarks>
public sealed record TerrainBuildDetailDto(
    TerrainBuildDto Build,
    string? LogTail,
    IReadOnlyList<TerrainBuildSourceDto> Sources);

/// <summary>
/// One thing a build is to be made from, other than the coverage the application obtains itself.
/// </summary>
/// <remarks>
/// The credit is given here, by whoever declares the source, because nothing on this server can
/// work out whose data a raster holds. It is recorded rather than checked: an installation may
/// build from anything it can obtain, and what the licence requires is stated so that the result
/// carries it. What is <i>not</i> acceptable is a pyramid stamped with somebody else's credit,
/// which is what a single installation-wide attribution would produce the moment a build mixed two
/// sources — and a false licence statement, displayed to everyone who looks at the scene, is worse
/// than none.
/// </remarks>
/// <param name="Kind">
/// Whether the reference names a raster sent through the browser or a directory on the server.
/// </param>
/// <param name="Reference">
/// The reference an upload answered with, or the directory's path on the server.
/// </param>
public sealed record TerrainBuildSourceRequest(
    TerrainBuildSourceKind Kind,
    string Reference,
    string Attribution,
    string? Licence);

/// <summary>
/// Asking for terrain over a rectangle.
/// </summary>
/// <remarks>
/// Four edges rather than a drawn shape: the area is chosen by dragging a box on a map, a box is
/// what the elevation datasets are addressed by, and a free-form polygon would have to be reduced
/// to its bounding box by the first thing that touched it anyway.
/// </remarks>
/// <param name="MaxDepth">
/// The deepest pyramid level to bake, always stated rather than inferred. Left to the mesher it is
/// derived from the finest raster in the input, so a small patch of half-metre lidar pushes the
/// whole bake several levels deeper and multiplies the tile count — and cost tracks tile count.
/// </param>
/// <param name="HeightDatum">
/// What the heights in the result are measured from. Defaults to heights above the geoid, which is
/// what elevation models usually publish; declaring it wrong moves every cave about forty metres up
/// or down its hillside with nothing on screen to say so.
/// </param>
/// <param name="FetchCoverage">
/// Whether to obtain the open dataset's coverage of the rectangle. Left unsaid it is done, which is
/// what asking for terrain over an area normally means; it is turned off by a build made entirely
/// from finer data somebody already holds.
/// </param>
/// <param name="Sources">
/// Rasters this installation already has — uploaded, or in a directory the operator listed as
/// readable — to build from as well as, or instead of, the obtained coverage.
/// </param>
public sealed record TerrainBuildSubmitRequest(
    double West,
    double South,
    double East,
    double North,
    int MaxDepth,
    TerrainHeightDatum? HeightDatum,
    double? GeoidHeightM,
    bool? FetchCoverage = null,
    IReadOnlyList<TerrainBuildSourceRequest>? Sources = null);

/// <summary>What an upload of one raster answers with.</summary>
/// <param name="Reference">What a build quotes to say it is made from this raster.</param>
public sealed record TerrainRasterUploadDto(string Reference, long SizeBytes);

/// <summary>
/// The directories on the server this installation may read rasters from. Empty means the operator
/// has not opted into reading the server's own disk, which is the default.
/// </summary>
public sealed record TerrainSourceDirectoriesDto(IReadOnlyList<string> Roots);

/// <summary>
/// The checks that can be made without knowing anything about terrain.
/// </summary>
/// <remarks>
/// Deliberately thin. Whether the rectangle is a rectangle, whether it is too large and whether the
/// depth can be built are decisions about terrain, they have one home in the domain, and each
/// answers with a stable code a screen can translate — which this filter's generic
/// "validation failed" could not carry.
/// </remarks>
public sealed class TerrainBuildSubmitRequestValidator : AbstractValidator<TerrainBuildSubmitRequest>
{
    public TerrainBuildSubmitRequestValidator()
    {
        RuleFor(x => x.HeightDatum).IsInEnum().When(x => x.HeightDatum.HasValue);
        RuleFor(x => x.GeoidHeightM)
            .InclusiveBetween(-200d, 200d)
            .When(x => x.GeoidHeightM.HasValue)
            .WithMessage("The geoid height must be between -200 and 200 metres.");

        RuleFor(x => x.Sources)
            .Must(s => s is null || s.Count <= 100)
            .WithMessage("A build may name at most 100 sources.");
        RuleForEach(x => x.Sources).ChildRules(source =>
        {
            source.RuleFor(x => x.Kind).IsInEnum();

            // The obtained coverage is asked for by name and not declared as a source: its credit
            // and its licence belong to the dataset rather than to whoever pressed the button, and
            // accepting them from a request would be accepting a credit somebody made up.
            source.RuleFor(x => x.Kind)
                .NotEqual(TerrainBuildSourceKind.Fetched)
                .WithMessage("Coverage this application obtains is asked for by fetchCoverage, not declared as a source.");
            source.RuleFor(x => x.Reference).NotEmpty().MaximumLength(2000);
            source.RuleFor(x => x.Attribution).NotEmpty().MaximumLength(500);
            source.RuleFor(x => x.Licence).MaximumLength(200);
        });
    }
}

/// <summary>
/// Reading the record of terrain builds. Installation-level throughout: a build has no owner, no
/// caving group and no audience, so the right to see one is held over the whole terrain domain
/// rather than derived from any single row, and the listing is unfiltered once that right is
/// established because there is no per-row audience to filter to.
/// </summary>
public static class TerrainBuildEndpoints
{
    /// <summary>The build asked for does not exist.</summary>
    public const string NotFoundCode = "terrain_build.not_found";

    /// <summary>The caller is signed in but holds nothing over the terrain domain.</summary>
    public const string ForbiddenCode = "access.forbidden";

    /// <summary>The same area is already being built and has not finished.</summary>
    public const string AlreadyBuildingCode = "terrain_build.already_building";

    /// <summary>The build named nothing to build from.</summary>
    public const string NoSourcesCode = "terrain_build.no_sources";

    /// <summary>A declared source names nothing this installation holds.</summary>
    public const string SourceInvalidCode = "terrain_build.source_invalid";

    /// <summary>
    /// A named directory cannot be read: it is not there, or it is not one this installation may
    /// read. Deliberately one code for both.
    /// </summary>
    /// <remarks>
    /// Two codes here would answer a question nobody is entitled to ask. "Not allowed" and "not
    /// there" told apart turn this route into a way of testing whether any given path on the
    /// server's disk exists, one request at a time, without ever reading a byte of it — so the two
    /// are answered identically and the sentence names both possibilities.
    /// </remarks>
    public const string DirectoryUnavailableCode = "terrain_build.directory_unavailable";

    /// <summary>The file sent is not one the raster tools here will open.</summary>
    public const string RasterUnsupportedCode = "terrain_build.raster_unsupported";

    /// <summary>The file sent is empty, or larger than one raster may be.</summary>
    public const string RasterSizeInvalidCode = "terrain_build.raster_size_invalid";

    /// <summary>
    /// The file sent is an elevation format that states its position nowhere but in its name, under
    /// a name that does not state one — or does, and the bytes are not the square it claims.
    /// </summary>
    public const string RasterUnplaceableCode = "terrain_build.raster_unplaceable";

    /// <summary>
    /// The build has produced no pyramid that anything could draw, so it cannot become the terrain.
    /// </summary>
    /// <remarks>
    /// Two things have to be true and neither implies the other: the build must carry the version
    /// stamped on its manifest when its tiles were read back and found whole — which is the only
    /// mark saying anything was ever checked — and the pyramid must actually be at the address it
    /// would be served from. A build whose status says it succeeded and whose bytes somebody has
    /// since removed would otherwise become the terrain and draw nothing, with no error anywhere.
    /// </remarks>
    public const string NotPublishedCode = "terrain_build.not_published";

    /// <summary>The build is the terrain the scene draws, and so cannot be deleted.</summary>
    /// <remarks>
    /// A refusal rather than a quiet deactivation. Removing the ground everyone is looking at is a
    /// separate decision from removing a build nobody is using, and it should be taken deliberately
    /// and be visible in the trail as two acts.
    /// </remarks>
    public const string ActiveCode = "terrain_build.active";

    /// <summary>The build is waiting to run or running, so its files are not anybody's to remove.</summary>
    public const string RunningCode = "terrain_build.running";

    /// <summary>
    /// The largest single raster that may be sent through the browser.
    /// </summary>
    /// <remarks>
    /// The same ceiling the scanned-map upload uses, and for the same reason: elevation rasters are
    /// far larger than documents, and a request body limit meant for a form would refuse them with
    /// nothing in the answer to say what went wrong. It is deliberately not the whole story — a
    /// national lidar tile set is tens of gigabytes and belongs in a directory the operator names,
    /// which is why that way in exists at all.
    /// </remarks>
    private const long MaxRasterUploadBytes = 512L * 1024 * 1024;

    public static RouteGroupBuilder MapTerrainBuildEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/terrain/builds", ListAsync)
            .WithTags("Terrain")
            .WithSummary("Terrain builds, newest first, with their status and size; requires Read on the Terrain domain.");
        api.MapGet("/terrain/builds/{id:guid}", GetAsync)
            .WithTags("Terrain")
            .WithSummary("One terrain build with its sources and the tail of its log; requires Read on the Terrain domain.");
        api.MapPost("/terrain/builds", SubmitAsync)
            .WithValidation<TerrainBuildSubmitRequest>()
            .WithTags("Terrain")
            .WithSummary("Starts a terrain build over a rectangle; requires Execute on the Terrain domain.");
        api.MapPost("/terrain/rasters", UploadRasterAsync)
            .DisableAntiforgery() // a bearer-token API; there is no cookie form to forge
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(MaxRasterUploadBytes))
            .WithTags("Terrain")
            .WithSummary("Sends one raster for a build to be made from; requires Execute on the Terrain domain.");
        api.MapGet("/terrain/source-directories", SourceDirectoriesAsync)
            .WithTags("Terrain")
            .WithSummary("Directories on the server this installation may read rasters from; requires Execute on the Terrain domain.");
        api.MapPost("/terrain/builds/{id:guid}/active", ActivateAsync)
            .WithTags("Terrain")
            .WithSummary("Makes this build the terrain the 3D scene draws; requires Execute on the Terrain domain.");
        api.MapDelete("/terrain/builds/{id:guid}/active", DeactivateAsync)
            .WithTags("Terrain")
            .WithSummary("Stops drawing this build's terrain, leaving the scene on bare ground; requires Execute on the Terrain domain.");
        api.MapDelete("/terrain/builds/{id:guid}", DeleteAsync)
            .WithTags("Terrain")
            .WithSummary("Removes a build and everything it left on disk; requires Delete on the Terrain domain.");
        return api;
    }

    /// <summary>
    /// Accepts a rectangle and starts a build over it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Answers with the <b>build</b> and not with the queue row that will run it. The build is the
    /// resource: it carries the area, the sources, the phase, the progress and the size, and it
    /// outlives the job — queue rows are machinery and their own read surface deliberately shows
    /// nothing of a payload. Every other enqueue in this application that has a row of its own
    /// answers with that row for the same reason.
    /// </para>
    /// <para>
    /// The row and the queue entry are written in one save, so there can be no build nothing will
    /// ever run and no job pointing at a build that does not exist.
    /// </para>
    /// </remarks>
    private static async Task<Results<Created<TerrainBuildDto>, UnauthorizedHttpResult, ProblemHttpResult>> SubmitAsync(
        TerrainBuildSubmitRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        ServerDirectorySource serverDirectories,
        TerrainUploads uploads,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // Execute rather than Create: a build is not a piece of content somebody authors, it is
        // hours of the server's work that produces the ground everyone then sees.
        if (!AccessEvaluator.Decide(ctx, AccessDomain.Terrain, AccessAction.Execute, null).Allowed)
        {
            return ApiProblems.Forbidden(ForbiddenCode);
        }

        if (TerrainBuildRequestRules.Refuse(
                request.West, request.South, request.East, request.North, request.MaxDepth) is { } refusal)
        {
            return ApiProblems.BadRequest(refusal.Code, refusal.Detail);
        }

        var extent = TerrainBuildRequestRules.Rectangle(
            request.West, request.South, request.East, request.North);

        // Every source is resolved before anything is written, so a request naming a directory that
        // cannot be read is refused outright instead of becoming a queued build that fails minutes
        // later with nobody watching. The same paths are resolved and judged again when the build
        // actually runs — the disk can change in between, and the check that counts is the one
        // nearest the read.
        var fetchCoverage = request.FetchCoverage ?? true;
        var declared = new List<TerrainBuildSource>();
        foreach (var source in request.Sources ?? [])
        {
            string reference;
            if (source.Kind == TerrainBuildSourceKind.ServerDirectory)
            {
                // Naming a location for the server to read is a second question, and a harder one
                // than "may this account start a build": there is nothing to scope a right to, and
                // the answer is about the machine rather than about any content on it. It is asked
                // of the same predicate the document import asks, so the two cannot drift apart —
                // and it is asked again, of rights rebuilt at that moment, when the build runs.
                if (!ServerImportPaths.MayReadServerDisk(ctx))
                {
                    return ApiProblems.Forbidden(ForbiddenCode);
                }

                var (resolved, code) = serverDirectories.Resolve(source.Reference);
                if (resolved is null)
                {
                    return RefuseDirectory(code);
                }

                reference = resolved;
            }
            else if (uploads.PathOf(source.Reference) is not null)
            {
                reference = source.Reference;
            }
            else
            {
                return ApiProblems.BadRequest(
                    SourceInvalidCode, "That raster is not one this installation is holding.");
            }

            declared.Add(new TerrainBuildSource
            {
                Kind = source.Kind,

                // The resolved path rather than what was typed: the record should say which
                // directory was actually read, not which one somebody meant.
                Reference = reference,
                Attribution = source.Attribution,
                Licence = source.Licence,
            });
        }

        if (!fetchCoverage && declared.Count == 0)
        {
            return ApiProblems.BadRequest(
                NoSourcesCode,
                "A build has to be made from something: either obtain the coverage of the "
                + "rectangle, or name rasters this installation already holds.");
        }

        // The check below and the write that follows it are one decision, so they are made inside
        // one transaction holding the submission lock. Pressing the button twice sends two requests
        // at once, and that is precisely the pair a read-then-write lets both through.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await TerrainBuildSql.TakeSubmissionLockAsync(db, ct);

        // One rectangle at a time. Nothing here reports back while a build runs except the build's
        // own row, so an administrator who has waited a minute without seeing much has no way of
        // telling a slow start from a lost request — and the obvious response, pressing the button
        // again, would put five copies of the same hours-long work in front of everything else.
        var alreadyBuilding = await db.TerrainBuilds.AnyAsync(
            b => (b.Status == TerrainBuildStatus.Queued || b.Status == TerrainBuildStatus.Running)
                && b.Extent.EqualsTopologically(extent),
            ct);
        if (alreadyBuilding)
        {
            return ApiProblems.Conflict(
                AlreadyBuildingCode, "That area is already being built. Wait for it to finish.");
        }

        var build = new TerrainBuild
        {
            Extent = extent,
            RequestedMaxDepth = request.MaxDepth,
            HeightDatum = request.HeightDatum ?? TerrainHeightDatum.Orthometric,
            GeoidHeightM = request.GeoidHeightM ?? 0d,
        };

        db.TerrainBuilds.Add(build);

        // The sources are the build's declaration of what it is made from, written when it is asked
        // for; the step that obtains them makes each one real on disk. The obtained coverage is one
        // source and not one per cell — which cells a rectangle happens to need is arithmetic, and
        // the credit is the dataset's rather than any single tile's.
        if (fetchCoverage)
        {
            db.TerrainBuildSources.Add(new TerrainBuildSource
            {
                TerrainBuildId = build.Id,
                Kind = TerrainBuildSourceKind.Fetched,
                Reference = CopernicusCoverage.Name,
                Attribution = CopernicusCoverage.Attribution,
                Licence = CopernicusCoverage.Licence,
            });
        }

        foreach (var source in declared)
        {
            source.TerrainBuildId = build.Id;
            db.TerrainBuildSources.Add(source);
        }

        db.ProcessingJobs.Add(new ProcessingJob
        {
            Kind = ProcessingJobKinds.TerrainBuild,
            Payload = JsonSerializer.Serialize(
                new TerrainBuildPayload(build.Id), JsonSerializerOptions.Web),

            // The build itself records no requester — who started one is answered from the audit
            // trail — but the queue row does, so whoever asked is told when it ends.
            RequestedBy = ctx.UserId,
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return TypedResults.Created($"/api/v1/terrain/builds/{build.Id}", ToDto(build));
    }

    private static async Task<Results<Ok<PagedResult<TerrainBuildDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListAsync(
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        int? page,
        int? pageSize,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.Terrain, AccessAction.Read, null).Allowed)
        {
            return ApiProblems.Forbidden(ForbiddenCode);
        }

        var (p, size) = Paging.Normalize(page, pageSize);
        var query = db.TerrainBuilds.AsNoTracking();
        var total = await query.CountAsync(ct);

        // Newest first, then by key. The timestamp alone is not an order: two builds submitted in
        // the same tick would sort differently between one page and the next, so a row could be
        // read twice or skipped entirely. The key breaks every tie, and because it is time-ordered
        // it breaks them in the direction the timestamp was already going.
        var rows = await query
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((p - 1) * size).Take(size)
            .ToListAsync(ct);

        return TypedResults.Ok(new PagedResult<TerrainBuildDto>([.. rows.Select(ToDto)], p, size, total));
    }

    private static async Task<Results<Ok<TerrainBuildDetailDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // The refusal is 403 and not a 404 standing in for one: unlike a job, a build belongs to
        // nobody, so there is no other person's row whose existence could be disclosed by saying
        // plainly that the caller lacks the right.
        if (!AccessEvaluator.Decide(ctx, AccessDomain.Terrain, AccessAction.Read, null).Allowed)
        {
            return ApiProblems.Forbidden(ForbiddenCode);
        }

        var build = await db.TerrainBuilds.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (build is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        var sources = await db.TerrainBuildSources.AsNoTracking()
            .Where(s => s.TerrainBuildId == id)
            .OrderBy(s => s.Id)
            .ToListAsync(ct);

        return TypedResults.Ok(new TerrainBuildDetailDto(
            ToDto(build),
            build.LogTail,
            [.. sources.Select(s => new TerrainBuildSourceDto(
                s.Id, s.Kind, s.Reference, s.Attribution, s.Licence))]));
    }

    /// <summary>
    /// Takes one raster for a build to be made from and answers with the reference that names it.
    /// </summary>
    /// <remarks>
    /// It is put where builds do their work rather than in the document store, because it is not a
    /// document: nobody reads it, it belongs to no cabinet and to no audience, and the next thing to
    /// touch it is a raster tool that wants a path on disk. The reference answered with is a name
    /// this server made up — nothing a caller sends is ever part of a path here.
    /// </remarks>
    private static async Task<Results<Created<TerrainRasterUploadDto>, UnauthorizedHttpResult, ProblemHttpResult>> UploadRasterAsync(
        IFormFile file,
        IAccessContextAccessor accessAccessor,
        TerrainUploads uploads,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.Terrain, AccessAction.Execute, null).Allowed)
        {
            return ApiProblems.Forbidden(ForbiddenCode);
        }

        if (!TerrainRasterFiles.IsRaster(file.FileName))
        {
            return ApiProblems.BadRequest(
                RasterUnsupportedCode,
                $"Elevation is read from {string.Join(", ", TerrainRasterFiles.Accepted)}.");
        }

        if (file.Length <= 0 || file.Length > MaxRasterUploadBytes)
        {
            return ApiProblems.BadRequest(
                RasterSizeInvalidCode,
                $"A raster sent this way must be between 1 byte and {MaxRasterUploadBytes / (1024 * 1024)} MB. "
                + "Larger data belongs in a directory on the server the operator has listed.");
        }

        // A tile says where it is only by what it is called, and the store issues names of its own
        // — so a name that does not describe a square is refused here, while somebody is still
        // looking at the file they chose. Left to be found later it would surface minutes into a
        // build, as a file the raster library reports it cannot read at all.
        if (ElevationTileConversion.NeedsConversion(file.FileName)
            && SrtmTileName.Parse(file.FileName) is null)
        {
            return ApiProblems.BadRequest(
                RasterUnplaceableCode,
                "An elevation tile carries no position inside it, only in its name, in the form "
                + "N45E024.hgt for the square whose corner is 45°N 24°E. This file's name does not "
                + "say which square it covers, so nothing can place it on the earth. Rename it, or "
                + "send it in a format that carries its own position.");
        }

        await using var content = file.OpenReadStream();

        TerrainStoredRaster stored;
        try
        {
            stored = await uploads.SaveAsync(content, file.FileName, ct);
        }
        catch (InvalidDataException e)
        {
            return ApiProblems.BadRequest(RasterUnplaceableCode, e.Message);
        }

        return TypedResults.Created(
            $"/api/v1/terrain/rasters/{stored.Reference}",
            new TerrainRasterUploadDto(stored.Reference, stored.SizeBytes));
    }

    /// <summary>
    /// The directories on this server a build may read rasters from, so a page can offer them
    /// rather than asking somebody to type a path and guess.
    /// </summary>
    private static async Task<Results<Ok<TerrainSourceDirectoriesDto>, UnauthorizedHttpResult, ProblemHttpResult>> SourceDirectoriesAsync(
        IAccessContextAccessor accessAccessor,
        ServerDirectorySource serverDirectories,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // The same bar as naming one of them in a build, and for the same reason: this list is the
        // operator's own directory layout, which is a fact about the machine.
        if (!AccessEvaluator.Decide(ctx, AccessDomain.Terrain, AccessAction.Execute, null).Allowed
            || !ServerImportPaths.MayReadServerDisk(ctx))
        {
            return ApiProblems.Forbidden(ForbiddenCode);
        }

        return TypedResults.Ok(new TerrainSourceDirectoriesDto(serverDirectories.Roots()));
    }

    /// <summary>
    /// Makes this build the terrain the scene draws.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two writes inside one transaction, because at most one build may carry the mark and the
    /// database holds that rule itself. Whatever held it has to let go in a write of its own before
    /// this row can take it: presented with both changes at once the database sees two rows claiming
    /// the mark at the same instant and refuses the pair. One transaction, so an interruption
    /// between the two cannot leave an installation drawing nothing.
    /// </para>
    /// <para>
    /// Serialised by a lock taken first, because this is a read followed by a write and the case it
    /// has to survive — two people choosing terrain in the same moment — is exactly the pair that
    /// slips between them.
    /// </para>
    /// <para>
    /// Tracked and saved rather than updated in place, deliberately: choosing what everyone sees is
    /// one of the few acts on a build a person actually takes, and the trail only records saves it
    /// is given to see.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<TerrainBuildDto>, UnauthorizedHttpResult, ProblemHttpResult>> ActivateAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        TerrainWorkspace workspace,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // The same right that starts a build. Choosing which finished one everybody sees is the
        // other half of the same job, and there is nothing else on a build to hold a right over.
        if (!AccessEvaluator.Decide(ctx, AccessDomain.Terrain, AccessAction.Execute, null).Allowed)
        {
            return ApiProblems.Forbidden(ForbiddenCode);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await TerrainBuildSql.TakeActivationLockAsync(db, ct);

        var build = await db.TerrainBuilds.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (build is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (string.IsNullOrWhiteSpace(build.PyramidVersion) || !workspace.HasPublishedPyramid(build.Id))
        {
            return ApiProblems.Conflict(
                NotPublishedCode,
                "This build has no pyramid to draw. Either its tiles were never read back and "
                + "found whole, or what it produced is no longer where terrain is served from — "
                + "and terrain that is not there is drawn as smooth bare ground with nothing "
                + "anywhere saying so.");
        }

        var held = await db.TerrainBuilds.Where(x => x.IsActive && x.Id != id).ToListAsync(ct);
        foreach (var other in held)
        {
            other.IsActive = false;
        }

        build.IsActive = false;
        await db.SaveChangesAsync(ct);

        build.IsActive = true;
        // Chosen, so a build finishing later leaves it alone. A finished build draws itself only
        // over ground nobody picked.
        build.ActivationWasAutomatic = false;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return TypedResults.Ok(ToDto(build));
    }

    /// <summary>
    /// Stops drawing this build, leaving the scene on bare ground until something else is chosen.
    /// </summary>
    /// <remarks>
    /// One write, and no lock: letting the mark go can only ever end with fewer rows holding it, so
    /// there is no pair of requests whose interleaving produces a state the database would refuse.
    /// A build that is not the current one is answered with itself rather than with a refusal —
    /// what was asked for is already true.
    /// </remarks>
    private static async Task<Results<Ok<TerrainBuildDto>, UnauthorizedHttpResult, ProblemHttpResult>> DeactivateAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.Terrain, AccessAction.Execute, null).Allowed)
        {
            return ApiProblems.Forbidden(ForbiddenCode);
        }

        var build = await db.TerrainBuilds.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (build is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (build.IsActive)
        {
            build.IsActive = false;
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.Ok(ToDto(build));
    }

    /// <summary>
    /// Removes a build and everything it left on disk.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rows go first and the bytes afterwards, which is the order everything here that owns
    /// files uses: once nothing can reach a file, a file that will not delete is litter rather than
    /// a fault, and reporting a failure to whoever pressed delete would be reporting it about work
    /// that has already been done. The rasters the build was made from go with it, by the database's
    /// own rule about rows that belong to a parent.
    /// </para>
    /// <para>
    /// Nothing running is deleted, because its files are being written while this asks for them, and
    /// nothing current is deleted, because that would take the ground out from under everyone
    /// looking at the scene without anybody having said to.
    /// </para>
    /// <para>
    /// That second refusal is a read followed by a write, so it takes the same lock choosing
    /// terrain does and holds it, in one transaction, across the removal. Without it one
    /// administrator's delete can read a build as not being drawn in the moment before another's
    /// activation makes it the one that is, and then take the row and the pyramid out from under
    /// the scene — which is exactly the state the refusal exists to prevent, arrived at with
    /// nothing anywhere recording that a refusal was due. The bytes are swept after the transaction
    /// commits, because a rollback must not find them already gone.
    /// </para>
    /// </remarks>
    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> DeleteAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        TerrainWorkspace workspace,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.Terrain, AccessAction.Delete, null).Allowed)
        {
            return ApiProblems.Forbidden(ForbiddenCode);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await TerrainBuildSql.TakeActivationLockAsync(db, ct);

        var build = await db.TerrainBuilds.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (build is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (build.IsActive)
        {
            return ApiProblems.Conflict(
                ActiveCode,
                "This build is the terrain the scene is drawing. Choose other terrain, or stop "
                + "drawing this one, before removing it.");
        }

        if (build.Status is TerrainBuildStatus.Queued or TerrainBuildStatus.Running)
        {
            return ApiProblems.Conflict(
                RunningCode,
                "This build has not finished. Its files are being written while this is being "
                + "asked, so it can be removed once it has stopped, whichever way it stops.");
        }

        db.TerrainBuilds.Remove(build);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        // Nothing can reach these bytes any more, so the sweep is not the caller's to wait on being
        // cancelled: a request abandoned halfway through would leave gigabytes behind that no row
        // names and nothing will ever come back for.
        workspace.Remove(id);

        return TypedResults.NoContent();
    }

    /// <summary>
    /// A directory that may not be read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An installation that lists no directories at all has not opted into reading its own disk,
    /// which is a fact about the deployment rather than about the path somebody typed — so it is a
    /// conflict rather than a bad request, and a screen can say "ask your operator" instead of
    /// "check what you typed". That answer discloses nothing about any path.
    /// </para>
    /// <para>
    /// Every other reason is answered identically. A path that exists but is not permitted and a
    /// path that is simply not there must not be told apart from outside, or this route becomes a
    /// way of mapping the server's disk one request at a time; the sentence therefore names both
    /// possibilities and the answer does not say which it was.
    /// </para>
    /// </remarks>
    private static ProblemHttpResult RefuseDirectory(string? code) =>
        code == ServerImportPaths.NoRootsCode
            ? ApiProblems.Conflict(code, "This installation reads rasters from no directory on the server.")
            : ApiProblems.BadRequest(
                DirectoryUnavailableCode,
                "That directory cannot be read from: it is not there, or it is not one this "
                + "installation may read.");

    private static TerrainBuildDto ToDto(TerrainBuild build) => new(
        build.Id,
        GeoJsonGeometry.From(build.Extent),
        build.RequestedMaxDepth,
        build.Status,
        build.Phase,
        build.Progress,
        build.Message,
        build.ErrorCode,
        build.SizeBytes,
        build.PyramidVersion,
        build.HeightDatum,
        build.GeoidHeightM,
        build.SurveyHeightOffsetM,
        build.IsActive,
        build.CreatedAt,
        build.StartedAt,
        build.FinishedAt,
        build.UpdatedAt);
}
