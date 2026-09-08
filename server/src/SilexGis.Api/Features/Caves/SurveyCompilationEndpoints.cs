// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Caves;

/// <summary>
/// One row of the compiler's loop-error table: how far a closed loop failed to close.
/// </summary>
/// <remarks>
/// Both measures are given because they answer different questions. <c>absoluteErrorM</c> is a
/// distance in metres — how far apart the two arrivals at the closing station are.
/// <c>relativeErrorPercent</c> is that distance as a percentage of the loop's own length, which is
/// what says whether the distance is large for a loop of this size. A short loop can be much the
/// worse percentage while being much the smaller distance, so neither stands in for the other and
/// nothing is ranked by one under the other's name. The field names are the compiler's own column
/// headings so that a figure here and a figure in the operator's log are visibly the same number.
/// </remarks>
public sealed record SurveyLoopErrorDto(
    /// <summary>The loop's position in the compiler's own table, from zero.</summary>
    int Ordinal,
    /// <summary>REL-ERR: the closing distance as a percentage of the loop's length.</summary>
    double RelativeErrorPercent,
    /// <summary>ABS-ERR: the closing distance itself, in metres.</summary>
    double AbsoluteErrorM,
    /// <summary>TOTAL-L: the length of the loop, in metres.</summary>
    double TotalLengthM,
    /// <summary>STS: how many stations the loop runs through.</summary>
    int StationCount,
    /// <summary>X-ERROR: the east–west component of the closing distance, in metres.</summary>
    double ErrorXM,
    /// <summary>Y-ERROR: the north–south component of the closing distance, in metres.</summary>
    double ErrorYM,
    /// <summary>Z-ERROR: the vertical component of the closing distance, in metres.</summary>
    double ErrorZM,
    /// <summary>The loop's station sequence as the compiler printed it, joined by " - ".</summary>
    string Stations);

/// <summary>
/// What one archived compilation log said about the survey it was written for.
/// </summary>
/// <remarks>
/// The figures describe a single run and carry the run with them: <c>logFileId</c> and
/// <c>logVersionNumber</c> say which archived bytes were read and which revision of the source they
/// were, <c>readAt</c> says when they were read here, and <c>compilerVersion</c> says what read
/// them. A log carries no timestamp of its own, so <c>compilerReleaseDate</c> is a fact about the
/// compiler and never about when the compilation happened.
/// </remarks>
public sealed record SurveyCompilationDto(
    Guid Id,
    Guid CaveId,
    /// <summary>The archived log these figures were read from.</summary>
    Guid SurveySourceId,
    /// <summary>That archive entry's file name.</summary>
    string SourceName,
    /// <summary>How far this application has got with the log — queued, read, or unreadable.</summary>
    SurveyCompilationStatus Status,
    /// <summary>Why it could not be read; null unless the status says it could not be.</summary>
    string? ReadError,
    /// <summary>When the log was read here; null until it has been.</summary>
    DateTimeOffset? ReadAt,
    /// <summary>The stored file whose bytes were read.</summary>
    Guid LogFileId,
    /// <summary>Which revision of the archived source those bytes were.</summary>
    int LogVersionNumber,
    /// <summary>
    /// How the compilation itself ended. A failed compilation is a quality signal of its own and is
    /// reported as one — it is not a survey with no loops.
    /// </summary>
    SurveyCompilationOutcome? Outcome,
    string? CompilerVersion,
    string? CompilerReleaseDate,
    /// <summary>The step a failed run died in; null for a run that did not fail.</summary>
    string? IncompleteStage,
    int? CompilationSeconds,
    /// <summary>How many errors the compiler reported; null until the log has been read.</summary>
    int? ErrorCount,
    /// <summary>How many warnings the compiler reported; null until the log has been read.</summary>
    int? WarningCount,
    /// <summary>
    /// How many loops the compiler said the survey has. Not the length of <c>loops</c>: the count
    /// and the table are printed by different stages, so a run that stopped in between reports the
    /// count and no rows.
    /// </summary>
    int? LoopCount,
    double? AverageLoopErrorPercent,
    double? TotalLengthM,
    /// <summary>The total length after the compiler distributed the loop errors around the network.</summary>
    double? TotalLengthAdjustedM,
    /// <summary>The loop-error table, in the order the compiler printed it.</summary>
    IReadOnlyList<SurveyLoopErrorDto> Loops,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// How well a cave's surveys close, as the compiler that compiled them reported it.
///
/// <para>
/// Nothing here is re-derived. The numbers are the ones printed in the log the surveyor archived,
/// under the compiler's own names for them, so that a figure on this route and a figure in their
/// own log are recognisably the same number and a disagreement is a real disagreement.
/// </para>
///
/// <para>
/// Access is the cave's, on the same terms as its archived sources and its compiled models. How
/// well a survey closes is not itself a position — but it is only readable through a record that
/// names stations inside the cave, it exists only because a log was archived against that cave, and
/// a cave whose exact location is protected withholds its survey material whole. This route
/// inherits that gate rather than being given a looser one for looking harmless: the cave stays
/// readable and its compilations do not follow it out.
/// </para>
/// </summary>
public static class SurveyCompilationEndpoints
{
    public static RouteGroupBuilder MapSurveyCompilationEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/caves/{caveId:guid}/survey-compilations", ListAsync)
            .WithTags("SurveySources")
            .WithSummary("Loop closure as the compiler reported it; withheld without the exact-location permission.");

        return api;
    }

    private static async Task<Results<Ok<List<SurveyCompilationDto>>, ProblemHttpResult>> ListAsync(
        Guid caveId,
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var cave = await db.Features.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == caveId && f.Kind == FeatureKind.Cave, ct);
        if (cave is null || !(await access.DecideAsync(ctx, AccessAction.Read, cave, ct)).Allowed)
        {
            return ApiProblems.NotFound("cave.not_found");
        }

        // The cave stays readable; what its surveys measured does not follow it out.
        if (!await SurveyModelAccess.LocationOpenAsync(protection, ctx, caveId, ct))
        {
            return TypedResults.Ok(new List<SurveyCompilationDto>());
        }

        var rows = await db.SurveyCompilations.AsNoTracking()
            .Where(c => c.CaveFeatureId == caveId)
            .Include(c => c.Loops)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);

        var names = await db.SurveySources.AsNoTracking()
            .Where(s => s.CaveFeatureId == caveId)
            .Select(s => new { s.Id, s.Name })
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        return TypedResults.Ok(rows.Select(c => ToDto(c, names)).ToList());
    }

    private static SurveyCompilationDto ToDto(SurveyCompilation c, Dictionary<Guid, string> names) => new(
        c.Id,
        c.CaveFeatureId,
        c.SurveySourceId,
        names.TryGetValue(c.SurveySourceId, out var name) ? name : string.Empty,
        c.Status,
        c.ReadError,
        c.ReadAt,
        c.LogFileId,
        c.LogVersionNumber,
        c.Outcome,
        c.CompilerVersion,
        c.CompilerReleaseDate,
        c.IncompleteStage,
        c.CompilationSeconds,
        c.ErrorCount,
        c.WarningCount,
        c.LoopCount,
        c.AverageLoopErrorPercent,
        c.TotalLengthM,
        c.TotalLengthAdjustedM,
        [.. c.Loops.OrderBy(l => l.Ordinal).Select(l => new SurveyLoopErrorDto(
            l.Ordinal,
            l.RelativeErrorPercent,
            l.AbsoluteErrorM,
            l.TotalLengthM,
            l.StationCount,
            l.ErrorXM,
            l.ErrorYM,
            l.ErrorZM,
            l.Stations))],
        c.CreatedAt,
        c.UpdatedAt);
}
