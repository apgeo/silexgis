// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Filters;
using SilexGis.Infrastructure.Filters;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.SavedFilters;

/// <summary>A saved filter as one reader sees it.</summary>
/// <param name="Document">
/// The stored question, or null when <paramref name="State"/> says this reader cannot run it — a
/// question they cannot run has nothing to show them, and whatever made it unreadable stays in the
/// row. Never carries an anchor. A value it names is shown by name only through lists the reader
/// may already read, so an identity they cannot resolve reads as "not available to you", never as
/// a name.
/// </param>
/// <param name="State">What it reads as to this reader — runnable, unreadable for them, or stale.</param>
/// <param name="Mine">Whether the reader wrote it.</param>
public sealed record SavedFilterDto(
    Guid Id,
    string Name,
    string? Description,
    FilterDocument? Document,
    SavedFilterState State,
    Guid? CavingGroupId,
    Visibility Visibility,
    bool Mine,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>What a saved filter is written as.</summary>
/// <param name="Document">
/// The question. Required when creating; on an update, null keeps the stored one, so renaming or
/// sharing a filter never has to re-send — and re-validate — a question nobody changed.
/// </param>
/// <param name="Visibility">Private, or the members of <paramref name="CavingGroupId"/>.</param>
public sealed record SavedFilterWriteRequest(
    string Name,
    string? Description,
    FilterDocument? Document,
    Guid? CavingGroupId,
    Visibility Visibility = Visibility.Private);

public sealed class SavedFilterWriteRequestValidator : AbstractValidator<SavedFilterWriteRequest>
{
    public SavedFilterWriteRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(SavedFilterRules.MaxNameLength);
        RuleFor(x => x.Description).MaximumLength(SavedFilterRules.MaxDescriptionLength);
        RuleFor(x => x.Visibility)
            .Must(SavedFilterRules.IsAllowedVisibility)
            .WithMessage("A saved filter is private or shared with one caving group.");
        RuleFor(x => x.CavingGroupId)
            .NotNull()
            .When(x => x.Visibility == Visibility.CavingGroup)
            .WithMessage("Sharing with a caving group needs the group.");
        // A row bound to a group is reached by rules scoped to that group whatever its visibility
        // says, so "private" with a group would be read by the group's members after all.
        RuleFor(x => x.CavingGroupId)
            .Null()
            .When(x => x.Visibility == Visibility.Private)
            .WithMessage("A private filter is shared with nobody, so it names no caving group.");
    }
}

/// <summary>
/// Filters somebody named and kept, and shared — or not — with a caving group.
/// </summary>
/// <remarks>
/// <para>
/// Governed by the rules over saved map views, as the same kind of thing: saved workspace. A saved
/// filter describes a question and grants nothing; whoever runs one is answered under their own
/// access, wherever it is run.
/// </para>
/// <para>
/// A document is validated against its author's vocabulary when it is saved, and against each
/// reader's when it is read or run. It never stores an anchor or any other position: one sent is
/// refused, and the stored text is always the server's own serialisation of the document it parsed,
/// so nothing that travelled beside the document's members is kept.
/// </para>
/// </remarks>
public static class SavedFilterEndpoints
{
    public const string NotFoundCode = SurfaceFilters.SavedNotFoundCode;
    public const string DocumentRequiredCode = "saved_filter.document_required";

    public static RouteGroupBuilder MapSavedFilterEndpoints(this RouteGroupBuilder api)
    {
        var filters = api.MapGroup("/saved-filters").WithTags("SavedFilters");

        filters.MapGet("/", ListAsync)
            .WithSummary("The saved filters the caller may read, each with what it reads as to them.");
        filters.MapGet("/{id:guid}", GetAsync)
            .WithSummary("One saved filter, with what it reads as to the caller.");
        filters.MapPost("/", CreateAsync).WithValidation<SavedFilterWriteRequest>()
            .WithSummary("Saves a filter; the caller becomes owner. The document is checked against "
                + "the caller's vocabulary and may not carry an anchor.");
        filters.MapPut("/{id:guid}", UpdateAsync).WithValidation<SavedFilterWriteRequest>()
            .WithSummary("Renames, describes, shares or rewrites a saved filter (its author or a full "
                + "administrator, holding Write permission).");
        filters.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Deletes a saved filter (its author or a full administrator, holding Delete permission).");

        return api;
    }

    private static async Task<Results<Ok<List<SavedFilterDto>>, UnauthorizedHttpResult>> ListAsync(
        SavedFilterReader reader,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var rows = await reader.Visible(ctx)
            .OrderBy(x => x.Name).ThenBy(x => x.Id)
            .ToListAsync(ct);
        var readings = await reader.DescribeAsync(ctx, rows, ct);
        return TypedResults.Ok(readings.Select(r => ToDto(r, ctx)).ToList());
    }

    private static async Task<Results<Ok<SavedFilterDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        Guid id,
        SavedFilterReader reader,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var reading = await reader.ReadAsync(ctx, id, ct);
        return reading is null ? ApiProblems.NotFound(NotFoundCode) : TypedResults.Ok(ToDto(reading, ctx));
    }

    private static async Task<Results<Created<SavedFilterDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        SavedFilterWriteRequest request,
        SilexGisDbContext db,
        SavedFilterReader reader,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!CreateRules.MayCreate(ctx, AccessDomain.MapViews, request.CavingGroupId))
        {
            return ApiProblems.Forbidden(CreateRules.ForbiddenCode);
        }

        if (request.CavingGroupId is { } groupId
            && !CavingGroupBindingRules.MayBind(ctx, AccessDomain.MapViews, groupId))
        {
            return ApiProblems.Forbidden(CavingGroupBindingRules.ForbiddenCode);
        }

        if (request.Document is null)
        {
            return ApiProblems.BadRequest(DocumentRequiredCode, "A saved filter has to say what it asks.");
        }

        if (await DocumentProblemAsync(request.Document, reader, ctx, ct) is { } problem)
        {
            return problem;
        }

        var row = new SavedFilter { Name = request.Name, OwnerUserId = ctx.UserId };
        Apply(row, request);
        db.SavedFilters.Add(row);
        await db.SaveChangesAsync(ct);

        var reading = (await reader.DescribeAsync(ctx, [row], ct))[0];
        return TypedResults.Created($"/api/v1/saved-filters/{row.Id}", ToDto(reading, ctx));
    }

    private static async Task<Results<Ok<SavedFilterDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        SavedFilterWriteRequest request,
        SilexGisDbContext db,
        SavedFilterReader reader,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var row = await db.SavedFilters.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!SavedFilterRules.MayChange(ctx, row)
            || !(await access.DecideAsync(ctx, AccessAction.Write, row, ct)).Allowed)
        {
            // Somebody who may read it is told they may not change it; anybody else is told there
            // is nothing here, which is all a filter they cannot read should ever say to them.
            return (await access.DecideAsync(ctx, AccessAction.Read, row, ct)).Allowed
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound(NotFoundCode);
        }

        // Re-binding moves who may read the question exactly as binding on create does; being the
        // author is not consent from the club it is handed to.
        if (request.CavingGroupId is { } requested
            && requested != row.CavingGroupId
            && !CavingGroupBindingRules.MayBind(ctx, AccessDomain.MapViews, requested))
        {
            return ApiProblems.Forbidden(CavingGroupBindingRules.ForbiddenCode);
        }

        if (request.Document is not null
            && await DocumentProblemAsync(request.Document, reader, ctx, ct) is { } problem)
        {
            return problem;
        }

        Apply(row, request);
        await db.SaveChangesAsync(ct);

        var reading = (await reader.DescribeAsync(ctx, [row], ct))[0];
        return TypedResults.Ok(ToDto(reading, ctx));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> DeleteAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var row = await db.SavedFilters.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!SavedFilterRules.MayChange(ctx, row)
            || !(await access.DecideAsync(ctx, AccessAction.Delete, row, ct)).Allowed)
        {
            return (await access.DecideAsync(ctx, AccessAction.Read, row, ct)).Allowed
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound(NotFoundCode);
        }

        db.SavedFilters.Remove(row);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>Why this document may not be stored for this author, or null when it may.</summary>
    private static async Task<ProblemHttpResult?> DocumentProblemAsync(
        FilterDocument document, SavedFilterReader reader, AccessContext ctx, CancellationToken ct)
    {
        if (!FilterValidation.IsWellFormed(document))
        {
            return ApiProblems.BadRequest(SurfaceFilters.MalformedCode, "This filter could not be read.");
        }

        // A point or an outline inside a condition is refused like an anchor on the document: both
        // are a position the stored filter would hand to everybody it is shared with.
        if (document.Anchor is not null || SpatialConditions.HoldsPosition(document))
        {
            return ApiProblems.BadRequest(
                SavedFilterRules.AnchorRefusedCode, "A saved filter never stores a place to measure from.");
        }

        var errors = SavedFilterRules.ProblemsOnSave(document, await reader.VocabulariesAsync(ctx, ct));
        return errors.Count == 0 ? null : ApiProblems.BadRequest(SurfaceFilters.InvalidCode, string.Join(" ", errors));
    }

    private static void Apply(SavedFilter row, SavedFilterWriteRequest request)
    {
        row.Name = request.Name.Trim();
        row.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        row.CavingGroupId = request.CavingGroupId;
        row.Visibility = request.Visibility;
        if (request.Document is not null)
        {
            // The server's own serialisation of what it parsed and validated, never the text as
            // sent: whatever travelled beside the document's members is not stored.
            row.Document = FilterDocument.Serialize(request.Document);
        }
    }

    private static SavedFilterDto ToDto(SavedFilterReading reading, AccessContext ctx) => new(
        reading.Row.Id,
        reading.Row.Name,
        reading.Row.Description,
        // An unreadable document is not shown at all, and a readable one has no anchor to show;
        // the anchor is cleared here as well so a position written into a row by any other road
        // than this route still never reaches whoever opens the filter.
        reading.State == SavedFilterState.Unreadable ? null : reading.Document! with { Anchor = null },
        reading.State,
        reading.Row.CavingGroupId,
        reading.Row.Visibility,
        reading.Row.OwnerUserId == ctx.UserId,
        reading.Row.CreatedAt,
        reading.Row.UpdatedAt);
}
