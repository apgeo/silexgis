// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Infrastructure.Catalogue;
using SilexGis.Infrastructure.Import;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Catalogue;

/// <summary>
/// A selection from the national catalogue confirmed by an account under review, imported on
/// acceptance by the same commit that imports a selection at once — which is also when the
/// catalogue is first asked for the records.
/// </summary>
internal sealed class CatalogueHeldImportHandler(
    SilexGisDbContext db,
    FeatureProtection protection,
    SpeologieImportService importer) : HeldFileImportHandler(db, protection)
{
    /// <summary>The section a held catalogue selection's lines are filed under.</summary>
    public const string Section = "catalogue";

    public override ImportSource Source => ImportSource.ExternalCatalogue;

    protected override async Task<HeldCommitFailure?> CommitAsync(
        ImportBatch batch, IReadOnlySet<string> accepted, HeldCommit into, AccessContext uploader, CancellationToken ct)
    {
        var request = batch.HeldRequest is null ? null : ImportJson.Deserialize<SpeologieImportRequest>(batch.HeldRequest);
        if (request is null)
        {
            return new HeldCommitFailure(UnreadableCode, "The held confirmation cannot be read.");
        }

        var decisions = CatalogueEndpoints.ReadDecisions(request);
        var replayed = request.Selection.Distinct().ToDictionary(
            id => id,
            id => accepted.Contains(SpeologieImportService.KeyOf(id))
                ? decisions.GetValueOrDefault(id) ?? new SpeologieDecision()
                : new SpeologieDecision(SpeologieAction.Skip));
        try
        {
            // The import asks the uploader's right to create itself, before it asks the catalogue.
            await importer.CommitAsync(request.Selection, replayed, CatalogueEndpoints.OptionsOf(request), uploader, ct, into);
            return null;
        }
        catch (SpeologieImportException e)
        {
            return new HeldCommitFailure(e.Code, e.Message);
        }
        catch (SpeologieException e)
        {
            // The catalogue not answering is not the uploader's doing; the decision waits for it.
            throw new HeldWriteUnavailableException(e.Code, e.Message);
        }
    }

    /// <summary>The candidates a held selection waits with: every chosen cave not set aside.</summary>
    public static IReadOnlyList<HeldCandidate> CandidatesOf(
        SpeologieImportRequest request, IReadOnlyDictionary<int, SpeologieDecision> decisions) =>
        [.. request.Selection.Distinct()
            .Select(id => (Id: id, Decision: decisions.GetValueOrDefault(id) ?? new SpeologieDecision()))
            .Where(c => c.Decision.Action != SpeologieAction.Skip)
            .Select(c => new HeldCandidate(
                SpeologieImportService.KeyOf(c.Id),
                null,
                c.Decision,
                request.ParentId is { } parent ? [parent] : [],
                request.LocationProtected))];
}
