// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Trips;

namespace SilexGis.Api.Features.Taxonomies;

/// <summary>
/// One purpose a trip may be recorded under. <c>IsSeeded</c> travels with the row so a client
/// can say which rows it may offer to edit without keeping its own copy of the shipped list.
/// </summary>
public sealed record TripTypeDto(
    long Id,
    string Code,
    string Name,
    string? Description,
    int SortOrder,
    bool IsSeeded,
    // The three schemas travel as the raw text an administrator wrote, because that is what the
    // form that renders them parses and what the editor edits. Each carries its own version, so
    // a client can tell which of the three actually moved.
    string? FieldDataSchema,
    int FieldDataSchemaVersion,
    string? LogisticsSchema,
    int LogisticsSchemaVersion,
    string? SafetySchema,
    int SafetySchemaVersion);

/// <summary>A trip purpose as an administrator asks for it to be.</summary>
public sealed record TripTypeRequest(
    string Code,
    string Name,
    string? Description,
    int SortOrder,
    string? FieldDataSchema,
    string? LogisticsSchema,
    string? SafetySchema);

public sealed class TripTypeRequestValidator : AbstractValidator<TripTypeRequest>
{
    public TripTypeRequestValidator()
    {
        // Codes are the natural key installations exchange rows by, so they stay to the
        // characters that survive a URL, a file name and a spreadsheet unharmed.
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50)
            .Matches("^[a-z0-9_]+$")
            .WithMessage("The code may contain lower-case letters, digits and underscores.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(1000);
        // Whether the text is a usable JSON Schema is decided by the schema engine on the write
        // path; this only keeps an unbounded blob out of a jsonb column.
        RuleFor(x => x.FieldDataSchema).MaximumLength(64_000);
        RuleFor(x => x.LogisticsSchema).MaximumLength(64_000);
        RuleFor(x => x.SafetySchema).MaximumLength(64_000);
    }
}

/// <summary>
/// The trip-purpose vocabulary: the rows that ship plus whatever a club adds. Reading it is open
/// to every account through the taxonomy read the All Users seed carries — every trip screen
/// renders a purpose from it. Editing it needs taxonomy rights, which by default only a full
/// administrator holds.
/// <para>
/// Shipped rows are the exchange vocabulary: their codes are immutable and the rows undeletable,
/// because clients translate labels by code and installations exchange records under them, so a
/// renamed or deleted code would silently break both. Their wording and ordering stay editable —
/// that is presentation, and an installation is entitled to its own. Rows a club adds are
/// installation-local and freely edited, and deletion is refused while any trip still names one.
/// </para>
/// </summary>
public static class TripTypeEndpoints
{
    // Aliases of the write service's codes, so a caller reading this slice sees what it can be
    // answered with while the strings themselves keep one home.
    public const string NotFoundCode = TripTypeWriteService.NotFoundCode;
    public const string CodeTakenCode = TripTypeWriteService.CodeTakenCode;
    public const string SeededImmutableCode = TripTypeWriteService.SeededImmutableCode;
    public const string InUseCode = TripTypeWriteService.InUseCode;

    public static RouteGroupBuilder MapTripTypeEndpoints(this RouteGroupBuilder api)
    {
        var types = api.MapGroup("/trip-types").WithTags("Taxonomies");

        types.MapGet("/", ListAsync)
            .WithSummary("The trip purposes: shipped rows (translated by code) and club rows (shown as written).");
        types.MapPost("/", CreateAsync)
            .WithValidation<TripTypeRequest>()
            .WithSummary("Adds a trip purpose.");
        types.MapPut("/{id:long}", UpdateAsync)
            .WithValidation<TripTypeRequest>()
            .WithSummary("Updates a trip purpose; a shipped row keeps its code.");
        types.MapDelete("/{id:long}", DeleteAsync)
            .WithSummary("Deletes an unused club trip purpose; shipped rows cannot be deleted.");

        return api;
    }

    private static async Task<Results<Ok<List<TripTypeDto>>, ProblemHttpResult>> ListAsync(
        SilexGisDbContext db, IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null || !AccessEvaluator.Decide(ctx, AccessDomain.Taxonomies, AccessAction.Read, null).Allowed)
        {
            return ApiProblems.Forbidden();
        }

        var rows = await db.TripTypes.AsNoTracking()
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Id)
            .ToListAsync(ct);
        return TypedResults.Ok(rows.Select(ToDto).ToList());
    }

    private static async Task<Results<Created<TripTypeDto>, ProblemHttpResult>> CreateAsync(
        TripTypeRequest request,
        TripTypeWriteService writer,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (!CreateRules.MayCreate(ctx, AccessDomain.Taxonomies))
        {
            return ApiProblems.Forbidden(CreateRules.ForbiddenCode);
        }

        try
        {
            var row = await writer.CreateAsync(ToInput(request), ct);
            return TypedResults.Created($"/api/v1/trip-types/{row.Id}", ToDto(row));
        }
        catch (TripWriteException e)
        {
            return ToProblem(e);
        }
    }

    private static async Task<Results<Ok<TripTypeDto>, ProblemHttpResult>> UpdateAsync(
        long id,
        TripTypeRequest request,
        TripTypeWriteService writer,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null || !AccessEvaluator.Decide(ctx, AccessDomain.Taxonomies, AccessAction.Write, null).Allowed)
        {
            return ApiProblems.Forbidden();
        }

        try
        {
            return TypedResults.Ok(ToDto(await writer.UpdateAsync(id, ToInput(request), ct)));
        }
        catch (TripWriteException e)
        {
            return ToProblem(e);
        }
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        long id,
        TripTypeWriteService writer,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null || !AccessEvaluator.Decide(ctx, AccessDomain.Taxonomies, AccessAction.Delete, null).Allowed)
        {
            return ApiProblems.Forbidden();
        }

        try
        {
            await writer.DeleteAsync(id, ct);
            return TypedResults.NoContent();
        }
        catch (TripWriteException e)
        {
            return ToProblem(e, seededIsConflict: true);
        }
    }

    // A refused edit and a refused delete of the same shipped row are told apart by status: an
    // edit that only wanted a different code is a request that could have been written another
    // way, while deleting a row that ships with the software conflicts with what the row is.
    private static ProblemHttpResult ToProblem(TripWriteException e, bool seededIsConflict = false) => e.Code switch
    {
        TripTypeWriteService.NotFoundCode => ApiProblems.NotFound(e.Code),
        TripTypeWriteService.InUseCode => ApiProblems.Conflict(e.Code, e.Message),
        TripTypeWriteService.SeededImmutableCode when seededIsConflict => ApiProblems.Conflict(e.Code, e.Message),
        _ => ApiProblems.BadRequest(e.Code, e.Message),
    };

    private static TripTypeInput ToInput(TripTypeRequest request) => new(
        request.Code,
        request.Name,
        request.Description,
        request.SortOrder,
        request.FieldDataSchema,
        request.LogisticsSchema,
        request.SafetySchema);

    private static TripTypeDto ToDto(TripType row) => new(
        row.Id,
        row.Code,
        row.Name,
        row.Description,
        row.SortOrder,
        TripTypeSeeds.IsSeeded(row.Code),
        row.FieldDataSchema,
        row.FieldDataSchemaVersion,
        row.LogisticsSchema,
        row.LogisticsSchemaVersion,
        row.SafetySchema,
        row.SafetySchemaVersion);
}
