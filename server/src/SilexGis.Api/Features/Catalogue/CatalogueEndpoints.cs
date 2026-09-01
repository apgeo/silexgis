// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Catalogue;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Catalogue;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Catalogue;

/// <summary>
/// Reading the Romanian community cave catalogue at speologie.org, and taking caves from it.
///
/// <para>
/// Two surfaces sit on this slice and they are deliberately different. One is for looking things
/// up — a person wants to know what the national register says about a cave, and follows the link
/// out to read it there. The other is for taking a cave into this registry. Looking something up
/// is the common act and it creates nothing, so it is not behind the import screen.
/// </para>
///
/// <para>
/// <b>Every route here needs the right to create caves, including the ones that only read.</b>
/// That is not the obvious gate and it is the right one: the searches go out over this
/// installation's own API key, against somebody else's small volunteer-run service, and an
/// endpoint any signed-in account could call would make this server a free proxy to it. The right
/// to add caves is also, in practice, exactly the set of people who have a reason to be looking
/// at a catalogue of caves to add.
/// </para>
///
/// <para>
/// The one exception is the status route, which answers a single boolean about this installation
/// and reaches nothing: the screens need to be able to say "your administrator has not set this
/// up" to whoever navigates to them.
/// </para>
/// </summary>
public static class CatalogueEndpoints
{
    /// <summary>The catalogue's own site, linked from the screens.</summary>
    private const string PortalUrl = "https://www.speologie.org";

    /// <summary>Default page of results, when the caller does not say.</summary>
    private const int DefaultPageSize = 25;

    /// <summary>No API key is configured, so this installation cannot reach the catalogue.</summary>
    public const string NotConfiguredCode = SpeologieException.NotConfiguredCode;

    /// <summary>A search that named neither a term nor a county — refused rather than answered with the whole register.</summary>
    public const string SearchTooBroadCode = "speologie.search_too_broad";

    /// <summary>The catalogue has no cave under the identifier asked for.</summary>
    public const string CaveNotFoundCode = "speologie.cave_not_found";

    /// <summary>
    /// A search parameter that is wrong in a way that has nothing to do with breadth — a term
    /// longer than the catalogue accepts, or a county given as a name rather than as the
    /// two-letter code its filter matches on. The same code the validation filter uses
    /// everywhere else, because it means the same thing.
    /// </summary>
    public const string ValidationFailedCode = "validation.failed";

    public static RouteGroupBuilder MapCatalogueEndpoints(this RouteGroupBuilder api)
    {
        var catalogue = api.MapGroup("/catalogue/speologie").WithTags("Catalogue");

        catalogue.MapGet("/status", StatusAsync)
            .WithSummary(
                "Whether this installation has been given an API key for the speologie.org cave "
                + "catalogue, and the limits it works to. Reaches nothing.");

        catalogue.MapGet("/caves", SearchAsync)
            .WithSummary(
                "Searches the speologie.org cave catalogue by name and county, marking the caves "
                + "this installation already holds. Needs a term or a county.");

        catalogue.MapGet("/caves/{id:int}", GetAsync)
            .WithSummary(
                "One cave from the speologie.org catalogue in full, with its description already "
                + "converted to the plain text an import would store.");

        catalogue.MapPost("/import", ImportAsync)
            .WithValidation<SpeologieImportRequest>()
            .WithSummary(
                "Imports the selected caves from the speologie.org catalogue as one revertible "
                + "batch, creating new caves and refreshing ones imported before.");

        return api;
    }

    private static async Task<Results<Ok<SpeologieStatusDto>, UnauthorizedHttpResult>> StatusAsync(
        SpeologieClient client,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(new SpeologieStatusDto(
            client.IsConfigured, client.MaxPageSize, client.MaxSelection, PortalUrl));
    }

    private static async Task<Results<Ok<SpeologieSearchDto>, ProblemHttpResult, UnauthorizedHttpResult>> SearchAsync(
        string? q,
        string? county,
        int? page,
        int? pageSize,
        SpeologieClient client,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        IValidator<SpeologieSearchQueryDto> validator,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!CreateRules.MayCreate(ctx, AccessDomain.Features, requestedCavingGroupId: null))
        {
            return ApiProblems.Forbidden(CreateRules.ForbiddenCode);
        }

        var parameters = new SpeologieSearchQueryDto(q, county, page, pageSize);
        var validation = await validator.ValidateAsync(parameters, ct);
        if (!validation.IsValid)
        {
            var detail = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));

            // Only one of the three ways a search can be refused is about breadth. A county
            // spelled as a name rather than as a code is a precise question asked in the wrong
            // vocabulary, and answering it with a code that says "too broad" sends whoever reads
            // it to look for the thing they did not do.
            var tooBroad = string.IsNullOrWhiteSpace(q) && string.IsNullOrWhiteSpace(county);

            return tooBroad
                ? ApiProblems.BadRequest(SearchTooBroadCode, detail)
                : ApiProblems.BadRequest(ValidationFailedCode, detail);
        }

        var wantedPage = Math.Max(1, page ?? 1);
        var wantedSize = Math.Clamp(pageSize ?? DefaultPageSize, 1, client.MaxPageSize);

        SpeologieSearchPage found;
        try
        {
            found = await client.SearchAsync(
                new SpeologieSearchQuery(q, county, (wantedPage - 1) * wantedSize, wantedSize), ct);
        }
        catch (SpeologieException e)
        {
            return Unreachable(e);
        }

        var annotated = await AnnotateAsync(db, ctx, found.Items, description: null, ct);

        return TypedResults.Ok(new SpeologieSearchDto(
            annotated, wantedPage, wantedSize, found.HasMore, found.Spellings));
    }

    private static async Task<Results<Ok<SpeologieCaveDto>, ProblemHttpResult, UnauthorizedHttpResult>> GetAsync(
        int id,
        SpeologieClient client,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        IOptions<SpeologieOptions> options,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!CreateRules.MayCreate(ctx, AccessDomain.Features, requestedCavingGroupId: null))
        {
            return ApiProblems.Forbidden(CreateRules.ForbiddenCode);
        }

        SpeologieRecord? record;
        try
        {
            record = await client.GetAsync(id, ct);
        }
        catch (SpeologieException e)
        {
            return Unreachable(e);
        }

        if (record is null)
        {
            return ApiProblems.NotFound(CaveNotFoundCode);
        }

        // Converted here rather than in the browser, and converted by exactly the code the import
        // will use, so what the screen shows is what would be stored rather than a second opinion
        // about it. That includes the truncation: a description cut on import is cut here too.
        var description = SpeologieMapping
            .ToCaveValues(record, DateTimeOffset.UtcNow, options.Value.MaxDescriptionChars)
            .Description;

        var annotated = await AnnotateAsync(db, ctx, [record], description, ct);
        return TypedResults.Ok(annotated[0]);
    }

    private static async Task<Results<Ok<SpeologieImportResultDto>, ProblemHttpResult, UnauthorizedHttpResult>>
        ImportAsync(
            SpeologieImportRequest request,
            SpeologieImportService importer,
            IAccessContextAccessor accessAccessor,
            CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // Whether the caller may bind what they create to the group they named. Checked before
        // anything is asked of the catalogue, so a request that was never going to be allowed
        // does not cost somebody else's service a call.
        if (!CreateRules.MayCreate(ctx, AccessDomain.Features, request.CavingGroupId))
        {
            return ApiProblems.Forbidden(CreateRules.ForbiddenCode);
        }

        var decisions = (request.Decisions ?? new Dictionary<string, SpeologieDecisionDto>())
            .Select(pair => (Parsed: int.TryParse(pair.Key, out var id), Id: id, pair.Value))
            // A key that is not a catalogue id is dropped rather than refused: a stale screen
            // must not be able to make the button unusable.
            .Where(x => x.Parsed && x.Value is not null)
            .ToDictionary(
                x => x.Id,
                x => new SpeologieDecision(x.Value.Action, x.Value.CaveTypeCode, x.Value.Longitude, x.Value.Latitude));

        try
        {
            var result = await importer.CommitAsync(
                request.Selection,
                decisions,
                new SpeologieImportOptions(
                    request.Visibility, request.CavingGroupId, request.LocationProtected, request.ParentId),
                ctx,
                ct);

            return TypedResults.Ok(new SpeologieImportResultDto(
                result.Batch.Id,
                result.Batch.CreatedCount,
                result.Batch.AttachedCount,
                result.Batch.SkippedCount,
                [.. result.Failures.Select(f => new SpeologieFailureDto(f.SpeologieId, f.Title, f.Code, f.Reason))]));
        }
        catch (SpeologieException e)
        {
            return Unreachable(e);
        }
        catch (SpeologieImportException e) when (e.Code == CreateRules.ForbiddenCode)
        {
            return ApiProblems.Forbidden(e.Code, e.Message);
        }
        catch (SpeologieImportException e)
        {
            return ApiProblems.BadRequest(e.Code, e.Message);
        }
    }

    /// <summary>
    /// Adds to each catalogue row the one thing the catalogue cannot say: whether this
    /// installation already holds it, and if so — and only if the caller may read it — which cave.
    /// </summary>
    /// <remarks>
    /// The two halves are answered from different queries on purpose. Whether an entry is already
    /// here is a fact about this registry that has to be true for everybody, or the screen would
    /// invite one person to import a duplicate of a cave another person can see. Which cave it is
    /// is a fact about a cave, and that is answered under the ordinary visibility rule like
    /// anything else.
    /// </remarks>
    private static async Task<IReadOnlyList<SpeologieCaveDto>> AnnotateAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        IReadOnlyList<SpeologieRecord> records,
        string? description,
        CancellationToken ct)
    {
        if (records.Count == 0)
        {
            return [];
        }

        var matches = await SpeologieSql.MatchAsync(db, [.. records.Select(r => r.Id)], ct);
        var matchedBy = matches.ToDictionary(m => m.SpeologieId, m => m.FeatureId);

        var readable = matchedBy.Count == 0
            ? []
            : await db.Features.AsNoTracking()
                .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
                .Where(f => matchedBy.Values.Contains(f.Id))
                .Select(f => f.Id)
                .ToListAsync(ct);

        var visible = readable.ToHashSet();

        return [.. records.Select(r =>
        {
            var alreadyImported = matchedBy.TryGetValue(r.Id, out var featureId);

            return new SpeologieCaveDto(
                Id: r.Id,
                Title: r.Title,
                Slug: r.Slug,
                Url: r.PublicUrl,
                County: r.Judet?.ToUpperInvariant(),
                Locality: r.Localitate,
                Mountain: r.Munte,
                Length: r.Lungime,
                Depth: r.Denivelare,
                NegativeDepth: r.DenNegativa,
                Altitude: r.Altitudine,
                ProtectionClass: ProtectionClassOf(r.Clasificare),
                Science: r.Stiinta,
                RockCode: r.Roca,
                Sump: r.Scufundabila switch { "1" => true, "0" => false, _ => null },
                Vanished: r.Disparuta is true,
                ProtectedAreaCode: r.CodAp?.Trim(),
                HydroNumber: r.NrHidro,
                HydroBasinId: r.BazinHidroId,
                Description: description,
                AlreadyImported: alreadyImported,
                ExistingCaveId: alreadyImported && visible.Contains(featureId) ? featureId : null);
        })];
    }

    /// <summary>
    /// The same shape the import stores, so the screen and the cave agree about which class a
    /// cave is in.
    /// </summary>
    private static string? ProtectionClassOf(string? raw) =>
        SpeologieMapping.ToCaveValues(new SpeologieRecord(0, string.Empty, Clasificare: raw),
            DateTimeOffset.UnixEpoch, 1).ProtectionClass;

    /// <summary>
    /// Four different ways of not getting an answer, kept apart by their codes and reported the
    /// same way: this installation cannot serve the request now, and it is not the caller's doing.
    /// </summary>
    private static ProblemHttpResult Unreachable(SpeologieException e) =>
        ApiProblems.ServiceUnavailable(e.Code, e.Message);
}
