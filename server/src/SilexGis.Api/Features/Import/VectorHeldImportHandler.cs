// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Infrastructure.Import;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Import;

/// <summary>
/// A vector file's confirmation held for review, written on acceptance by the same commit the
/// queue runs for a confirmation that was not held.
/// </summary>
/// <remarks>
/// The held request is the queue's own payload, with the selection already resolved — a
/// confirmation "without review" is turned into the rows its rules claimed when it is held, so
/// the reviewer decides about the same rows the account was shown. Accepting runs in the
/// reviewer's request rather than on the queue: the decision has to know what it wrote, in its
/// own transaction, to stamp the lines with it.
/// </remarks>
internal sealed class VectorHeldImportHandler(
    SilexGisDbContext db,
    FeatureProtection protection,
    IAccessService access,
    ImportCommitService commits,
    TermRuleSetStore ruleSets) : HeldFileImportHandler(db, protection)
{
    /// <summary>The section a held vector file's lines are filed under.</summary>
    public const string Section = "vectorFile";

    /// <summary>The file a held confirmation was made against is no longer here, or no longer readable.</summary>
    public const string FileGoneCode = "import.file_gone";

    public override ImportSource Source => ImportSource.VectorFile;

    protected override async Task<HeldCommitFailure?> CommitAsync(
        ImportBatch batch, IReadOnlySet<string> accepted, HeldCommit into, AccessContext uploader, CancellationToken ct)
    {
        var payload = batch.HeldRequest is null ? null : ImportJson.Deserialize<ImportCommitPayload>(batch.HeldRequest);
        if (payload is null)
        {
            return new HeldCommitFailure(HeldFileImportHandler.UnreadableCode, "The held confirmation cannot be read.");
        }

        // Everything the confirmation route decides before it queues the work is asked again, of
        // the uploader as they stand now: a review never lands what the account could no longer do.
        var geofile = await Db.Geofiles.AsNoTracking().FirstOrDefaultAsync(g => g.Id == payload.GeofileId, ct);
        if (geofile is null || !(await access.DecideAsync(uploader, AccessAction.Read, geofile, ct)).Allowed)
        {
            return new HeldCommitFailure(FileGoneCode, "The file this import was made from is no longer here.");
        }

        var options = ImportJson.Deserialize<ImportOptions>(payload.Options)
            ?? throw new InvalidOperationException("The held import's options are not readable.");
        if (!CreateRules.MayCreate(uploader, AccessDomain.Features, options.CavingGroupId))
        {
            return new HeldCommitFailure(CreateRules.ForbiddenCode, "The account may no longer create objects here.");
        }

        if (options.CavingGroupId is { } groupId && !CavingGroupBindingRules.MayBind(uploader, AccessDomain.Features, groupId))
        {
            return new HeldCommitFailure(CavingGroupBindingRules.ForbiddenCode, "The account may no longer file objects under this group.");
        }

        var ruleSet = await ruleSets.ResolveAsync(uploader, payload.TermRuleSetId, ct);
        if (payload.TermRuleSetId is not null && ruleSet is null)
        {
            return new HeldCommitFailure("term_rules.not_found", "The rule set this import was read with is no longer available.");
        }

        var decisions = ImportJson.Deserialize<Dictionary<long, ImportDecision>>(payload.Decisions) ?? [];
        var replayed = payload.Selection.Distinct().ToDictionary(
            id => id,
            id => accepted.Contains(ImportCommitService.KeyOf(id))
                ? decisions.GetValueOrDefault(id) ?? new ImportDecision()
                : new ImportDecision { Action = ImportDecisionAction.Skip });
        try
        {
            await commits.CommitAsync(
                geofile, ruleSet, options, payload.Selection, replayed, payload.Mode, uploader, ct: ct, into: into);
        }
        catch (ImportCommitException e)
        {
            return new HeldCommitFailure(e.Code, e.Message);
        }

        // The review is spent as a commit spends it: its rows are objects now, and offering them
        // again would make them twice.
        await Db.GeofileImportSessions
            .Where(s => s.GeofileId == payload.GeofileId && s.UserId == payload.UserId)
            .ExecuteDeleteAsync(ct);
        return null;
    }

    /// <summary>The candidates a held vector confirmation waits with: every selected row not set aside.</summary>
    public static IReadOnlyList<HeldCandidate> CandidatesOf(
        IReadOnlyList<long> selection,
        IReadOnlyDictionary<long, ImportDecision> decisions,
        IReadOnlyDictionary<long, string?> names,
        bool locationProtected) =>
        [.. selection.Distinct()
            .Select(id => (Id: id, Decision: decisions.GetValueOrDefault(id) ?? new ImportDecision()))
            .Where(c => c.Decision.Action != ImportDecisionAction.Skip)
            .Select(c => new HeldCandidate(
                ImportCommitService.KeyOf(c.Id),
                c.Decision.Name ?? names.GetValueOrDefault(c.Id),
                c.Decision,
                c.Decision.AttachToFeatureId is { } target ? [target] : [],
                locationProtected))];
}
