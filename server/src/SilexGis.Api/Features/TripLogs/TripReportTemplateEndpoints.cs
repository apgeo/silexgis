// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.TripLogs;

/// <summary>One club-written layout for the document a trip is written up as.</summary>
public sealed record TripReportTemplateDto(
    Guid Id,
    string Name,
    string Body,
    bool IsDefault,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>A layout as somebody asks for it to be.</summary>
public sealed record TripReportTemplateRequest(string Name, string Body, bool IsDefault);

public sealed class TripReportTemplateRequestValidator : AbstractValidator<TripReportTemplateRequest>
{
    public TripReportTemplateRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        // Whether the body is a layout anything can be built from is decided by reading it, which
        // happens in the handler so the refusal can say which line is wrong. This only keeps an
        // unbounded blob out of the column.
        RuleFor(x => x.Body).NotEmpty().MaximumLength(ReportTemplateFormat.MaxLength);
    }
}

/// <summary>
/// The layouts a club writes its trip write-ups in.
/// </summary>
/// <remarks>
/// <para>
/// The system hands out a layout to start from; somebody downloads it, edits it in a text
/// editor, uploads it back and says which one write-ups should use. Reading the list is open to
/// every account through the same vocabulary right every trip screen already needs, because
/// anybody producing a write-up has to be able to choose between the layouts; writing one needs
/// the right to edit the installation's vocabularies, which by default only a full administrator
/// holds.
/// </para>
/// <para>
/// A layout is read when it is written, not when a document is produced. A club secretary finds
/// out a line is wrong while the file is still open in front of them, rather than discovering it
/// as a failure on the evening the bulletin is due.
/// </para>
/// </remarks>
public static class TripReportTemplateEndpoints
{
    /// <summary>A layout that could not be read, with the faults and their line numbers in the detail.</summary>
    public const string InvalidCode = "trip_report_template.invalid";

    public const string NotFoundCode = "trip_report_template.not_found";

    public static RouteGroupBuilder MapTripReportTemplateEndpoints(this RouteGroupBuilder api)
    {
        var templates = api.MapGroup("/trip-report-templates").WithTags("TripLogs");

        templates.MapGet("/", ListAsync)
            .WithSummary("The layouts write-ups may be built in, and which one is used by default.");
        templates.MapGet("/default", DefaultAsync)
            .WithSummary(
                "The layout the system ships, as a file to edit and upload back. It documents the "
                + "whole vocabulary in its own comments.");
        templates.MapPost("/", CreateAsync)
            .WithValidation<TripReportTemplateRequest>()
            .WithSummary("Stores a layout, refusing one whose lines cannot be read.");
        templates.MapPut("/{id:guid}", UpdateAsync)
            .WithValidation<TripReportTemplateRequest>()
            .WithSummary("Rewrites a layout, refusing one whose lines cannot be read.");
        templates.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Removes a layout; write-ups fall back to the one the system ships.");

        return api;
    }

    /// <summary>
    /// The layout a write-up should be built in: the one asked for, the one chosen as default, or
    /// the one the product ships.
    /// </summary>
    /// <remarks>
    /// Returns null when a layout was named and there is no such row, so the caller can refuse
    /// rather than quietly produce a document in a layout nobody asked for.
    /// </remarks>
    internal static async Task<string?> BodyForAsync(
        SilexGisDbContext db, Guid? templateId, CancellationToken ct)
    {
        if (templateId is { } id)
        {
            return await db.TripReportTemplates.AsNoTracking()
                .Where(x => x.Id == id).Select(x => x.Body).FirstOrDefaultAsync(ct);
        }

        var chosen = await db.TripReportTemplates.AsNoTracking()
            .Where(x => x.IsDefault).Select(x => x.Body).FirstOrDefaultAsync(ct);
        return chosen ?? ReportTemplateFormat.Default;
    }

    private static async Task<Results<Ok<List<TripReportTemplateDto>>, ProblemHttpResult>> ListAsync(
        SilexGisDbContext db, IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null || !AccessEvaluator.Decide(ctx, AccessDomain.Taxonomies, AccessAction.Read, null).Allowed)
        {
            return ApiProblems.Forbidden();
        }

        var rows = await db.TripReportTemplates.AsNoTracking()
            .OrderBy(x => x.Name).ThenBy(x => x.Id)
            .ToListAsync(ct);
        return TypedResults.Ok(rows.Select(ToDto).ToList());
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> DefaultAsync(
        IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null || !AccessEvaluator.Decide(ctx, AccessDomain.Taxonomies, AccessAction.Read, null).Allowed)
        {
            return ApiProblems.Forbidden();
        }

        // Written out as UTF-8 with a byte-order mark: this file is opened in whatever text
        // editor is to hand, and the one most likely to be to hand on Windows reads a file
        // without one as the machine's own code page and mangles every accented word in it.
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
            .GetBytes(ReportTemplateFormat.Default.Replace("\n", "\r\n", StringComparison.Ordinal));
        return TypedResults.File(bytes, "text/plain; charset=utf-8", "trip-report-template.txt");
    }

    private static async Task<Results<Created<TripReportTemplateDto>, ProblemHttpResult>> CreateAsync(
        TripReportTemplateRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (!CreateRules.MayCreate(ctx, AccessDomain.Taxonomies))
        {
            return ApiProblems.Forbidden(CreateRules.ForbiddenCode);
        }

        if (Refusal(request.Body) is { } refusal)
        {
            return refusal;
        }

        var row = new TripReportTemplate { Name = request.Name.Trim(), Body = request.Body };
        db.TripReportTemplates.Add(row);
        await SaveWithDefaultAsync(db, row, request.IsDefault, ct);
        return TypedResults.Created($"/api/v1/trip-report-templates/{row.Id}", ToDto(row));
    }

    private static async Task<Results<Ok<TripReportTemplateDto>, ProblemHttpResult>> UpdateAsync(
        Guid id,
        TripReportTemplateRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null || !AccessEvaluator.Decide(ctx, AccessDomain.Taxonomies, AccessAction.Write, null).Allowed)
        {
            return ApiProblems.Forbidden();
        }

        var row = await db.TripReportTemplates.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (Refusal(request.Body) is { } refusal)
        {
            return refusal;
        }

        row.Name = request.Name.Trim();
        row.Body = request.Body;
        await SaveWithDefaultAsync(db, row, request.IsDefault, ct);
        return TypedResults.Ok(ToDto(row));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id, SilexGisDbContext db, IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null || !AccessEvaluator.Decide(ctx, AccessDomain.Taxonomies, AccessAction.Write, null).Allowed)
        {
            return ApiProblems.Forbidden();
        }

        var row = await db.TripReportTemplates.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        db.TripReportTemplates.Remove(row);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Saves the row, moving the "used when nobody names one" mark onto it if asked.
    /// </summary>
    /// <remarks>
    /// The mark is unique in the database, so whatever held it has to be let go in its own write
    /// before this row can take it — a single save would present the database with two rows
    /// claiming it at the same instant. Both writes are one transaction, so an interruption
    /// between them cannot leave an installation with no chosen layout.
    /// </remarks>
    private static async Task SaveWithDefaultAsync(
        SilexGisDbContext db, TripReportTemplate row, bool isDefault, CancellationToken ct)
    {
        if (!isDefault)
        {
            row.IsDefault = false;
            await db.SaveChangesAsync(ct);
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var held = await db.TripReportTemplates
            .Where(x => x.IsDefault && x.Id != row.Id)
            .ToListAsync(ct);
        foreach (var other in held)
        {
            other.IsDefault = false;
        }

        row.IsDefault = false;
        await db.SaveChangesAsync(ct);

        row.IsDefault = true;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private static ProblemHttpResult? Refusal(string body)
    {
        var read = ReportTemplateFormat.Parse(body);
        return read.Ok ? null : ApiProblems.BadRequest(InvalidCode, string.Join(" ", read.Errors));
    }

    private static TripReportTemplateDto ToDto(TripReportTemplate row) =>
        new(row.Id, row.Name, row.Body, row.IsDefault, row.CreatedAt, row.UpdatedAt);
}
