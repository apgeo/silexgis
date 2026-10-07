// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Taxonomies;

/// <summary>One club-written layout for the document a trip or a camp is written up as.</summary>
public sealed record ReportTemplateDto(
    Guid Id,
    string Name,
    ReportTemplateKind Kind,
    string Body,
    bool IsDefault,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long? TripTypeId = null);

/// <summary>A layout as somebody asks for it to be.</summary>
/// <param name="Kind">
/// Which kind of thing this layout writes up. Required, and deliberately not defaulted: the kind
/// decides which vocabulary the body is read under and which write-ups may use the layout. A
/// caller that omitted it would have one chosen for them — and because this is a full-replace
/// write, the chosen one would be stamped over the kind the stored layout already had, quietly
/// retyping a camp layout as a trip layout and taking the trip default with it.
/// </param>
/// <param name="TripTypeId">
/// The trip purpose this layout is the write-up of: a trip of that purpose is built in it unless
/// another is named. Appended with a default so a caller that does not know of it saves a layout
/// for any trip, which is what every layout was before.
/// </param>
public sealed record ReportTemplateRequest(
    string Name, string Body, bool IsDefault, ReportTemplateKind? Kind, long? TripTypeId = null);

public sealed class ReportTemplateRequestValidator : AbstractValidator<ReportTemplateRequest>
{
    public ReportTemplateRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        // Asked for rather than assumed. See the request's own note: a missing kind that fell back
        // to a default would rewrite the kind of the layout being saved.
        RuleFor(x => x.Kind).NotNull().IsInEnum();
        // A camp has no purpose to be bound to; a trip-type binding on a camp layout would never
        // be read and would sit there looking like a choice.
        RuleFor(x => x.TripTypeId)
            .Null()
            .When(x => x.Kind != ReportTemplateKind.Trip)
            .WithMessage("Only a trip layout can be bound to a trip purpose.");
        // Whether the body is a layout anything can be built from is decided by reading it, which
        // happens in the handler so the refusal can say which line is wrong. This only keeps an
        // unbounded blob out of the column.
        RuleFor(x => x.Body).NotEmpty().MaximumLength(ReportTemplateFormat.MaxLength);
    }
}

/// <summary>
/// The layouts a club writes its write-ups in — a trip's and a camp's, told apart by their kind.
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
public static class ReportTemplateEndpoints
{
    /// <summary>A layout that could not be read, with the faults and their line numbers in the detail.</summary>
    public const string InvalidCode = "report_template.invalid";

    /// <summary>
    /// A layout that is not there. Shared with the read that resolves a layout for a write-up, so
    /// a caller sees one code whether it named a layout that never existed or one of another kind.
    /// </summary>
    public const string NotFoundCode = ReportTemplateReads.NotFoundCode;

    /// <summary>A word that is not one of the kinds of thing a layout writes up.</summary>
    public const string KindInvalidCode = "report_template.kind_invalid";

    /// <summary>A layout bound to a trip purpose that does not exist.</summary>
    public const string TypeUnknownCode = "report_template.type_unknown";

    /// <summary>A layout bound to a trip purpose another layout is already the own layout of.</summary>
    public const string TypeTakenCode = "report_template.type_taken";

    public static RouteGroupBuilder MapReportTemplateEndpoints(this RouteGroupBuilder api)
    {
        var templates = api.MapGroup("/report-templates").WithTags("Taxonomies");

        templates.MapGet("/", ListAsync)
            .WithSummary(
                "The layouts write-ups may be built in, and which one is used by default. "
                + "Narrowed by what a layout writes up when a kind is named.");
        templates.MapGet("/default", DefaultAsync)
            .WithSummary(
                "The layout the system ships for the named kind, as a file to edit and upload "
                + "back. It documents that kind's whole vocabulary in its own comments.");
        templates.MapPost("/", CreateAsync)
            .WithValidation<ReportTemplateRequest>()
            .WithSummary("Stores a layout, refusing one whose lines cannot be read.");
        templates.MapPut("/{id:guid}", UpdateAsync)
            .WithValidation<ReportTemplateRequest>()
            .WithSummary("Rewrites a layout, refusing one whose lines cannot be read.");
        templates.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Removes a layout; write-ups fall back to the one the system ships.");

        return api;
    }

    /// <summary>
    /// Which kind a caller named, or null for "all of them" where that is allowed.
    /// </summary>
    /// <remarks>
    /// Parsed here rather than by route binding, the way every other word-valued filter in this
    /// application is: binding a bad word answers with a bare 400 carrying no code, and a client
    /// cannot tell that apart from any other refusal. A word this application does not have is a
    /// refusal rather than "no filter" — somebody who asked for something specific wants to be
    /// told, not handed everything.
    /// </remarks>
    private static bool TryReadKind(string? kind, out ReportTemplateKind? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(kind))
        {
            return true;
        }

        if (!RouteEnums.TryParse<ReportTemplateKind>(kind, out var value))
        {
            return false;
        }

        parsed = value;
        return true;
    }

    private static async Task<Results<Ok<List<ReportTemplateDto>>, ProblemHttpResult>> ListAsync(
        string? kind,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null || !AccessEvaluator.Decide(ctx, AccessDomain.Taxonomies, AccessAction.Read, null).Allowed)
        {
            return ApiProblems.Forbidden();
        }

        if (!TryReadKind(kind, out var wanted))
        {
            return ApiProblems.BadRequest(KindInvalidCode, $"Unknown layout kind '{kind}'.");
        }

        var rows = await db.ReportTemplates.AsNoTracking()
            .Where(x => wanted == null || x.Kind == wanted)
            .OrderBy(x => x.Kind).ThenBy(x => x.Name).ThenBy(x => x.Id)
            .ToListAsync(ct);
        return TypedResults.Ok(rows.Select(ToDto).ToList());
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> DefaultAsync(
        string? kind, IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null || !AccessEvaluator.Decide(ctx, AccessDomain.Taxonomies, AccessAction.Read, null).Allowed)
        {
            return ApiProblems.Forbidden();
        }

        if (!TryReadKind(kind, out var named))
        {
            return ApiProblems.BadRequest(KindInvalidCode, $"Unknown layout kind '{kind}'.");
        }

        // Written out as UTF-8 with a byte-order mark: this file is opened in whatever text
        // editor is to hand, and the one most likely to be to hand on Windows reads a file
        // without one as the machine's own code page and mangles every accented word in it.
        var wanted = named ?? ReportTemplateKind.Trip;
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
            .GetBytes(ReportTemplateFormat.DefaultFor(wanted)
                .Replace("\n", "\r\n", StringComparison.Ordinal));
        var name = wanted == ReportTemplateKind.Expedition
            ? "expedition-report-template.txt"
            : "trip-report-template.txt";
        return TypedResults.File(bytes, "text/plain; charset=utf-8", name);
    }

    private static async Task<Results<Created<ReportTemplateDto>, ProblemHttpResult>> CreateAsync(
        ReportTemplateRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (!CreateRules.MayCreate(ctx, AccessDomain.Taxonomies))
        {
            return ApiProblems.Forbidden(CreateRules.ForbiddenCode);
        }

        // Present because the validator filter runs before this and requires it; a body naming
        // none is a 400 and never arrives here.
        var kind = request.Kind!.Value;
        if (Refusal(request.Body, kind) is { } refusal)
        {
            return refusal;
        }

        if (await TypeRefusalAsync(db, request.TripTypeId, null, ct) is { } typeRefusal)
        {
            return typeRefusal;
        }

        var row = new ReportTemplate
        {
            Name = request.Name.Trim(), Body = request.Body, Kind = kind, TripTypeId = request.TripTypeId,
        };
        db.ReportTemplates.Add(row);
        await SaveWithDefaultAsync(db, row, request.IsDefault, ct);
        return TypedResults.Created($"/api/v1/report-templates/{row.Id}", ToDto(row));
    }

    private static async Task<Results<Ok<ReportTemplateDto>, ProblemHttpResult>> UpdateAsync(
        Guid id,
        ReportTemplateRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null || !AccessEvaluator.Decide(ctx, AccessDomain.Taxonomies, AccessAction.Write, null).Allowed)
        {
            return ApiProblems.Forbidden();
        }

        var row = await db.ReportTemplates.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        var kind = request.Kind!.Value;
        if (Refusal(request.Body, kind) is { } refusal)
        {
            return refusal;
        }

        if (await TypeRefusalAsync(db, request.TripTypeId, row.Id, ct) is { } typeRefusal)
        {
            return typeRefusal;
        }

        row.Name = request.Name.Trim();
        row.Body = request.Body;
        row.Kind = kind;
        row.TripTypeId = request.TripTypeId;
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

        var row = await db.ReportTemplates.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        db.ReportTemplates.Remove(row);
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
        SilexGisDbContext db, ReportTemplate row, bool isDefault, CancellationToken ct)
    {
        if (!isDefault)
        {
            row.IsDefault = false;
            await db.SaveChangesAsync(ct);
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var held = await db.ReportTemplates
            .Where(x => x.IsDefault && x.Kind == row.Kind && x.Id != row.Id)
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

    /// <summary>
    /// A binding to a purpose that is not there, or to one another layout already holds, is
    /// refused with a code rather than left to the foreign key and the unique index — which would
    /// answer the same mistakes as a server fault.
    /// </summary>
    private static async Task<ProblemHttpResult?> TypeRefusalAsync(
        SilexGisDbContext db, long? tripTypeId, Guid? exceptId, CancellationToken ct)
    {
        if (tripTypeId is not { } typeId)
        {
            return null;
        }

        if (!await db.TripTypes.AsNoTracking().AnyAsync(t => t.Id == typeId, ct))
        {
            return ApiProblems.BadRequest(TypeUnknownCode, "There is no such trip purpose.");
        }

        if (await db.ReportTemplates.AsNoTracking().AnyAsync(x => x.TripTypeId == typeId && x.Id != exceptId, ct))
        {
            return ApiProblems.Conflict(TypeTakenCode, "Another layout is already this trip purpose's own.");
        }

        return null;
    }

    private static ProblemHttpResult? Refusal(string body, ReportTemplateKind kind)
    {
        var read = ReportTemplateFormat.Parse(body, kind);
        return read.Ok ? null : ApiProblems.BadRequest(InvalidCode, string.Join(" ", read.Errors));
    }

    private static ReportTemplateDto ToDto(ReportTemplate row) =>
        new(row.Id, row.Name, row.Kind, row.Body, row.IsDefault, row.CreatedAt, row.UpdatedAt, row.TripTypeId);
}
