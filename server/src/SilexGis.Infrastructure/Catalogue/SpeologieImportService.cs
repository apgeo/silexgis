// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Catalogue;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Import;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Catalogue;

/// <summary>What is to be done with one catalogue cave.</summary>
public enum SpeologieAction : short
{
    /// <summary>Create a new cave from the catalogue record.</summary>
    Create = 0,

    /// <summary>Rewrite the fields this integration owns on the cave that was imported before.</summary>
    Update = 1,

    /// <summary>Leave it alone. Recorded, so the batch says what was passed over rather than staying silent.</summary>
    Skip = 2,
}

/// <summary>The choices that apply to a whole confirmation.</summary>
/// <param name="Visibility">Who may see what is created. Private unless the person says otherwise.</param>
/// <param name="CavingGroupId">The group the created caves belong to, if any.</param>
/// <param name="LocationProtected">Whether the created caves are protection roots.</param>
/// <param name="ParentId">A feature to file the created caves under, if any.</param>
public sealed record SpeologieImportOptions(
    Visibility Visibility = Visibility.Private,
    Guid? CavingGroupId = null,
    bool LocationProtected = false,
    Guid? ParentId = null);

/// <summary>
/// What one person decided about one catalogue cave. Every field beyond
/// <see cref="Action"/> overrides something that was worked out for them, so agreeing with what
/// was proposed costs nothing to send.
/// </summary>
/// <param name="Action">Create, refresh, or pass over.</param>
/// <param name="CaveTypeCode">Overrides the type worked out from the cave's name.</param>
/// <param name="Longitude">Where the cave is, if the person knows and the catalogue does not. Both ordinates or neither.</param>
/// <param name="Latitude">See <paramref name="Longitude"/>.</param>
public sealed record SpeologieDecision(
    SpeologieAction? Action = null,
    string? CaveTypeCode = null,
    double? Longitude = null,
    double? Latitude = null);

/// <summary>One catalogue cave that could not be dealt with, and why.</summary>
public sealed record SpeologieFailure(int SpeologieId, string? Title, string Code, string Reason);

/// <summary>What one confirmation did.</summary>
public sealed record SpeologieImportResult(ImportBatch Batch, IReadOnlyList<SpeologieFailure> Failures);

/// <summary>
/// Turns caves chosen from the Romanian catalogue into caves of this registry.
///
/// <para>
/// It follows the shape every other import here follows — one transaction, one batch, one
/// revert, and every object created through <see cref="FeatureWriteService"/> rather than by a
/// second creation path — and it differs from them in one way that shows up everywhere:
/// <b>the catalogue publishes no coordinates at all</b>. Not withheld, not approximate; the
/// field does not exist. So an imported cave arrives with no position unless the person
/// importing it supplies one, and a cave with no position is a legal but quiet thing here — it
/// is in the registry and in search, and it is on no map. That is a faithful record of what is
/// known, and the alternative would be to invent a position, which is worse.
/// </para>
/// </summary>
public sealed class SpeologieImportService(
    SilexGisDbContext db,
    SpeologieClient client,
    FeatureWriteService writer,
    IAccessService access,
    IOptions<SpeologieOptions> options)
{
    /// <summary>Type a cave gets when its name says nothing about what kind of cave it is.</summary>
    private const string DefaultCaveTypeCode = "cave";

    /// <summary>Type given to the entrance created when somebody places an imported cave on the map.</summary>
    private const string DefaultEntranceTypeCode = "natural";

    /// <summary>
    /// Confirms a selection. Records the catalogue could not produce, and caves the caller may
    /// not write to, are reported per row rather than taking the confirmation down — the same
    /// bargain the other importers make, for the same reason.
    /// </summary>
    public async Task<SpeologieImportResult> CommitAsync(
        IReadOnlyCollection<int> selection,
        IReadOnlyDictionary<int, SpeologieDecision> decisions,
        SpeologieImportOptions importOptions,
        AccessContext ctx,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(decisions);
        ArgumentNullException.ThrowIfNull(importOptions);

        var wanted = selection.Distinct().ToArray();

        if (wanted.Length == 0)
        {
            throw new SpeologieImportException("speologie.selection_empty", "Nothing was selected.");
        }

        if (wanted.Length > client.MaxSelection)
        {
            throw new SpeologieImportException(
                "speologie.selection_too_large",
                $"One confirmation takes at most {client.MaxSelection} caves; {wanted.Length} were selected. "
                + "Confirm them in smaller batches — each one reverts on its own.");
        }

        if (!CreateRules.MayCreate(ctx, AccessDomain.Features, importOptions.CavingGroupId))
        {
            throw new SpeologieImportException(CreateRules.ForbiddenCode, "You may not create caves here.");
        }

        // Everything the far end has to say, before the transaction opens. A slow third party is
        // not something to hold a database transaction open across, and the throttle means this
        // takes seconds rather than milliseconds by design.
        var records = (await client.GetManyAsync(wanted, ct)).ToDictionary(r => r.Id);
        var existing = await ExistingByCatalogueIdAsync(wanted, ct);

        var taxonomies = await CaveTaxonomies.LoadAsync(db, ct);
        var rules = TermRuleSeeds.Default;
        var retrievedAt = DateTimeOffset.UtcNow;

        var parents = await ParentSpecsAsync(importOptions.ParentId, ctx, ct);

        var batch = new ImportBatch
        {
            Source = ImportSource.ExternalCatalogue,
            ConfirmedByUserId = ctx.UserId,
            Mode = ImportBatchMode.Reviewed,
            Options = JsonSerializer.Serialize(importOptions),
        };

        var failures = new List<SpeologieFailure>();
        var items = new List<ImportBatchItem>();
        var placed = new HashSet<Guid>();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        foreach (var id in wanted)
        {
            if (!records.TryGetValue(id, out var record))
            {
                // The catalogue no longer publishes it, or never did. Not a failure of this
                // installation, and worth saying in those words.
                failures.Add(new SpeologieFailure(
                    id, null, "speologie.record_missing",
                    "The catalogue no longer publishes this cave."));
                continue;
            }

            var decision = decisions.GetValueOrDefault(id) ?? new SpeologieDecision();
            existing.TryGetValue(id, out var already);

            var action = decision.Action ?? (already is null ? SpeologieAction.Create : SpeologieAction.Update);

            if (action == SpeologieAction.Skip)
            {
                batch.SkippedCount++;
                continue;
            }

            try
            {
                var item = action switch
                {
                    SpeologieAction.Update when already is not null => await UpdateAsync(
                        already, record, decision, retrievedAt, taxonomies, ctx, placed, ct),
                    _ => await CreateAsync(
                        record, decision, importOptions, parents, retrievedAt, taxonomies, rules, ctx, placed, ct),
                };

                item.ImportBatchId = batch.Id;
                items.Add(item);

                if (item.Action == ImportDecisionAction.Attach)
                {
                    batch.AttachedCount++;
                }
                else
                {
                    batch.CreatedCount++;
                }
            }
            catch (FeatureWriteException e)
            {
                failures.Add(new SpeologieFailure(id, record.Title, e.Code, string.Join("; ", e.Errors)));
            }
            catch (SpeologieImportException e)
            {
                failures.Add(new SpeologieFailure(id, record.Title, e.Code, e.Message));
            }
        }

        if (items.Count == 0 && failures.Count > 0)
        {
            await transaction.RollbackAsync(ct);
            throw new SpeologieImportException(
                "speologie.nothing_created",
                $"None of the {failures.Count} selected caves could be imported. {failures[0].Reason}");
        }

        db.ImportBatches.Add(batch);
        db.ImportBatchItems.AddRange(items);
        await db.SaveChangesAsync(ct);

        // Caves that gained an entrance need the point that stands for them refreshed, and that
        // can only happen once the entrance rows exist.
        foreach (var caveId in placed)
        {
            await writer.SyncCaveMirrorAsync(caveId, ct);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new SpeologieImportResult(batch, failures);
    }

    /// <summary>
    /// The caves this installation already holds for the given catalogue ids, found through the
    /// key the import wrote into their properties bag.
    /// </summary>
    /// <remarks>
    /// Deleted caves are deliberately not excluded from the lookup by this query alone — a cave
    /// in the recycling bin still holds the catalogue's id, and treating it as absent would
    /// create a second copy that then collides with the first if the deletion is undone. Soft
    /// deleted rows are filtered by the context's own query filter, so what is left here is the
    /// live ones; a re-import after a deletion creates a fresh cave, which is the honest outcome.
    /// </remarks>
    private async Task<Dictionary<int, Feature>> ExistingByCatalogueIdAsync(
        IReadOnlyCollection<int> ids, CancellationToken ct)
    {
        var matches = await SpeologieSql.MatchAsync(db, ids, ct);
        if (matches.Count == 0)
        {
            return [];
        }

        // Tracked and with the subtype row, because these are the rows a refresh writes to.
        var featureIds = matches.Select(m => m.FeatureId).ToArray();
        var features = await db.Features
            .Include(f => f.Cave)
            .Where(f => featureIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, ct);

        var byCatalogueId = new Dictionary<int, Feature>(matches.Count);
        foreach (var match in matches)
        {
            if (features.TryGetValue(match.FeatureId, out var feature))
            {
                byCatalogueId[match.SpeologieId] = feature;
            }
        }

        return byCatalogueId;
    }

    private async Task<IReadOnlyList<ParentSpec>> ParentSpecsAsync(
        Guid? parentId, AccessContext ctx, CancellationToken ct)
    {
        if (parentId is not { } id)
        {
            return [];
        }

        var parent = await db.Features.AsNoTracking()
            .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .FirstOrDefaultAsync(f => f.Id == id, ct)
            ?? throw new SpeologieImportException(
                "speologie.parent_not_found", "The chosen parent no longer exists, or you may not see it.");

        return [new ParentSpec(parent.Id, true)];
    }

    private async Task<ImportBatchItem> CreateAsync(
        SpeologieRecord record,
        SpeologieDecision decision,
        SpeologieImportOptions importOptions,
        IReadOnlyList<ParentSpec> parents,
        DateTimeOffset retrievedAt,
        CaveTaxonomies taxonomies,
        IReadOnlyList<TermRule> rules,
        AccessContext ctx,
        HashSet<Guid> placed,
        CancellationToken ct)
    {
        var values = SpeologieMapping.ToCaveValues(record, retrievedAt, options.Value.MaxDescriptionChars);
        var caveTypeCode = decision.CaveTypeCode ?? CaveTypeFromName(record.Title, rules);

        var feature = new Feature
        {
            Name = values.Name,
            Description = values.Description,
            Properties = values.Properties.ToJsonString(),
            OwnerUserId = ctx.UserId,
            CavingGroupId = importOptions.CavingGroupId,
            Visibility = importOptions.Visibility,
            LocationProtected = importOptions.LocationProtected,
        };

        var cave = new Cave
        {
            CaveTypeId = taxonomies.CaveType(caveTypeCode),
            Region = values.Region,
            ClosestAddress = values.ClosestAddress,
            Website = values.Website,
            Altitude = values.Altitude,
            SurveyedLength = values.SurveyedLength,
            Depth = values.Depth,
            NegativeDepth = values.NegativeDepth,
            ProtectionClass = values.ProtectionClass,
        };

        feature.Cave = cave;
        await writer.CreateCaveAsync(feature, cave, parents, ct);

        await PlaceAsync(feature.Id, decision, values.Altitude, taxonomies, placed, ct);

        return NewItem(record, feature.Id, ImportDecisionAction.Create);
    }

    /// <summary>
    /// Refreshes a cave that was imported before.
    ///
    /// <para>
    /// Only the fields this integration maps are rewritten — everything else somebody has put on
    /// that cave is not this import's to discard, and the properties bag is merged key by key for
    /// the same reason. Two guards apply beyond the ordinary write check. A caller who may not
    /// see the cave's exact position does not get to overwrite the fields that are withheld from
    /// them: what they were shown is not what is stored, so writing back what they were shown
    /// would quietly replace the real value with a redaction. And a protection flag is never
    /// touched here, because turning protection off is a decision about a cave, not a side effect
    /// of refreshing its length.
    /// </para>
    /// </summary>
    private async Task<ImportBatchItem> UpdateAsync(
        Feature feature,
        SpeologieRecord record,
        SpeologieDecision decision,
        DateTimeOffset retrievedAt,
        CaveTaxonomies taxonomies,
        AccessContext ctx,
        HashSet<Guid> placed,
        CancellationToken ct)
    {
        if (!(await access.DecideAsync(ctx, AccessAction.Write, feature, ct)).Allowed)
        {
            throw new SpeologieImportException(
                "speologie.update_forbidden",
                $"You may not change '{feature.Name}', which is where this cave was imported before.");
        }

        var cave = feature.Cave
            ?? throw new SpeologieImportException(
                "speologie.cave_missing", "The cave this catalogue entry was imported as is no longer a cave.");

        var values = SpeologieMapping.ToCaveValues(record, retrievedAt, options.Value.MaxDescriptionChars);
        var maySeeExact = (await access.DecideAsync(ctx, AccessAction.ViewExactLocation, feature, ct)).Allowed;

        feature.Name = values.Name;
        feature.Description = values.Description;
        feature.Properties = SpeologieMapping.MergeProperties(feature.Properties, values.Properties);

        cave.Region = values.Region;
        cave.Website = values.Website;
        cave.Altitude = values.Altitude;
        cave.SurveyedLength = values.SurveyedLength;
        cave.Depth = values.Depth;
        cave.NegativeDepth = values.NegativeDepth;
        cave.ProtectionClass = values.ProtectionClass;

        if (maySeeExact)
        {
            cave.ClosestAddress = values.ClosestAddress;
        }

        if (decision.CaveTypeCode is { } code)
        {
            cave.CaveTypeId = taxonomies.CaveType(code);
        }

        await PlaceAsync(feature.Id, decision, values.Altitude, taxonomies, placed, ct);

        // An update points at the cave it changed rather than at one it created, which is what
        // makes undo leave it alone: reverting soft-deletes what a batch created, and this batch
        // did not create this cave.
        return NewItem(record, featureId: null, ImportDecisionAction.Attach, attachedTo: feature.Id);
    }

    /// <summary>
    /// Gives a cave the position somebody supplied, as its main entrance — which is the only way
    /// a cave has a position here at all.
    /// </summary>
    /// <remarks>
    /// A cave that already has an entrance is left alone: the person placing caves on a map in an
    /// import wizard is filling in what the catalogue does not know, not correcting a survey
    /// somebody else recorded.
    /// </remarks>
    private async Task PlaceAsync(
        Guid caveId,
        SpeologieDecision decision,
        decimal? altitude,
        CaveTaxonomies taxonomies,
        HashSet<Guid> placed,
        CancellationToken ct)
    {
        if (decision.Longitude is not { } lon || decision.Latitude is not { } lat)
        {
            return;
        }

        if (double.IsNaN(lon) || double.IsNaN(lat)
            || lon is < -180 or > 180 || lat is < -90 or > 90)
        {
            throw new SpeologieImportException(
                "speologie.position_invalid", "The position given for this cave is not on the earth.");
        }

        var hasEntrance = await db.Features
            .AnyAsync(f => f.Kind == FeatureKind.CaveEntrance && f.Entrance!.CaveFeatureId == caveId, ct);

        if (hasEntrance)
        {
            return;
        }

        // The altitude the catalogue records is the entrance's, so it belongs on the point as
        // well as on the cave — the same place the other importers put it.
        var point = altitude is { } z
            ? new Point(new CoordinateZ(lon, lat, (double)z)) { SRID = 4326 }
            : new Point(new Coordinate(lon, lat)) { SRID = 4326 };

        var entranceFeature = new Feature { Geom = point };
        var entrance = new CaveEntrance
        {
            CaveFeatureId = caveId,
            EntranceTypeId = taxonomies.EntranceType(DefaultEntranceTypeCode),
            IsMain = true,
            Altitude = altitude,
            // Somebody read a map and pointed at it. That is exactly what this value means, and
            // it is neither a reading from an instrument nor a guess from a description.
            PositionQuality = PositionQuality.Map,
        };

        await writer.CreateEntranceAsync(entranceFeature, entrance, ct);
        placed.Add(caveId);
    }

    /// <summary>
    /// Works out what kind of cave this is from its name, using the same Romanian naming rules a
    /// file import uses — <c>Peștera …</c> is a cave, <c>Avenul …</c> is a pit. The catalogue has
    /// no field for it, and the names are consistent enough that reading them is better than
    /// calling everything a cave.
    /// </summary>
    private static string CaveTypeFromName(string? title, IReadOnlyList<TermRule> rules)
    {
        // Romanian and English both, because the seeded set carries terms under each and the
        // order it puts them in is what resolves a name that could be read either way.
        var proposal = TermRuleEvaluator.Evaluate(rules, new CandidateText(title, null), ["ro", "en"]);

        return proposal.Winner?.Rule is { Target: ImportTargetKind.Cave, CaveTypeCode: { } code }
            ? code
            : DefaultCaveTypeCode;
    }

    private static ImportBatchItem NewItem(
        SpeologieRecord record, Guid? featureId, ImportDecisionAction action, Guid? attachedTo = null) =>
        new()
        {
            FeatureId = featureId,
            AttachedToFeatureId = attachedTo,
            Action = action,
            // The record exactly as the catalogue gave it. This is what makes a wrong mapping
            // recoverable without asking the catalogue again — and the catalogue is somebody
            // else's small service, so not asking it again matters.
            SourceProperties = JsonSerializer.Serialize(record),
        };

    /// <summary>The taxonomy codes this import resolves, read once per confirmation.</summary>
    private sealed record CaveTaxonomies(
        IReadOnlyDictionary<string, long> CaveTypes,
        IReadOnlyDictionary<string, long> EntranceTypes)
    {
        public static async Task<CaveTaxonomies> LoadAsync(SilexGisDbContext db, CancellationToken ct) => new(
            await db.CaveTypes.AsNoTracking()
                .ToDictionaryAsync(t => t.Code, t => t.Id, StringComparer.OrdinalIgnoreCase, ct),
            await db.EntranceTypes.AsNoTracking()
                .ToDictionaryAsync(t => t.Code, t => t.Id, StringComparer.OrdinalIgnoreCase, ct));

        public long CaveType(string? code) => Resolve(CaveTypes, code ?? DefaultCaveTypeCode, "cave type");

        public long EntranceType(string? code) =>
            Resolve(EntranceTypes, code ?? DefaultEntranceTypeCode, "entrance type");

        private static long Resolve(IReadOnlyDictionary<string, long> map, string code, string what) =>
            map.TryGetValue(code, out var id)
                ? id
                : throw new SpeologieImportException(
                    "speologie.type_unknown", $"This installation has no {what} called '{code}'.");
    }
}

/// <summary>Refusal of one row, or of a whole confirmation before anything was written.</summary>
public sealed class SpeologieImportException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}
