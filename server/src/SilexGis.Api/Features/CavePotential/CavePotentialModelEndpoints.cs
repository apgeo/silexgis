// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Potential;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.CavePotential;

/// <summary>One kind of evidence a model can weigh, and how it may be scored.</summary>
/// <param name="UsesPositions">
/// Whether a surface of this kind is made from recorded positions. A map computed from a model
/// that weighs such a layer is governed by who may place those features, so a form says so before
/// the layer is chosen.
/// </param>
/// <param name="Unit">What a cell's value is measured in, as a stable code, or null for a class.</param>
/// <param name="Transforms">The ways this layer's values may be turned into scores.</param>
/// <param name="Categories">The class codes of a categorical layer; empty otherwise.</param>
public sealed record EvidenceLayerDto(
    string Code,
    EvidenceValueKind ValueKind,
    EvidenceSource Source,
    bool UsesPositions,
    string? Unit,
    IReadOnlyList<EvidenceTransformKind> Transforms,
    IReadOnlyList<string> Categories);

/// <summary>One layer's share of a model, out of one.</summary>
public sealed record EvidenceLayerWeightDto(string Layer, double Weight);

/// <summary>How consistent a model's pairwise comparisons are with each other.</summary>
/// <param name="Ratio">The consistency ratio. Above one tenth the comparisons are customarily revised.</param>
/// <param name="Acceptable">
/// Whether the ratio is within one tenth. False is a warning for whoever made the comparisons and
/// never a refusal: the model is stored and may be run either way.
/// </param>
public sealed record PairwiseConsistencyDto(
    double PrincipalEigenvalue,
    double Index,
    double RandomIndex,
    double Ratio,
    bool Acceptable);

/// <summary>The weights a model works with, in the order of its layers.</summary>
/// <param name="Consistency">Present when the weights follow from pairwise comparisons.</param>
public sealed record CavePotentialWeightingDto(
    IReadOnlyList<EvidenceLayerWeightDto> Weights,
    PairwiseConsistencyDto? Consistency);

/// <summary>A cave-potential model as one reader sees it.</summary>
/// <param name="Definition">
/// The layers, their scoring and their weights or comparisons; null when the stored definition is
/// one this version cannot read.
/// </param>
/// <param name="Weighting">
/// The weights the definition comes to, worked out by the server. Null when the definition cannot
/// be read or is no longer a valid model.
/// </param>
/// <param name="UsesPositions">
/// Whether the model weighs any layer made from recorded positions (see
/// <see cref="EvidenceLayerDto.UsesPositions"/>).
/// </param>
/// <param name="Mine">Whether the reader wrote it.</param>
/// <param name="AuthorName">Who wrote it, as the reader may be shown that member, or null when the account is gone.</param>
/// <param name="MayEdit">Whether the server would accept this reader's change to it: its author or a full administrator, holding Write permission.</param>
/// <param name="MayDelete">The same answer for deleting it.</param>
public sealed record CavePotentialModelDto(
    Guid Id,
    string Name,
    string? Description,
    CavePotentialMethod Method,
    CavePotentialModelDefinition? Definition,
    CavePotentialWeightingDto? Weighting,
    bool UsesPositions,
    Guid? CavingGroupId,
    Visibility Visibility,
    bool Mine,
    string? AuthorName,
    bool MayEdit,
    bool MayDelete,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>What a model is written as.</summary>
/// <param name="Visibility">Private, the members of <paramref name="CavingGroupId"/>, or everyone signed in.</param>
public sealed record CavePotentialModelWriteRequest(
    string Name,
    string? Description,
    CavePotentialMethod Method,
    CavePotentialModelDefinition Definition,
    Guid? CavingGroupId,
    Visibility Visibility = Visibility.Private);

/// <summary>A copy of a model the caller may read, under a name of their choosing.</summary>
public sealed record CavePotentialModelCloneRequest(string Name);

/// <summary>A definition whose weights are wanted before it is saved.</summary>
public sealed record CavePotentialWeightingRequest(
    CavePotentialMethod Method,
    CavePotentialModelDefinition Definition);

public sealed class CavePotentialModelWriteRequestValidator : AbstractValidator<CavePotentialModelWriteRequest>
{
    public CavePotentialModelWriteRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(CavePotentialModelRules.MaxNameLength);
        RuleFor(x => x.Description).MaximumLength(CavePotentialModelRules.MaxDescriptionLength);
        RuleFor(x => x.Visibility)
            .Must(CavePotentialModelRules.IsAllowedVisibility)
            .WithMessage("A model is private, shared with one caving group, or shared with everyone signed in.");
        RuleFor(x => x.CavingGroupId)
            .NotNull()
            .When(x => CavePotentialModelRules.NamesACavingGroup(x.Visibility))
            .WithMessage("Sharing with a caving group needs the group.");
        // Bound to a group, a row is reached by the rules scoped to that group whatever its
        // visibility says, so a private model naming one would not be private.
        RuleFor(x => x.CavingGroupId)
            .Null()
            .When(x => !CavePotentialModelRules.NamesACavingGroup(x.Visibility))
            .WithMessage("Only a model shared with a caving group names one.");
        RuleFor(x => x).Custom((request, context) =>
            CavePotentialModelEndpoints.AddDefinitionFailures(request.Method, request.Definition, context));
    }
}

public sealed class CavePotentialModelCloneRequestValidator : AbstractValidator<CavePotentialModelCloneRequest>
{
    public CavePotentialModelCloneRequestValidator() =>
        RuleFor(x => x.Name).NotEmpty().MaximumLength(CavePotentialModelRules.MaxNameLength);
}

public sealed class CavePotentialWeightingRequestValidator : AbstractValidator<CavePotentialWeightingRequest>
{
    public CavePotentialWeightingRequestValidator() =>
        RuleFor(x => x).Custom((request, context) =>
            CavePotentialModelEndpoints.AddDefinitionFailures(request.Method, request.Definition, context));
}

/// <summary>
/// Cave-potential models somebody named and kept: which evidence is weighed, how each layer is
/// scored, and how the layers weigh against each other.
/// </summary>
/// <remarks>
/// <para>
/// A model is a recipe and holds no place, so these routes read no terrain and no feature and are
/// governed as saved workspace, with the saved views and filters: its author's, shared with a
/// caving group or with everyone signed in, never with anybody without an account. What a run of a
/// model may read is decided where it is run, under the runner's own access.
/// </para>
/// <para>
/// A shared model is changed only by its author or a full administrator. Anybody who may read one
/// may take a copy, which is theirs and private until they share it.
/// </para>
/// </remarks>
public static class CavePotentialModelEndpoints
{
    public const string NotFoundCode = "cave_potential_model.not_found";

    public static RouteGroupBuilder MapCavePotentialModelEndpoints(this RouteGroupBuilder api)
    {
        var potential = api.MapGroup("/cave-potential").WithTags("CavePotential");

        potential.MapGet("/evidence-layers", EvidenceLayersAsync)
            .WithSummary("The kinds of evidence a model can weigh, and how each may be scored.");

        var models = potential.MapGroup("/models");
        models.MapGet("/", ListAsync)
            .WithSummary("The cave-potential models the caller may read.");
        models.MapGet("/{id:guid}", GetAsync)
            .WithSummary("One cave-potential model, with the weights it comes to.");
        models.MapPost("/", CreateAsync).WithValidation<CavePotentialModelWriteRequest>()
            .WithSummary("Saves a model; the caller becomes owner.");
        models.MapPut("/{id:guid}", UpdateAsync).WithValidation<CavePotentialModelWriteRequest>()
            .WithSummary("Renames, describes, shares or rewrites a model (its author or a full "
                + "administrator, holding Write permission).");
        models.MapPost("/{id:guid}/clone", CloneAsync).WithValidation<CavePotentialModelCloneRequest>()
            .WithSummary("Copies a model the caller may read into a private one of their own.");
        models.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Deletes a model (its author or a full administrator, holding Delete permission).");
        models.MapPost("/weighting", WeightingAsync).WithValidation<CavePotentialWeightingRequest>()
            .WithSummary("The weights an unsaved definition comes to, with the consistency of its "
                + "pairwise comparisons. Stores nothing.");

        return api;
    }

    /// <summary>
    /// Reports what stops a definition being stored, each problem under the member at fault. The
    /// one rule that decides is asked, rather than a second copy of it written in validator form.
    /// </summary>
    internal static void AddDefinitionFailures<T>(
        CavePotentialMethod method, CavePotentialModelDefinition? definition, ValidationContext<T> context)
    {
        foreach (var problem in CavePotentialModelRules.Problems(method, definition))
        {
            context.AddFailure(
                problem.Path == "method" ? "method" : $"definition.{problem.Path}", problem.Message);
        }
    }

    private static async Task<Results<Ok<List<EvidenceLayerDto>>, UnauthorizedHttpResult>> EvidenceLayersAsync(
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        if (await accessAccessor.GetAsync(ct) is null)
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(EvidenceLayers.All
            .Select(l => new EvidenceLayerDto(
                l.Code,
                l.ValueKind,
                l.Source,
                l.UsesPositions,
                l.Unit,
                [.. Enum.GetValues<EvidenceTransformKind>().Where(l.Accepts)],
                l.Categories))
            .ToList());
    }

    private static async Task<Results<Ok<List<CavePotentialModelDto>>, UnauthorizedHttpResult>> ListAsync(
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null)
        {
            return TypedResults.Unauthorized();
        }

        var rows = await Visible(db, ctx).AsNoTracking()
            .OrderBy(x => x.Name).ThenBy(x => x.Id)
            .ToListAsync(ct);
        return TypedResults.Ok(await ToDtosAsync(rows, ctx, user, db, access, ct));
    }

    private static async Task<Results<Ok<CavePotentialModelDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null)
        {
            return TypedResults.Unauthorized();
        }

        var row = await Visible(db, ctx).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return row is null
            ? ApiProblems.NotFound(NotFoundCode)
            : TypedResults.Ok((await ToDtosAsync([row], ctx, user, db, access, ct))[0]);
    }

    private static async Task<Results<Created<CavePotentialModelDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        CavePotentialModelWriteRequest request,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null)
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

        var row = new CavePotentialModel { Name = request.Name, OwnerUserId = ctx.UserId };
        Apply(row, request);
        db.CavePotentialModels.Add(row);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created(
            $"/api/v1/cave-potential/models/{row.Id}", (await ToDtosAsync([row], ctx, user, db, access, ct))[0]);
    }

    private static async Task<Results<Ok<CavePotentialModelDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        CavePotentialModelWriteRequest request,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null)
        {
            return TypedResults.Unauthorized();
        }

        var row = await db.CavePotentialModels.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!CavePotentialModelRules.MayChange(ctx, row)
            || !(await access.DecideAsync(ctx, AccessAction.Write, row, ct)).Allowed)
        {
            // Somebody who may read it is told they may not change it; anybody else is told there
            // is nothing here, which is all a model they cannot read should ever say to them.
            return (await access.DecideAsync(ctx, AccessAction.Read, row, ct)).Allowed
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound(NotFoundCode);
        }

        // Re-binding moves who may read the model exactly as binding on create does; being the
        // author is not consent from the club it is handed to.
        if (request.CavingGroupId is { } requested
            && requested != row.CavingGroupId
            && !CavingGroupBindingRules.MayBind(ctx, AccessDomain.MapViews, requested))
        {
            return ApiProblems.Forbidden(CavingGroupBindingRules.ForbiddenCode);
        }

        Apply(row, request);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok((await ToDtosAsync([row], ctx, user, db, access, ct))[0]);
    }

    private static async Task<Results<Created<CavePotentialModelDto>, UnauthorizedHttpResult, ProblemHttpResult>> CloneAsync(
        Guid id,
        CavePotentialModelCloneRequest request,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null)
        {
            return TypedResults.Unauthorized();
        }

        // Read first: a model the caller cannot read is not there for them, whatever else they may
        // or may not do.
        var source = await Visible(db, ctx).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (source is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!CreateRules.MayCreate(ctx, AccessDomain.MapViews))
        {
            return ApiProblems.Forbidden(CreateRules.ForbiddenCode);
        }

        // The copy is the caller's and private, whoever the original was shared with: taking a
        // copy of a club's model is not a decision to publish one to the club.
        var copy = new CavePotentialModel
        {
            Name = request.Name.Trim(),
            Description = source.Description,
            Method = source.Method,
            Definition = source.Definition,
            OwnerUserId = ctx.UserId,
            Visibility = Visibility.Private,
        };
        db.CavePotentialModels.Add(copy);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created(
            $"/api/v1/cave-potential/models/{copy.Id}", (await ToDtosAsync([copy], ctx, user, db, access, ct))[0]);
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

        var row = await db.CavePotentialModels.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!CavePotentialModelRules.MayChange(ctx, row)
            || !(await access.DecideAsync(ctx, AccessAction.Delete, row, ct)).Allowed)
        {
            return (await access.DecideAsync(ctx, AccessAction.Read, row, ct)).Allowed
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound(NotFoundCode);
        }

        db.CavePotentialModels.Remove(row);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<CavePotentialWeightingDto>, UnauthorizedHttpResult>> WeightingAsync(
        CavePotentialWeightingRequest request,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        if (await accessAccessor.GetAsync(ct) is null)
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(ToDto(CavePotentialModelRules.Weighting(request.Method, request.Definition)));
    }

    /// <summary>
    /// The models this caller may read. States no tracking option: that belongs to whoever
    /// materialises the query and knows whether the rows will be changed.
    /// </summary>
    private static IQueryable<CavePotentialModel> Visible(SilexGisDbContext db, AccessContext ctx) =>
        db.CavePotentialModels.VisibleTo(ctx, AccessDomain.MapViews);

    private static void Apply(CavePotentialModel row, CavePotentialModelWriteRequest request)
    {
        row.Name = request.Name.Trim();
        row.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        row.CavingGroupId = request.CavingGroupId;
        row.Visibility = request.Visibility;
        row.Method = request.Method;
        // The server's own serialisation of what it parsed and validated, never the text as sent.
        row.Definition = CavePotentialModelDefinition.Serialize(request.Definition);
    }

    /// <summary>
    /// Each row as this reader sees it: the author named through the profile directory's
    /// attribution rule, and the changes they may make decided by the same two checks the write
    /// routes apply, so the screen and the server never disagree.
    /// </summary>
    private static async Task<List<CavePotentialModelDto>> ToDtosAsync(
        IReadOnlyList<CavePotentialModel> rows,
        AccessContext ctx,
        UserContext user,
        SilexGisDbContext db,
        IAccessService access,
        CancellationToken ct)
    {
        var labels = await ProfileDirectory.ResolveLabelsAsync(db, user, rows.Select(r => r.OwnerUserId), ct);

        var dtos = new List<CavePotentialModelDto>(rows.Count);
        foreach (var row in rows)
        {
            // Only a row the author rule already admits pays for the permission read; everybody
            // else's answer is "no" without one.
            var effective = CavePotentialModelRules.MayChange(ctx, row)
                ? await access.EffectiveAsync(ctx, row, ct)
                : AccessAction.None;
            var definition = CavePotentialModelDefinition.Deserialize(row.Definition);
            var valid = definition is not null
                && CavePotentialModelRules.Problems(row.Method, definition).Count == 0;
            dtos.Add(new CavePotentialModelDto(
                row.Id,
                row.Name,
                row.Description,
                row.Method,
                definition,
                valid ? ToDto(CavePotentialModelRules.Weighting(row.Method, definition!)) : null,
                definition is not null && CavePotentialModelRules.UsesPositions(definition),
                row.CavingGroupId,
                row.Visibility,
                row.OwnerUserId == ctx.UserId,
                labels.GetValueOrDefault(row.OwnerUserId),
                effective.HasFlag(AccessAction.Write),
                effective.HasFlag(AccessAction.Delete),
                row.CreatedAt,
                row.UpdatedAt));
        }

        return dtos;
    }

    internal static CavePotentialWeightingDto ToDto(CavePotentialWeighting weighting) => new(
        [.. weighting.Weights.Select(w => new EvidenceLayerWeightDto(w.Layer, w.Weight))],
        weighting.Consistency is { } c
            ? new PairwiseConsistencyDto(c.PrincipalEigenvalue, c.Index, c.RandomIndex, c.Ratio, c.Acceptable)
            : null);
}
