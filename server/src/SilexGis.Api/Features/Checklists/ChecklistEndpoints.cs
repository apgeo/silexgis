// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Checklists;

/// <summary>A list of things to settle before a party sets off, and the lines on it.</summary>
public sealed record ChecklistDto(
    Guid Id,
    string Title,
    string? Description,
    Guid OwnerUserId,
    Guid? CavingGroupId,
    Visibility Visibility,
    IReadOnlyList<ChecklistItemDto> Items,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>One line, and the identity a confirmation names.</summary>
public sealed record ChecklistItemDto(Guid Id, string Text, int SortOrder);

/// <summary>
/// A line as its author asks for it to be. An id that names a line already on the list keeps
/// that line's confirmations; one that names nothing, or is absent, is a new line.
/// </summary>
public sealed record ChecklistItemRequest(Guid? Id, string Text);

/// <summary>
/// A list as its author asks for it to be, lines included.
/// </summary>
/// <remarks>
/// The lines travel with the list rather than through routes of their own. A list is edited as
/// one thing — text corrected, a line added, the order changed — and splitting that across four
/// requests would let a reader see a list halfway through somebody rewriting it. The order is
/// the order they arrive in, so an author reordering them sends them reordered.
/// </remarks>
public sealed record ChecklistWriteRequest(
    string Title,
    string? Description,
    Guid? CavingGroupId,
    Visibility Visibility,
    IReadOnlyList<ChecklistItemRequest> Items);

public sealed class ChecklistWriteRequestValidator : AbstractValidator<ChecklistWriteRequest>
{
    /// <summary>
    /// Enough lines for any list somebody would actually work through, and a bound so that one
    /// request cannot be used to write an unbounded number of rows.
    /// </summary>
    public const int MaxItems = 200;

    public ChecklistWriteRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        // A value outside the vocabulary is not a narrower audience than the ones in it: the
        // readability walk asks whether the number is at least "any account", so an unknown one
        // reads as world-visible while the surface drawing it has no word for it.
        RuleFor(x => x.Visibility).IsInEnum();
        RuleFor(x => x.Items).NotNull().Must(x => x is null || x.Count <= MaxItems)
            .WithMessage($"A list may hold at most {MaxItems} lines.");
        RuleForEach(x => x.Items).ChildRules(item =>
            item.RuleFor(x => x.Text).NotEmpty().MaximumLength(500));
    }
}

/// <summary>
/// Authoring the lists trips work through: anyone may keep their own, and share it as far as
/// its audience says.
/// </summary>
/// <remarks>
/// <para>
/// There is one kind of list. What an administrator publishes for the whole installation is a
/// row created through these same routes with an audience everyone falls inside — no separate
/// shape, no separate route, and nothing that reads a list asks whether it is "the default".
/// </para>
/// <para>
/// The audience is the visibility column and the entries about the row, and there is no
/// capability token: what a visitor with no account may reach is a short named allow-list, and
/// a list of preparations has no reason to join it.
/// </para>
/// <para>
/// Nothing here knows what a trip has settled. How much of a list a party has worked through is
/// read on the trip, is worked out from rows rather than stored, and is never consulted when
/// deciding who may read anything.
/// </para>
/// </remarks>
public static class ChecklistEndpoints
{
    /// <summary>Absent, and unreachable-so, read alike: a refusal never confirms a list exists.</summary>
    public const string NotFoundCode = "checklist.not_found";

    public static RouteGroupBuilder MapChecklistEndpoints(this RouteGroupBuilder api)
    {
        var checklists = api.MapGroup("/checklists").WithTags("Checklists");

        checklists.MapGet("/", ListAsync)
            .WithSummary("The lists this caller may read, lines included.");
        checklists.MapGet("/{id:guid}", GetAsync)
            .WithSummary("One list. Answered as absent where the caller may not read it.");
        checklists.MapPost("/", CreateAsync).WithValidation<ChecklistWriteRequest>()
            .WithSummary("Writes a list; the caller becomes its owner.");
        checklists.MapPut("/{id:guid}", UpdateAsync).WithValidation<ChecklistWriteRequest>()
            .WithSummary("Rewrites a list and its lines (Write permission).");
        checklists.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Deletes a list and its lines (Delete permission).");

        return api;
    }

    private static async Task<Results<Ok<List<ChecklistDto>>, UnauthorizedHttpResult>> ListAsync(
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var lists = await db.Checklists.AsNoTracking()
            .VisibleTo(ctx, AccessDomain.Checklists)
            .OrderBy(x => x.Title)
            .ToListAsync(ct);

        // One query for every line of the page rather than one per list: the lines are read for
        // the whole answer at once, the way anything belonging to a page of rows is.
        var items = await LinesOfAsync(db, [.. lists.Select(x => x.Id)], ct);
        return TypedResults.Ok(
            lists.Select(x => ToDto(x, items.GetValueOrDefault(x.Id, []))).ToList());
    }

    private static async Task<Results<Ok<ChecklistDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
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

        var list = await db.Checklists.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (list is null || !(await access.DecideAsync(ctx, AccessAction.Read, list, ct)).Allowed)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        var items = await LinesOfAsync(db, [list.Id], ct);
        return TypedResults.Ok(ToDto(list, items.GetValueOrDefault(list.Id, [])));
    }

    private static async Task<Results<Created<ChecklistDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        ChecklistWriteRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!CreateRules.MayCreate(ctx, AccessDomain.Checklists, request.CavingGroupId))
        {
            return ApiProblems.Forbidden(CreateRules.ForbiddenCode);
        }

        if (request.CavingGroupId is not null
            && !CavingGroupBindingRules.MayBind(ctx, AccessDomain.Checklists, request.CavingGroupId.Value))
        {
            return ApiProblems.Forbidden(CavingGroupBindingRules.ForbiddenCode);
        }

        var list = new Checklist { Title = request.Title, OwnerUserId = ctx.UserId };
        Apply(list, request);
        db.Checklists.Add(list);

        var lines = NewLines(list.Id, request.Items);
        db.ChecklistItems.AddRange(lines);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/v1/checklists/{list.Id}", ToDto(list, lines));
    }

    private static async Task<Results<Ok<ChecklistDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        ChecklistWriteRequest request,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var list = await db.Checklists.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (list is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (ctx is null || !(await access.DecideAsync(ctx, AccessAction.Write, list, ct)).Allowed)
        {
            return (await access.DecideAsync(ctx, AccessAction.Read, list, ct)).Allowed
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound(NotFoundCode);
        }

        // Re-binding on update moves rights exactly as binding on create does: being allowed to
        // write a row is not consent to hand it to a club.
        if (request.CavingGroupId is { } requested
            && requested != list.CavingGroupId
            && !CavingGroupBindingRules.MayBind(ctx, AccessDomain.Checklists, requested))
        {
            return ApiProblems.Forbidden(CavingGroupBindingRules.ForbiddenCode);
        }

        Apply(list, request);

        var existing = await db.ChecklistItems.Where(x => x.ChecklistId == list.Id).ToListAsync(ct);
        var kept = new HashSet<Guid>();
        var lines = new List<ChecklistItem>();
        var order = 0;
        foreach (var wanted in request.Items ?? [])
        {
            // A line named by an id keeps that row, so every confirmation made against it stands.
            // Rewording a line is a correction of the same line, not a different one — and losing
            // the confirmations for a typo fixed the morning of the trip is exactly the failure
            // that makes people stop using the list.
            var row = wanted.Id is { } wantedId
                ? existing.FirstOrDefault(x => x.Id == wantedId)
                : null;
            if (row is null)
            {
                row = new ChecklistItem { ChecklistId = list.Id, Text = wanted.Text };
                db.ChecklistItems.Add(row);
            }

            row.Text = wanted.Text;
            row.SortOrder = order++;
            kept.Add(row.Id);
            lines.Add(row);
        }

        // A line taken off the list takes its confirmations with it, by the reference between
        // them: a confirmation of something that is no longer on the list is not a fact about
        // anything the list measures.
        db.ChecklistItems.RemoveRange(existing.Where(x => !kept.Contains(x.Id)));

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToDto(list, lines));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var list = await db.Checklists.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (list is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        // Refused as absent rather than forbidden, so that probing for a list by trying to delete
        // it says no more than reading it does.
        if (ctx is null || !(await access.DecideAsync(ctx, AccessAction.Delete, list, ct)).Allowed)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        // A purpose that named this list is left naming none, by the reference itself rather than
        // by a statement here: the column is set-null, so the purposes are cleared inside the same
        // transaction as the delete and cannot be cleared by a request that then fails to delete.
        // Leaving them naming none is deliberate and not a refusal — a list nobody can delete
        // because a purpose points at it would make an administrator's tidying depend on a
        // reference they may not even be able to see.
        //
        // The lines go with the list, and the confirmations go with the lines.
        db.Checklists.Remove(list);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Dictionary<Guid, List<ChecklistItem>>> LinesOfAsync(
        SilexGisDbContext db, IReadOnlyList<Guid> checklistIds, CancellationToken ct)
    {
        if (checklistIds.Count == 0)
        {
            return [];
        }

        var rows = await db.ChecklistItems.AsNoTracking()
            .Where(x => checklistIds.Contains(x.ChecklistId))
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
            .ToListAsync(ct);
        return rows.GroupBy(x => x.ChecklistId).ToDictionary(g => g.Key, g => g.ToList());
    }

    private static List<ChecklistItem> NewLines(Guid checklistId, IReadOnlyList<ChecklistItemRequest>? items)
    {
        var order = 0;
        return [.. (items ?? []).Select(x => new ChecklistItem
        {
            ChecklistId = checklistId,
            Text = x.Text,
            SortOrder = order++,
        })];
    }

    private static void Apply(Checklist list, ChecklistWriteRequest request)
    {
        list.Title = request.Title;
        list.Description = request.Description;
        list.CavingGroupId = request.CavingGroupId;
        list.Visibility = request.Visibility;
    }

    private static ChecklistDto ToDto(Checklist list, IReadOnlyList<ChecklistItem> items) => new(
        list.Id,
        list.Title,
        list.Description,
        list.OwnerUserId,
        list.CavingGroupId,
        list.Visibility,
        [.. items.Select(x => new ChecklistItemDto(x.Id, x.Text, x.SortOrder))],
        list.CreatedAt,
        list.UpdatedAt);
}
