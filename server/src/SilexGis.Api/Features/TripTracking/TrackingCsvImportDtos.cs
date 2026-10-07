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
/// <param name="TimeZone">
/// The zone the sheet's times were written in, as an IANA name ("Europe/Bucharest"), applied to
/// every cell that states no offset of its own. Left out, such a cell is read exactly as written,
/// as UTC. A cell that writes its offset is the instant it says under either choice.
/// </param>
/// <param name="Day">
/// The day a sheet that writes only times of day was kept on. Used for a sheet with a time column
/// and no date column, and there for the cells that write no date of their own; such a sheet is
/// refused without it. Ignored by every other layout — a sheet that writes its dates says its own
/// days.
/// </param>
public sealed record TrackingCsvImportOptionsDto(
    IReadOnlyDictionary<string, string>? Columns,
    string? Delimiter,
    string? MultiValueSeparators,
    TripCsvDateOrder? DateOrder,
    IReadOnlyList<string>? WentInWords,
    IReadOnlyList<string>? CameOutWords,
    string? TimeZone = null,
    DateOnly? Day = null);

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

/// <summary>
/// The rules on how a sheet may be asked to be read, held once because two requests carry the
/// same choices.
/// </summary>
/// <remarks>
/// The preview and the commit have to read the sheet under the same choices, or the reviewer
/// commits something they never saw: the reader drops a delimiter that is not one character and
/// reads with the default, so a commit that accepted ";;" after the preview refused it would write
/// a file previewed under no such choice. One validator, included by both requests, is what keeps
/// the two from drifting apart rule by rule.
/// </remarks>
public sealed class TrackingCsvImportOptionsDtoValidator : AbstractValidator<TrackingCsvImportOptionsDto>
{
    public TrackingCsvImportOptionsDtoValidator()
    {
        RuleFor(x => x.Delimiter!).Length(1).When(x => x.Delimiter is not null);
        RuleFor(x => x.MultiValueSeparators!).MaximumLength(8).When(x => x.MultiValueSeparators is not null);
        RuleForEach(x => x.Columns!.Values).NotEmpty().MaximumLength(200).When(x => x.Columns is not null);
        RuleForEach(x => x.WentInWords!).NotEmpty().MaximumLength(100).When(x => x.WentInWords is not null);
        RuleForEach(x => x.CameOutWords!).NotEmpty().MaximumLength(100).When(x => x.CameOutWords is not null);
        // A bound only. Whether the name is a zone at all is answered where the sheet is read, by
        // the one rule that also resolves it, and refused there under a code of its own — a
        // second description of a zone name here would be a second rule to keep in step.
        RuleFor(x => x.TimeZone!).MaximumLength(TrackingCsvZones.MaxNameLength).When(x => x.TimeZone is not null);
        // Bounded well inside the calendar, so that a time on that day stays an instant that can
        // be written down whatever offset or zone it is then read in. Nothing is said here about
        // the day being in the future: the rows are refused for that one by one, under the same
        // rule and the same clock as a row that wrote its own date.
        RuleFor(x => x.Day!.Value)
            .InclusiveBetween(EarliestDay, LatestDay)
            .OverridePropertyName(nameof(TrackingCsvImportOptionsDto.Day))
            .When(x => x.Day is not null);
    }

    /// <summary>The first day a sheet of times may be said to have been kept on.</summary>
    public static readonly DateOnly EarliestDay = new(1900, 1, 1);

    /// <summary>The last one.</summary>
    public static readonly DateOnly LatestDay = new(2200, 12, 31);
}

public sealed class TrackingCsvImportRequestValidator : AbstractValidator<TrackingCsvImportRequest>
{
    public TrackingCsvImportRequestValidator()
    {
        RuleFor(x => x.Text).NotEmpty().MaximumLength(TrackingCsvImportRequest.MaxTextLength);
        RuleFor(x => x.Options!).SetValidator(new TrackingCsvImportOptionsDtoValidator())
            .When(x => x.Options is not null);
    }
}

/// <summary>Committing a read sheet: the same text and choices, plus what may be done with it.</summary>
/// <param name="ReplaceExisting">
/// Whether a report the log already holds for the same person at the same instant may be
/// overwritten. Off by default: re-importing a corrected sheet is the intended use, and it is
/// also the one that silently changes history, so the reviewer says so.
/// </param>
/// <param name="Lines">
/// The physical lines to commit, or null for every importable one. An empty list commits
/// nothing: saying "none" is not the same as saying nothing. A reviewer who has read a
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
        RuleFor(x => x.Options!).SetValidator(new TrackingCsvImportOptionsDtoValidator())
            .When(x => x.Options is not null);
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
/// <param name="TimeZone">
/// The zone the cells that state no offset were read in, by the name the caller gave for it;
/// null when they were read exactly as written, as UTC. Every moment and every finding of this
/// answer — what is in the future, what the log already holds — was decided on the instants this
/// reading gave.
/// </param>
/// <param name="Day">
/// The day the sheet's times were put on, where the sheet wrote times with no dates and the caller
/// named the day; null where the rows wrote their own dates, whatever the caller sent.
/// </param>
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
    IReadOnlyList<TrackingCsvDiagnosticDto> Refused,
    string? TimeZone,
    DateOnly? Day);

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
