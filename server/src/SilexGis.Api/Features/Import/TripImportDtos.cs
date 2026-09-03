// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Domain.Import;
using SilexGis.Domain.Import.TripCsv;

namespace SilexGis.Api.Features.Import;

/// <summary>
/// Something worth telling the reviewer about, tied to the physical line of the file it came
/// from. The line is counted in the file rather than in the rows that survived, because a blank
/// line or a value carrying a newline shifts the two apart and a number that does not point at
/// what the reviewer sees in their editor is worse than no number. A line of 0 is about the file
/// as a whole.
/// </summary>
public sealed record TripImportProblemDto(
    int Line,
    TripCsvSeverity Severity,
    TripCsvDiagnosticCode Code,
    TripCsvField? Field,
    string? Column,
    string? Detail);

/// <summary>
/// One row of the review table, read but not yet resolved: everything here is text, a date or a
/// list of names as the sheet wrote them.
///
/// <para>
/// A row carrying an error is never one of these. It is reported among the file's problems
/// instead, because a row offered for selection that cannot be created produces one failure line
/// per row at confirmation and teaches the reviewer nothing they could not have been told first.
/// Warnings do appear here, on the row they were raised about: the row can still be imported and
/// the reviewer should see what was reconciled to make that true.
/// </para>
/// </summary>
public sealed record TripImportRowDto(
    int Line,
    string? SourceId,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? StartDateText,
    string? EndDateText,
    string? Title,
    string? Country,
    string? Massif,
    string? SubArea,
    IReadOnlyList<string> Caves,
    IReadOnlyList<string> Proposers,
    IReadOnlyList<string> Participants,
    string? Details,
    string? Details2,
    string? TripType,
    string? Errors,
    IReadOnlyDictionary<string, string> Unmapped,
    IReadOnlyList<TripImportProblemDto> Warnings,
    TripImportDecision? Decision,
    TripImportRowResolution? Resolution);

/// <summary>
/// What the sheet as a whole would do here: the distinct values it wrote with what each was taken
/// for, and how many of each kind of thing a confirmation would add.
///
/// <para>
/// The four tables are the answer to "what am I agreeing to", which the row table cannot give —
/// a type value written on four hundred rows is one new vocabulary term, and four hundred rows
/// saying so is not a review.
/// </para>
/// <para>
/// The counts of what a person still has to settle are stated apart from the counts of what a
/// confirmation would add, because a confirmation does not settle them. There are two kinds and
/// both must be said. A name more than one roster entry answers to waits for somebody to say
/// which — that is <c>ambiguousPersonCount</c>. A name nothing answers to and nobody can be
/// created from — an initial, or a lone given name — waits for nobody: it becomes no roster row
/// whatever the create switch says, and the person is recorded only in the trip's own words.
/// That is <c>uncreatablePersonCount</c>, and it is counted regardless of the switch because the
/// switch cannot change it. Leaving it out of the headline is how a sheet whose people cannot
/// all be recorded comes to read exactly like a sheet whose people can, right up until the trips
/// arrive with somebody missing from them.
/// </para>
/// <para>
/// Every count is present even at zero. A number that disappears when it is nothing reads exactly
/// like a number that was never worked out.
/// </para>
/// </summary>
public sealed record TripImportProposalsDto(
    IReadOnlyList<TripImportTermMatch> TripTypes,
    IReadOnlyList<TripImportPersonMatch> People,
    IReadOnlyList<TripImportFeatureMatch> Caves,
    IReadOnlyList<TripImportFeatureMatch> Areas,
    int NewTripTypeCount,
    int NewCaverCount,
    int NewCaveCount,
    int NewAreaCount,
    int AmbiguousPersonCount,
    int UncreatablePersonCount,
    int AmbiguousPlaceCount);

/// <summary>
/// What the sheet amounts to under the choices made so far, before anything is created.
///
/// <para>
/// <c>filteredLines</c> is every readable row matching the current filter. <c>selectableLines</c>
/// is the subset that could actually be recorded — nothing the reviewer has set aside — and it is
/// what "select all" selects. The server answers this rather than the browser, because a decision
/// made on the first page has to count for a row on the ninth.
/// </para>
/// <para>
/// <c>truncated</c> says the sheet holds more rows than one review reads, and <c>rowCount</c>
/// says how many were read; a silently shortened list reads exactly like a complete one.
/// </para>
/// <para>
/// <c>dateOrder</c> is the day/month order the whole file was read in and <c>dateOrderSource</c>
/// says what settled it; <c>ambiguousDateRows</c> counts the rows riding on that choice, in rows
/// rather than cells, because a reviewer choosing the order is judging how many trips it moves.
/// </para>
/// </summary>
public sealed record TripImportPreviewDto(
    IReadOnlyList<TripImportRowDto> Items,
    int Page,
    int PageSize,
    int TotalItems,
    IReadOnlyList<int> FilteredLines,
    IReadOnlyList<int> SelectableLines,
    bool Truncated,
    int RowCount,
    int ReadableRowCount,
    int FailedRowCount,
    int SkippedRowCount,
    IReadOnlyList<string> Header,
    IReadOnlyDictionary<TripCsvField, string> ResolvedColumns,
    IReadOnlyList<string> UnmappedColumns,
    TripCsvDateOrder DateOrder,
    TripCsvDateOrderSource DateOrderSource,
    int AmbiguousDateRows,
    IReadOnlyList<TripImportProblemDto> Problems,
    TripImportProposalsDto Proposals);

/// <summary>
/// The header of an uploaded sheet, and what the built-in spellings made of it. Read from the
/// stored file rather than from a parse, so it still answers after a parse produced no rows at
/// all — which is exactly when somebody needs to re-point a column.
/// </summary>
public sealed record TripImportColumnsDto(
    IReadOnlyList<string> Header,
    IReadOnlyDictionary<TripCsvField, string> ResolvedColumns,
    IReadOnlyList<string> UnmappedColumns,
    IReadOnlyList<TripImportProblemDto> Problems);

/// <summary>A review in progress, resumed where its reviewer left it.</summary>
public sealed record TripImportSessionDto(
    Guid FileId,
    string FileName,
    TripImportOptions Options,
    IReadOnlyDictionary<string, TripImportDecision> Decisions,
    DateTimeOffset? UpdatedAt);

/// <summary>Saves the review. Sent as the reviewer works, so a closed tab costs nothing.</summary>
public sealed record TripImportSessionWriteRequest(
    TripImportOptions Options,
    IReadOnlyDictionary<string, TripImportDecision> Decisions);

/// <summary>
/// Reads the sheet under a set of options and answers a page of it. A computation over a body,
/// so a POST.
///
/// <para>
/// The options travel with the request because changing one is a different question rather than a
/// stale answer to the old one. The decisions do not: they are read from the saved review, which
/// is the only place that holds the ones made on pages the request is not asking for.
/// </para>
/// </summary>
public sealed record TripImportPreviewRequest(
    TripImportOptions Options,
    int? Page,
    int? PageSize,
    string? Search);

/// <summary>
/// Confirms the review: the rows chosen, under the options they were reviewed under.
///
/// <para>
/// The options travel with the confirmation rather than being read from the saved review, so that
/// what is created is what the screen showed. Reading them from the row would let a second tab
/// left open on different choices decide what a thousand trips become.
/// </para>
/// <para>
/// <c>lines</c> are physical file lines, the same numbers the preview answers with. A line the
/// review set aside is left alone rather than refused: a selection assembled across nine pages
/// and a decision made on the first are two answers to two different questions, and the row is
/// not a failure for having both.
/// </para>
/// </summary>
public sealed record TripImportCommitRequest(
    TripImportOptions Options,
    IReadOnlyList<int> Lines,
    IReadOnlyDictionary<string, TripImportDecision>? Decisions);

/// <summary>One row that could not be recorded, with its line, its code and its reason.</summary>
public sealed record TripImportFailureDto(int Line, string? Title, string Code, string Reason);

/// <summary>
/// What the confirmation did. The failures are listed rather than counted: a number tells the
/// reviewer that something went wrong and nothing about which row to go and fix.
/// </summary>
public sealed record TripImportCommitResultDto(
    Guid BatchId,
    int CreatedTripCount,
    int CreatedFeatureCount,
    int SkippedCount,
    IReadOnlyList<TripImportFailureDto> Failures);

public sealed class TripImportOptionsValidator : AbstractValidator<TripImportOptions>
{
    public TripImportOptionsValidator()
    {
        RuleFor(x => x.Delimiter).NotEmpty().Length(1)
            .WithMessage("The field separator is a single character.");
        RuleFor(x => x.MultiValueSeparators).NotNull().MaximumLength(8);
        RuleFor(x => x.SlashSeparatedFields).NotNull();
        RuleFor(x => x.DateOrder).IsInEnum();
        RuleFor(x => x.Visibility).IsInEnum();
        RuleFor(x => x.Columns).NotNull();
        RuleFor(x => x.TripTypeChoices).NotNull();
        RuleFor(x => x.TripTypeNames).NotNull();
        RuleFor(x => x.CaverChoices).NotNull();
        RuleFor(x => x.FeatureChoices).NotNull();

        // A name a reviewer types for a new vocabulary term is a name every trip form will show,
        // so it is held to a length rather than taken as written.
        RuleFor(x => x.TripTypeNames)
            .Must(names => names is null || names.Values.All(n => n is null || n.Length <= 120))
            .WithMessage("A trip type's name is at most 120 characters.");
    }
}

public sealed class TripImportSessionWriteRequestValidator : AbstractValidator<TripImportSessionWriteRequest>
{
    public TripImportSessionWriteRequestValidator()
    {
        RuleFor(x => x.Options).NotNull().SetValidator(new TripImportOptionsValidator()!);
        RuleFor(x => x.Decisions).NotNull();
    }
}

public sealed class TripImportPreviewRequestValidator : AbstractValidator<TripImportPreviewRequest>
{
    public TripImportPreviewRequestValidator()
    {
        RuleFor(x => x.Options).NotNull().SetValidator(new TripImportOptionsValidator()!);
    }
}

public sealed class TripImportCommitRequestValidator : AbstractValidator<TripImportCommitRequest>
{
    public TripImportCommitRequestValidator()
    {
        RuleFor(x => x.Options).NotNull().SetValidator(new TripImportOptionsValidator()!);
        RuleFor(x => x.Lines).NotNull().Must(l => l is null || l.Count > 0)
            .WithMessage("Choose at least one row to record.");
    }
}
