// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Infrastructure.Import;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Import;

/// <summary>
/// A trip-sheet confirmation of an account under review, as it waits: the file it was made
/// against and the request, with the saved review's decisions folded in. The saved review can
/// change while the confirmation waits — the account keeps working on its own screen — so what
/// the account confirmed is kept here rather than read back from it.
/// </summary>
internal sealed record HeldTripImportRequest(
    Guid FileId,
    TripImportOptions Options,
    IReadOnlyList<int> Lines,
    IReadOnlyDictionary<string, TripImportDecision> Decisions);

/// <summary>
/// A trip sheet confirmed by an account under review, recorded on acceptance by the same commit
/// that records a sheet at once.
/// </summary>
internal sealed class TripHeldImportHandler(
    SilexGisDbContext db,
    FeatureProtection protection,
    IAccessService access,
    TripImportCommitService commits) : HeldFileImportHandler(db, protection)
{
    /// <summary>The section a held sheet's lines are filed under.</summary>
    public const string Section = "tripSheet";

    public override ImportSource Source => ImportSource.TripCsv;

    protected override async Task<HeldCommitFailure?> CommitAsync(
        ImportBatch batch, IReadOnlySet<string> accepted, HeldCommit into, AccessContext uploader, CancellationToken ct)
    {
        var request = batch.HeldRequest is null ? null : ImportJson.Deserialize<HeldTripImportRequest>(batch.HeldRequest);
        if (request is null)
        {
            return new HeldCommitFailure(UnreadableCode, "The held confirmation cannot be read.");
        }

        // Everything the confirmation route asks before it records a sheet is asked again, of the
        // uploader as they stand now.
        if (TripImportCreateRights.Refusal(uploader, request.Options) is { } refusal)
        {
            return new HeldCommitFailure(refusal.Code, refusal.Message);
        }

        var (files, _) = await PhotoImportEndpoints.ReadableFilesAsync(Db, access, uploader, [request.FileId], ct);
        if (files.Count == 0)
        {
            return new HeldCommitFailure(VectorHeldImportHandler.FileGoneCode, "The sheet this import was made from is no longer here.");
        }

        var decisions = TripImportEndpoints.ReadDecisions(ImportJson.Serialize(request.Decisions));
        var replayed = request.Lines.Distinct().ToDictionary(
            line => line,
            line => accepted.Contains(TripImportCommitService.KeyOf(line))
                ? decisions.GetValueOrDefault(line) ?? new TripImportDecision()
                : new TripImportDecision { Action = TripImportRowAction.Skip });
        try
        {
            await commits.CommitAsync(files[0], request.Options, request.Lines, replayed, uploader, ct, into);
        }
        catch (TripCsvReadException e)
        {
            return new HeldCommitFailure(e.Code, e.Message);
        }
        catch (TripImportCommitException e)
        {
            return new HeldCommitFailure(e.Code, e.Message);
        }

        return null;
    }

    /// <summary>
    /// The candidates a held sheet waits with: every chosen row not set aside, named by its title
    /// as the sheet reads under the confirmed choices.
    /// </summary>
    public static IReadOnlyList<HeldCandidate> CandidatesOf(
        IReadOnlyList<int> lines,
        IReadOnlyDictionary<int, TripImportDecision> decisions,
        IReadOnlyDictionary<int, string?> titles) =>
        [.. lines.Distinct()
            .Select(line => (Line: line, Decision: decisions.GetValueOrDefault(line) ?? new TripImportDecision()))
            .Where(c => c.Decision.Action != TripImportRowAction.Skip)
            .Select(c => new HeldCandidate(
                TripImportCommitService.KeyOf(c.Line),
                titles.GetValueOrDefault(c.Line),
                new { line = c.Line, action = c.Decision.Action ?? TripImportRowAction.Create },
                [],
                false))];

    /// <summary>The decisions in their wire form, keyed as JSON keys are.</summary>
    public static Dictionary<string, TripImportDecision> WireOf(IReadOnlyDictionary<int, TripImportDecision> decisions) =>
        decisions.ToDictionary(d => d.Key.ToString(CultureInfo.InvariantCulture), d => d.Value);
}
