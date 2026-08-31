// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Domain;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Import;

/// <summary>A candidate that could not be created, and why — reported, never swallowed.</summary>
public sealed record ImportFailure(long SourceId, string? Name, string Code, string Reason);

/// <summary>
/// The attachments one line of a batch hung, as the row stores them. A tiny reader with one
/// home, because both the confirmation that writes them and the undo that takes them down have
/// to agree about the shape, and a list of ids in a jsonb column is easy to write two ways.
/// </summary>
public static class ImportBatchAttachments
{
    public static string Write(IEnumerable<Guid> ids) => ImportJson.Serialize(ids);

    public static IReadOnlyList<Guid> Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return ImportJson.Deserialize<List<Guid>>(json) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            // A line nothing can read is a line whose attachments undo cannot find. Saying so
            // by leaving them is better than refusing to undo the rest of the batch.
            return [];
        }
    }
}

/// <summary>What one confirmation did.</summary>
public sealed record ImportCommitResult(
    ImportBatch Batch, IReadOnlyList<ImportFailure> Failures);

/// <summary>Refusal of a whole commit, before anything was written.</summary>
public sealed class ImportCommitException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Turns reviewed candidates into registry objects, in one transaction, with a batch recording
/// what it did.
///
/// <para>
/// Every object goes through <see cref="FeatureWriteService"/> like any other write: an import
/// is a bulk create, not a second creation path, and the derived state it maintains
/// (containment edges, ancestor arrays, effective protection, the cave's entrance mirror) is
/// exactly what a hand-rolled bulk insert would get wrong and the integrity verifier would
/// then find months later.
/// </para>
/// </summary>
public sealed class ImportCommitService(
    SilexGisDbContext db,
    FeatureWriteService writer,
    ImportCandidateService candidates,
    IAccessService access,
    IOptions<ImportLimitOptions> limits)
{
    /// <summary>Entrance type a cave built around an imported waypoint gets when no rule said otherwise.</summary>
    private const string DefaultEntranceTypeCode = "natural";

    /// <summary>Cave type an imported cave gets when no rule said otherwise.</summary>
    private const string DefaultCaveTypeCode = "cave";

    public async Task<ImportCommitResult> CommitAsync(
        Geofile geofile,
        TermRuleSet? ruleSet,
        ImportOptions options,
        IReadOnlyCollection<long> selection,
        IReadOnlyDictionary<long, ImportDecision> decisions,
        ImportBatchMode mode,
        AccessContext ctx,
        CancellationToken ct = default)
    {
        var maxCommitItems = limits.Value.MaxCommitItems;
        if (selection.Count > maxCommitItems)
        {
            throw new ImportCommitException(
                "import.selection_too_large",
                $"A single confirmation creates at most {maxCommitItems} objects; {selection.Count} were selected. "
                + "Confirm them in smaller batches — each one reverts on its own.");
        }

        var rules = TermRuleSetStore.RulesOf(ruleSet);
        var (scanned, _, _) = await candidates.ScanAsync(geofile.Id, rules, options, ct);
        var byId = scanned.ToDictionary(c => c.SourceId);

        var taxonomies = await Taxonomies.LoadAsync(db, ct);
        // Read once for the batch rather than per created object: the tag set is the same for
        // every row, and asking again three hundred times is three hundred round trips.
        var tagIds = options.TagIds.Count == 0
            ? []
            : await db.Tags.AsNoTracking()
                .Where(t => options.TagIds.Contains(t.Id))
                .Select(t => t.Id)
                .ToListAsync(ct);
        var batch = new ImportBatch
        {
            GeofileId = geofile.Id,
            TermRuleSetId = ruleSet?.Id,
            TermRuleSetName = ruleSet?.Name,
            ConfirmedByUserId = ctx.UserId,
            Mode = mode,
            Options = ImportJson.Serialize(options),
            SkippedCount = scanned.Count - selection.Count,
        };

        var failures = new List<ImportFailure>();
        var items = new List<ImportBatchItem>();
        var touchedCaves = new HashSet<Guid>();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        foreach (var sourceId in selection)
        {
            if (!byId.TryGetValue(sourceId, out var candidate))
            {
                failures.Add(new ImportFailure(
                    sourceId, null, "import.candidate_missing",
                    "The row is no longer in the file — it was read again while the review was open."));
                continue;
            }

            var decision = decisions.GetValueOrDefault(sourceId) ?? new ImportDecision();
            try
            {
                var item = await ApplyAsync(
                    geofile, candidate, decision, options, taxonomies, tagIds, ctx, touchedCaves, ct);
                if (item is null)
                {
                    batch.SkippedCount++;
                    continue;
                }

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
            catch (FeatureWriteException ex)
            {
                // One odd row must not cost the other three hundred. The failure is reported
                // with the row it came from, so it can be fixed and re-confirmed on its own.
                failures.Add(new ImportFailure(
                    sourceId, candidate.SourceName, ex.Code, string.Join("; ", ex.Errors)));
            }
            catch (ImportCommitException ex)
            {
                failures.Add(new ImportFailure(sourceId, candidate.SourceName, ex.Code, ex.Message));
            }
        }

        if (items.Count == 0 && failures.Count > 0)
        {
            // Nothing landed: a batch recording only failures is a row nobody can act on, and
            // the failures themselves are the answer.
            await transaction.RollbackAsync(ct);
            throw new ImportCommitException(
                "import.nothing_created",
                $"None of the {failures.Count} selected rows could be created. {failures[0].Reason}");
        }

        db.ImportBatches.Add(batch);
        db.ImportBatchItems.AddRange(items);
        await db.SaveChangesAsync(ct);

        // Caves that gained an entrance need their mirror refreshed after the entrances exist.
        foreach (var caveId in touchedCaves)
        {
            await writer.SyncCaveMirrorAsync(caveId, ct);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new ImportCommitResult(batch, failures);
    }

    /// <summary>
    /// Undoes a confirmation: every object it created is soft-deleted, every attachment it hung
    /// is taken back down, and the batch is stamped. Caves that only gained an entrance keep
    /// their other entrances and are re-mirrored, so reverting an import that added a second
    /// entrance to somebody's cave leaves that cave exactly as it was.
    /// <para>
    /// One revert serves both kinds of batch. A vector import creates objects and nothing else,
    /// so taking the objects back is the whole of it; a photo import also hangs pictures on
    /// things that were already there, and soft-deleting what it created would leave those
    /// behind — on somebody else's cave, which is precisely the case undo exists for.
    /// </para>
    /// </summary>
    public async Task RevertAsync(ImportBatch batch, Guid userId, CancellationToken ct = default)
    {
        var allItems = await db.ImportBatchItems
            .Where(i => i.ImportBatchId == batch.Id)
            .ToListAsync(ct);
        var items = allItems.Where(i => i.FeatureId != null).ToList();

        var createdIds = items.Select(i => i.FeatureId!.Value).ToHashSet();
        var caves = await db.CaveEntrances.AsNoTracking()
            .Where(e => createdIds.Contains(e.Id))
            .Select(e => e.CaveFeatureId)
            .Distinct()
            .ToListAsync(ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        foreach (var id in createdIds)
        {
            // Deleting a cave stamps its entrances with it; asking again for an entrance that
            // is already gone stamps nothing, which is why order does not matter here.
            await writer.SoftDeleteAsync(id, ct);
        }

        // The pictures themselves are left alone — they were uploaded, not created here, and
        // an undo that deleted somebody's photographs would be a far larger action than the one
        // they asked for. Only the hanging comes down.
        var attachmentIds = allItems.SelectMany(i => ImportBatchAttachments.Read(i.AttachmentIds)).ToList();
        if (attachmentIds.Count > 0)
        {
            await db.Attachments.Where(a => attachmentIds.Contains(a.Id)).ExecuteDeleteAsync(ct);
        }

        batch.RevertedAt = DateTimeOffset.UtcNow;
        batch.RevertedByUserId = userId;
        await db.SaveChangesAsync(ct);

        foreach (var caveId in caves.Where(c => !createdIds.Contains(c)))
        {
            await writer.SyncCaveMirrorAsync(caveId, ct);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    // ---------- one candidate ----------

    private async Task<ImportBatchItem?> ApplyAsync(
        Geofile geofile,
        CandidateSummary candidate,
        ImportDecision decision,
        ImportOptions options,
        Taxonomies taxonomies,
        IReadOnlyList<long> tagIds,
        AccessContext ctx,
        HashSet<Guid> touchedCaves,
        CancellationToken ct)
    {
        if (decision.Action == ImportDecisionAction.Skip)
        {
            return null;
        }

        var item = new ImportBatchItem
        {
            SourceFeatureId = candidate.SourceId,
            RuleId = candidate.RuleId,
            RuleName = candidate.RuleName,
            Action = decision.Action,
            SourceProperties = candidate.SourceProperties,
        };

        if (decision.Action == ImportDecisionAction.Attach)
        {
            // Nothing is created: the batch records that this position was seen and recognised
            // as something already in the registry, which is what stops the next import of the
            // same file wondering about it again.
            item.AttachedToFeatureId = decision.AttachToFeatureId
                ?? throw new ImportCommitException(
                    "import.attach_target_missing", "Recognising a candidate needs the object it is.");
            return item;
        }

        var kind = decision.Kind
            ?? candidate.ProposedKind
            ?? throw new ImportCommitException(
                "import.kind_missing", "Nothing said what this candidate should become.");

        var keepElevation = KeepsElevation(options, decision);
        var geometry = await GeometryOfAsync(
                geofile.Id, candidate.SourceId, keepElevation, candidate.SourceElevation, ct)
            ?? throw new ImportCommitException(
                "import.geometry_missing", "The row no longer carries a readable geometry.");

        var name = decision.Name ?? candidate.ProposedName ?? candidate.SourceName;
        var altitude = AltitudeOf(keepElevation, candidate, geometry);

        item.FeatureId = kind switch
        {
            ImportTargetKind.Cave => await CreateCaveAsync(
                candidate, decision, options, taxonomies, geometry, name, altitude, ctx, touchedCaves, ct),
            ImportTargetKind.CaveEntrance => await CreateEntranceAsync(
                candidate, decision, options, taxonomies, geometry, name, altitude, ctx, touchedCaves, ct),
            _ => await CreateSurfaceFeatureAsync(candidate, decision, options, taxonomies, geometry, name, ctx, ct),
        };

        foreach (var tagId in tagIds)
        {
            db.Taggings.Add(new Tagging { TagId = tagId, FeatureId = item.FeatureId.Value, AddedBy = ctx.UserId });
        }

        return item;
    }

    private async Task<Guid> CreateCaveAsync(
        CandidateSummary candidate,
        ImportDecision decision,
        ImportOptions options,
        Taxonomies taxonomies,
        Geometry geometry,
        string? name,
        decimal? altitude,
        AccessContext ctx,
        HashSet<Guid> touchedCaves,
        CancellationToken ct)
    {
        RequireCreate(ctx, options);
        var point = AsPoint(geometry);
        var caveTypeId = taxonomies.CaveType(decision.CaveTypeCode ?? candidate.ProposedCaveTypeCode);

        var feature = new Feature
        {
            Name = name,
            Description = candidate.SourceDescription,
            Geom = point,
            OwnerUserId = ctx.UserId,
            CavingGroupId = options.CavingGroupId,
            Visibility = options.Visibility,
            LocationProtected = options.LocationProtected,
        };
        var cave = new Cave
        {
            CaveTypeId = caveTypeId,
            IdentificationCode = candidate.SourceCode,
            Altitude = altitude,
        };
        feature.Cave = cave;
        await writer.CreateCaveAsync(feature, cave, [], ct);

        // A cave whose only geometry is the cache of a main entrance it does not have would not
        // draw at all. An imported waypoint is a position first, so the cave it becomes gets
        // that position as its first entrance — which is also where the altitude belongs.
        var entranceFeature = new Feature { Geom = point };
        var entrance = new CaveEntrance
        {
            CaveFeatureId = feature.Id,
            EntranceTypeId = taxonomies.EntranceType(DefaultEntranceTypeCode),
            IsMain = true,
            Altitude = altitude,
            PositionQuality = PositionQuality.Unknown,
        };
        await writer.CreateEntranceAsync(entranceFeature, entrance, ct);
        touchedCaves.Add(feature.Id);

        return feature.Id;
    }

    private async Task<Guid> CreateEntranceAsync(
        CandidateSummary candidate,
        ImportDecision decision,
        ImportOptions options,
        Taxonomies taxonomies,
        Geometry geometry,
        string? name,
        decimal? altitude,
        AccessContext ctx,
        HashSet<Guid> touchedCaves,
        CancellationToken ct)
    {
        var point = AsPoint(geometry);
        var entranceTypeId = taxonomies.EntranceType(
            decision.EntranceTypeCode ?? candidate.ProposedEntranceTypeCode ?? DefaultEntranceTypeCode);

        if (decision.AttachToFeatureId is { } caveId)
        {
            // A second entrance of a cave already in the registry. Authorised against that
            // cave, not against the import: adding to somebody else's cave is a write on it.
            var cave = await db.Features.FirstOrDefaultAsync(f => f.Id == caveId && f.Kind == FeatureKind.Cave, ct)
                ?? throw new ImportCommitException("import.cave_not_found", "The chosen cave no longer exists.");
            if (!(await access.DecideAsync(ctx, AccessAction.Write, cave, ct)).Allowed)
            {
                throw new ImportCommitException(
                    "import.cave_forbidden", $"You may not add an entrance to '{cave.Name}'.");
            }

            var entranceFeature = new Feature
            {
                Name = name,
                Description = candidate.SourceDescription,
                Geom = point,
            };
            var entrance = new CaveEntrance
            {
                CaveFeatureId = caveId,
                EntranceTypeId = entranceTypeId,
                Altitude = altitude,
                PositionQuality = PositionQuality.Unknown,
            };
            await writer.CreateEntranceAsync(entranceFeature, entrance, ct);
            touchedCaves.Add(caveId);
            return entranceFeature.Id;
        }

        // An entrance with no cave named is a cave nobody has entered into the registry yet.
        // Creating the cave around it keeps the structure honest — an entrance without a cave
        // has nowhere to live — and the reviewer can move it under the right cave afterwards.
        return await CreateCaveAsync(
            candidate, decision, options, taxonomies, geometry, name, altitude, ctx, touchedCaves, ct);
    }

    private async Task<Guid> CreateSurfaceFeatureAsync(
        CandidateSummary candidate,
        ImportDecision decision,
        ImportOptions options,
        Taxonomies taxonomies,
        Geometry geometry,
        string? name,
        AccessContext ctx,
        CancellationToken ct)
    {
        RequireCreate(ctx, options);
        var code = decision.FeatureTypeCode ?? candidate.ProposedFeatureTypeCode;
        var featureTypeId = taxonomies.FeatureType(code);

        var feature = new Feature
        {
            Name = name,
            Description = candidate.SourceDescription,
            FeatureTypeId = featureTypeId,
            Geom = geometry,
            OwnerUserId = ctx.UserId,
            CavingGroupId = options.CavingGroupId,
            Visibility = options.Visibility,
            LocationProtected = options.LocationProtected,
        };
        await writer.CreateGenericAsync(feature, [], ct);
        return feature.Id;
    }

    private void RequireCreate(AccessContext ctx, ImportOptions options)
    {
        if (!CreateRules.MayCreate(ctx, AccessDomain.Features, options.CavingGroupId))
        {
            throw new ImportCommitException(CreateRules.ForbiddenCode, "You may not create features here.");
        }
    }

    // ---------- geometry ----------

    /// <summary>
    /// Whether this candidate keeps the altitude its row carries. The import's policy decides
    /// unless the row answers for itself; under the per-candidate policy nothing is kept until
    /// a row says so, because that is what choosing "decide per candidate" means.
    /// </summary>
    private static bool KeepsElevation(ImportOptions options, ImportDecision decision) =>
        decision.KeepElevation ?? options.Elevation == ImportElevationPolicy.Keep;

    private async Task<Geometry?> GeometryOfAsync(
        Guid geofileId, long sourceId, bool keepElevation, double? mappedElevation, CancellationToken ct)
    {
        var geometry = await db.GeofileFeatures.AsNoTracking()
            .Where(f => f.GeofileId == geofileId && f.Id == sourceId)
            .Select(f => f.Geom)
            .FirstOrDefaultAsync(ct);
        if (geometry is null)
        {
            return null;
        }

        // A single-part multi-geometry is what several writers produce for one waypoint; the
        // registry's point kinds want a point, and unwrapping is lossless.
        if (geometry is MultiPoint { NumGeometries: 1 } multi)
        {
            geometry = multi.GetGeometryN(0);
        }

        geometry.SRID = 4326;
        return geometry is Point point ? WithElevation(point, keepElevation, mappedElevation) : geometry;
    }

    /// <summary>
    /// The point as stored: Z carries the altitude when this candidate keeps one, and the point
    /// is flat when it does not. GPS altitude is the least reliable of the three numbers a
    /// handheld reports, so leaving it empty is a real answer rather than a way of losing data —
    /// and the file is the only place an altitude can come from.
    /// </summary>
    private static Point WithElevation(Point point, bool keepElevation, double? mappedElevation)
    {
        if (!keepElevation)
        {
            return new Point(new Coordinate(point.X, point.Y)) { SRID = 4326 };
        }

        var z = mappedElevation ?? (double.IsNaN(point.Coordinate.Z) ? null : point.Coordinate.Z);
        return z is null
            ? new Point(new Coordinate(point.X, point.Y)) { SRID = 4326 }
            : new Point(new CoordinateZ(point.X, point.Y, z.Value)) { SRID = 4326 };
    }

    private static decimal? AltitudeOf(bool keepElevation, CandidateSummary candidate, Geometry geometry)
    {
        if (!keepElevation)
        {
            return null;
        }

        var z = candidate.SourceElevation
            ?? (geometry.Coordinate is { } c && !double.IsNaN(c.Z) ? c.Z : null);
        return z is null ? null : (decimal)z;
    }

    private static Point AsPoint(Geometry geometry) =>
        geometry as Point
        ?? throw new ImportCommitException(
            "import.geometry_not_point", "A cave or an entrance is a position, so its candidate must be a point.");

    // ---------- taxonomy codes ----------

    /// <summary>
    /// The taxonomy rows a commit needs, read once. Rules name kinds by code because a code is
    /// what survives being sent to another installation; the identities they resolve to are
    /// local, and a code that resolves to nothing is a rule written against a taxonomy this
    /// installation does not have.
    /// </summary>
    private sealed record Taxonomies(
        IReadOnlyDictionary<string, long> CaveTypes,
        IReadOnlyDictionary<string, long> EntranceTypes,
        IReadOnlyDictionary<string, long> FeatureTypes)
    {
        public static async Task<Taxonomies> LoadAsync(SilexGisDbContext db, CancellationToken ct) => new(
            await db.CaveTypes.AsNoTracking().ToDictionaryAsync(t => t.Code, t => t.Id, StringComparer.OrdinalIgnoreCase, ct),
            await db.EntranceTypes.AsNoTracking().ToDictionaryAsync(t => t.Code, t => t.Id, StringComparer.OrdinalIgnoreCase, ct),
            await db.FeatureTypes.AsNoTracking().ToDictionaryAsync(t => t.Code, t => t.Id, StringComparer.OrdinalIgnoreCase, ct));

        public long CaveType(string? code) => Resolve(CaveTypes, code ?? DefaultCaveTypeCode, "cave type");

        public long EntranceType(string? code) =>
            Resolve(EntranceTypes, code ?? DefaultEntranceTypeCode, "entrance type");

        public long FeatureType(string? code) => code is null
            ? throw new ImportCommitException(
                "import.feature_type_missing", "A surface feature needs a kind, and no rule named one.")
            : Resolve(FeatureTypes, code, "feature type");

        private static long Resolve(IReadOnlyDictionary<string, long> map, string code, string what) =>
            map.TryGetValue(code, out var id)
                ? id
                : throw new ImportCommitException(
                    "import.type_unknown", $"This installation has no {what} called '{code}'.");
    }
}
