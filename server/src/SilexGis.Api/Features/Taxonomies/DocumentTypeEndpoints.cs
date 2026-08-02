// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Taxonomies;

/// <summary>
/// A kind of document, and the JSON Schema documents of that kind describe themselves with.
/// The raw schema text is served so a client can build the metadata form from it rather
/// than shipping a form per kind; <c>MetadataSchemaVersion</c> is what a document stamps
/// when its metadata is validated.
/// </summary>
public sealed record DocumentTypeDto(
    long Id, string Code, string Name, string? Description, int SortOrder,
    int MetadataSchemaVersion, string? MetadataSchema);

/// <summary>A document kind as an editor asks for it to be. A blank schema means "no schema".</summary>
public sealed record DocumentTypeRequest(
    string Code, string Name, string? Description, int SortOrder, string? MetadataSchema);

public sealed class DocumentTypeRequestValidator : AbstractValidator<DocumentTypeRequest>
{
    public DocumentTypeRequestValidator()
    {
        // Codes are the natural key installations exchange rows by, so they stay to the
        // characters that survive a URL, a file name and a spreadsheet unharmed.
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50)
            .Matches("^[a-z0-9_]+$")
            .WithMessage("The code may contain lower-case letters, digits and underscores.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(1000);

        // Whether the text is a usable JSON Schema is decided by the schema engine in the
        // write path; this only keeps an unbounded blob out of a jsonb column.
        RuleFor(x => x.MetadataSchema).MaximumLength(64_000);
    }
}

/// <summary>
/// The document-kind registry. Reading is open to every account through the taxonomy read
/// that the All Users seed carries; editing needs taxonomy rights, which by default only a
/// full administrator holds — a metadata schema decides what every document of that kind is
/// allowed to say, so it is administration, not content.
/// </summary>
public static class DocumentTypeEndpoints
{
    public static RouteGroupBuilder MapDocumentTypeEndpoints(this RouteGroupBuilder api)
    {
        var types = api.MapGroup("/document-types").WithTags("Taxonomies");

        types.MapGet("/", ListAsync)
            .WithSummary("Document kinds and the metadata schema each one publishes.");
        types.MapPost("/", CreateAsync)
            .WithValidation<DocumentTypeRequest>()
            .WithSummary("Adds a document kind.");
        types.MapPut("/{id:long}", UpdateAsync)
            .WithValidation<DocumentTypeRequest>()
            .WithSummary("Updates a document kind; a changed metadata schema publishes a new version.");

        return api;
    }

    private static async Task<Results<Ok<List<DocumentTypeDto>>, ProblemHttpResult>> ListAsync(
        SilexGisDbContext db, IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null || !AccessEvaluator.Decide(ctx, AccessDomain.Taxonomies, AccessAction.Read, null).Allowed)
        {
            return ApiProblems.Forbidden();
        }

        return TypedResults.Ok(await db.DocumentTypes.AsNoTracking()
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Id)
            .Select(t => new DocumentTypeDto(
                t.Id, t.Code, t.Name, t.Description, t.SortOrder, t.MetadataSchemaVersion, t.MetadataSchema))
            .ToListAsync(ct));
    }

    private static async Task<Results<Created<DocumentTypeDto>, ProblemHttpResult>> CreateAsync(
        DocumentTypeRequest request,
        DocumentTypeWriteService types,
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
            var type = await types.CreateAsync(ToInput(request), ct);
            return TypedResults.Created($"/api/v1/document-types/{type.Id}", ToDto(type));
        }
        catch (DocumentWriteException e)
        {
            return ToProblem(e);
        }
    }

    private static async Task<Results<Ok<DocumentTypeDto>, ProblemHttpResult>> UpdateAsync(
        long id,
        DocumentTypeRequest request,
        DocumentTypeWriteService types,
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
            return TypedResults.Ok(ToDto(await types.UpdateAsync(id, ToInput(request), ct)));
        }
        catch (DocumentWriteException e)
        {
            return ToProblem(e);
        }
    }

    private static ProblemHttpResult ToProblem(DocumentWriteException e) => e.Code switch
    {
        DocumentTypeWriteService.NotFoundCode => ApiProblems.NotFound(e.Code),
        DocumentTypeWriteService.CodeTakenCode => ApiProblems.Conflict(e.Code, e.Message),
        _ => ApiProblems.BadRequest(e.Code, e.Message),
    };

    private static DocumentTypeInput ToInput(DocumentTypeRequest request) =>
        new(request.Code, request.Name, request.Description, request.SortOrder, request.MetadataSchema);

    private static DocumentTypeDto ToDto(Domain.Entities.DocumentType type) =>
        new(type.Id, type.Code, type.Name, type.Description, type.SortOrder,
            type.MetadataSchemaVersion, type.MetadataSchema);
}
