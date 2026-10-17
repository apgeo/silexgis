// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Features;

namespace SilexGis.Api.Features.Taxonomies;

/// <summary>A feature kind as an administrator asks for it to be.</summary>
public sealed record FeatureTypeRequest(
    string Code,
    string Name,
    string? Description,
    int SortOrder,
    FeatureCategory Category,
    IReadOnlyList<GeometryClass> AcceptedGeometryClasses,
    bool RequiresParent,
    ProtectedDisplay ProtectedDisplay,
    string? SymbolFile,
    string? Style,
    string? PropertiesSchema);

public sealed class FeatureTypeRequestValidator : AbstractValidator<FeatureTypeRequest>
{
    public FeatureTypeRequestValidator()
    {
        // Codes are the natural key installations exchange features by, so they stay to the
        // characters that survive a URL, a file name and a spreadsheet unharmed.
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50)
            .Matches("^[a-z0-9_]+$")
            .WithMessage("The code may contain lower-case letters, digits and underscores.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.ProtectedDisplay).IsInEnum();
        // A kind that accepts no shape could never hold a feature.
        RuleFor(x => x.AcceptedGeometryClasses).NotNull().NotEmpty();
        RuleForEach(x => x.AcceptedGeometryClasses).IsInEnum();
        // A file name inside the bundled symbol set, never a path or an address: the browser
        // loads it from beside the application, and a path would let a kind point it elsewhere.
        RuleFor(x => x.SymbolFile).MaximumLength(100)
            .Matches(@"^[a-z0-9_\-]+\.(png|svg)$")
            .When(x => !string.IsNullOrWhiteSpace(x.SymbolFile))
            .WithMessage("The symbol is a file name from the bundled set, such as pit.png.");
        // Whether the texts are a usable style and schema is decided on the write path; this only
        // keeps an unbounded blob out of a jsonb column.
        RuleFor(x => x.Style).MaximumLength(8_000);
        RuleFor(x => x.PropertiesSchema).MaximumLength(64_000);
    }
}

/// <summary>
/// Writing the feature-kind vocabulary: the kinds that ship and those an installation adds.
/// Reading it stays where every account reads it (<c>GET /feature-types</c>). Writing needs
/// taxonomy rights, which by default only a full administrator holds, and every change is audited
/// with the row — a kind's protected display decides how protected features of that kind are
/// shown to people without exact view.
/// </summary>
public static class FeatureTypeEndpoints
{
    public static RouteGroupBuilder MapFeatureTypeEndpoints(this RouteGroupBuilder api)
    {
        var types = api.MapGroup("/feature-types").WithTags("Taxonomies");

        types.MapPost("/", CreateAsync)
            .WithValidation<FeatureTypeRequest>()
            .WithSummary("Adds a feature type.");
        types.MapPut("/{id:long}", UpdateAsync)
            .WithValidation<FeatureTypeRequest>()
            .WithSummary("Updates a feature type; a shipped one keeps its code, category, protected display and need for a parent.");
        types.MapDelete("/{id:long}", DeleteAsync)
            .WithSummary("Deletes a feature type an installation added and no feature uses; shipped ones cannot be deleted.");

        return api;
    }

    private static async Task<Results<Created<FeatureTypeDto>, ProblemHttpResult>> CreateAsync(
        FeatureTypeRequest request,
        FeatureTypeWriteService writer,
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
            return TypedResults.Created($"/api/v1/feature-types/{row.Id}", ToDto(row));
        }
        catch (FeatureTypeWriteException e)
        {
            return ToProblem(e);
        }
    }

    private static async Task<Results<Ok<FeatureTypeDto>, ProblemHttpResult>> UpdateAsync(
        long id,
        FeatureTypeRequest request,
        FeatureTypeWriteService writer,
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
        catch (FeatureTypeWriteException e)
        {
            return ToProblem(e);
        }
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        long id,
        FeatureTypeWriteService writer,
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
        catch (FeatureTypeWriteException e)
        {
            return ToProblem(e, seededIsConflict: true);
        }
    }

    // As for trip purposes: an edit that only wanted a different code could have been written
    // another way, while deleting a kind that ships with the software conflicts with what it is.
    private static ProblemHttpResult ToProblem(FeatureTypeWriteException e, bool seededIsConflict = false) => e.Code switch
    {
        FeatureTypeWriteService.NotFoundCode => ApiProblems.NotFound(e.Code),
        FeatureTypeWriteService.InUseCode => ApiProblems.Conflict(e.Code, e.Message),
        FeatureTypeWriteService.SeededImmutableCode when seededIsConflict => ApiProblems.Conflict(e.Code, e.Message),
        _ => ApiProblems.BadRequest(e.Code, e.Message),
    };

    private static FeatureTypeInput ToInput(FeatureTypeRequest request) => new(
        request.Code,
        request.Name,
        request.Description,
        request.SortOrder,
        request.Category,
        request.AcceptedGeometryClasses,
        request.RequiresParent,
        request.ProtectedDisplay,
        request.SymbolFile,
        request.Style,
        request.PropertiesSchema);

    internal static FeatureTypeDto ToDto(FeatureType t) => new(
        t.Id, t.Code, t.Name, t.Description, t.SortOrder,
        t.Category, t.AcceptedGeometryClasses, t.RequiresParent, t.PropertiesSchemaVersion,
        t.ProtectedDisplay, t.SymbolFile, t.Style, t.PropertiesSchema,
        FeatureTypeWriteService.IsShipped(t.Code));
}
