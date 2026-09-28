// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import.TrackingCsv;
using SilexGis.Domain.Import.TripCsv;

namespace SilexGis.Api.Features.TripTracking;

/// <summary>
/// How the caller wants a sheet read: which header is which field, and the shape of the file.
/// </summary>
/// <param name="Columns">
/// Field name to header, for the fields the reviewer pointed at by hand. A field left out is
/// detected from the header row instead, under the spellings the installation ships with.
/// </param>
public sealed record TrackingCsvImportOptionsDto(
    IReadOnlyDictionary<string, string>? Columns,
    string? Delimiter,
    string? MultiValueSeparators,
    TripCsvDateOrder? DateOrder,
    IReadOnlyList<string>? WentInWords,
    IReadOnlyList<string>? CameOutWords);

/// <summary>A sheet to read, and how to read it.</summary>
public sealed record TrackingCsvImportRequest(
    string? Text,
    TrackingCsvImportOptionsDto? Options)
{
    /// <summary>
    /// A bound on the sheet, not on the import.
    /// </summary>
    /// <remarks>
    /// The text arrives in the request rather than as a stored upload, because a tracking sheet is
    /// read, reviewed and committed in one sitting: there is no review to resume, and staging a
    /// parse would be wrong the moment the reviewer changed which column is the moment. A megabyte
    /// is some tens of thousands of reports, which is more than a season of trips.
    /// </remarks>
    public const int MaxTextLength = 1_000_000;
}

public sealed class TrackingCsvImportRequestValidator : AbstractValidator<TrackingCsvImportRequest>
{
    public TrackingCsvImportRequestValidator()
    {
        RuleFor(x => x.Text).NotEmpty().MaximumLength(TrackingCsvImportRequest.MaxTextLength);
        RuleFor(x => x.Options!.Delimiter!).Length(1)
            .When(x => x.Options?.Delimiter is not null);
        RuleFor(x => x.Options!.MultiValueSeparators!).MaximumLength(8)
            .When(x => x.Options?.MultiValueSeparators is not null);
        RuleForEach(x => x.Options!.Columns!.Values).NotEmpty().MaximumLength(200)
            .When(x => x.Options?.Columns is not null);
        RuleForEach(x => x.Options!.WentInWords!).NotEmpty().MaximumLength(100)
            .When(x => x.Options?.WentInWords is not null);
        RuleForEach(x => x.Options!.CameOutWords!).NotEmpty().MaximumLength(100)
            .When(x => x.Options?.CameOutWords is not null);
    }
}

/// <summary>Committing a read sheet: the same text and choices, plus what may be done with it.</summary>
/// <param name="ReplaceExisting">
/// Whether a report the log already holds for the same person at the same instant may be
/// overwritten. Off by default: re-importing a corrected sheet is the intended use, and it is
/// also the one that silently changes history, so the reviewer says so.
/// </param>
/// <param name="Lines">
/// The physical lines to commit, or null for every importable one. A reviewer who has read a
/// preview commits what they read, and naming the lines is what makes that exact.
/// </param>
public sealed record TrackingCsvCommitRequest(
    string? Text,
    TrackingCsvImportOptionsDto? Options,
    bool ReplaceExisting,
    IReadOnlyList<int>? Lines);

public sealed class TrackingCsvCommitRequestValidator : AbstractValidator<TrackingCsvCommitRequest>
{
    public TrackingCsvCommitRequestValidator()
    {
        RuleFor(x => x.Text).NotEmpty().MaximumLength(TrackingCsvImportRequest.MaxTextLength);
        RuleFor(x => x.Lines!.Count).LessThanOrEqualTo(20_000).When(x => x.Lines is not null);
    }
}

/// <summary>One thing worth saying about a sheet or one of its rows, as a page can show it.</summary>
public sealed record TrackingCsvDiagnosticDto(
    string Severity,
    string Problem,
    int Line,
    string? Column,
    string? Detail);

/// <summary>One report a row would become, with what it matched.</summary>
public sealed record TrackingCsvPreviewRowDto(
    int Line,
    DateTimeOffset RecordedAt,
    Guid CaverId,
    string CaverWritten,
    string CaverMatched,
    string MatchedBy,
    Guid? TeamId,
    TripPositionEventKind Kind,
    string? StationName,
    decimal? DepthM,
    string? Note,
    bool Replaces,
    IReadOnlyList<TrackingCsvDiagnosticDto> Diagnostics);

/// <summary>What reading a sheet against this trip found.</summary>
/// <param name="Header">The header as written, so a mapping screen can offer the real spellings.</param>
/// <param name="ResolvedColumns">Field name to the header it was read from.</param>
/// <param name="UnmappedColumns">Headers nothing claimed — usually a misspelling.</param>
/// <param name="UnmatchedCavers">Written names nobody on the trip answered to, each once.</param>
public sealed record TrackingCsvPreviewDto(
    IReadOnlyList<string> Header,
    IReadOnlyDictionary<string, string> ResolvedColumns,
    IReadOnlyList<string> UnmappedColumns,
    TripCsvDateOrder DateOrder,
    string DateOrderSource,
    int RowsRead,
    int Creates,
    int Replaces,
    IReadOnlyList<string> UnmatchedCavers,
    IReadOnlyList<TrackingCsvPreviewRowDto> Rows,
    IReadOnlyList<TrackingCsvDiagnosticDto> FileDiagnostics,
    IReadOnlyList<TrackingCsvDiagnosticDto> Refused);

/// <summary>What committing a sheet did.</summary>
public sealed record TrackingCsvCommitDto(
    int Created,
    int Updated,
    int Skipped,
    IReadOnlyList<TrackingCsvDiagnosticDto> Refused);

/// <summary>The fields a sheet can carry, so a mapping screen need not hard-code them.</summary>
public sealed record TrackingCsvFieldDto(
    string Field,
    IReadOnlyList<string> Candidates);
