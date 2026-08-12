// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Import;

/// <summary>
/// Turns reviewed photographs into registry objects, in one transaction, with a batch recording
/// what it did.
///
/// <para>
/// Everything goes through <see cref="FeatureWriteService"/> like any other write: deriving a
/// feature from a picture is a create, not a second creation path, and the derived state that
/// service maintains — containment, ancestry, effective protection, a cave's entrance mirror —
/// is exactly what a hand-rolled insert here would get wrong and the integrity verifier would
/// find months later. What is specific to photographs is only what happens afterwards: every
/// picture of the place is hung on the object it produced, in the order they were taken, so the
/// first one is the object's first picture.
/// </para>
/// </summary>
public sealed class PhotoCommitService(
    SilexGisDbContext db,
    FeatureWriteService writer,
    PhotoCandidateService candidates,
    IAccessService access)
{
    /// <summary>
    /// The most places a single confirmation creates. Lower than a vector import's ceiling
    /// because each of these carries pictures: the objects are the same cost and the attachments
    /// are on top of it.
    /// </summary>
    public const int MaxCommitItems = 250;

    private const string DefaultEntranceTypeCode = "natural";

    private const string DefaultCaveTypeCode = "cave";

    public async Task<ImportCommitResult> CommitAsync(
        IReadOnlyList<StoredFile> files,
        PhotoImportOptions options,
        IReadOnlyCollection<Guid> selection,
        IReadOnlyDictionary<Guid, PhotoDecision> decisions,
        AccessContext ctx,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(selection);

        if (selection.Count > MaxCommitItems)
        {
            throw new ImportCommitException(
                "photo_import.selection_too_large",
                $"A single confirmation creates at most {MaxCommitItems} places; {selection.Count} were selected. "
                + "Confirm them in smaller batches — each one reverts on its own.");
        }

        var built = (await candidates.BuildAsync(files, options, decisions, ctx, ct)).Candidates;
        var byKey = built.ToDictionary(c => c.Key);

        var taxonomies = await Taxonomies.LoadAsync(db, ct);
        var tagIds = options.TagIds.Count == 0
            ? []
            : await db.Tags.AsNoTracking()
                .Where(t => options.TagIds.Contains(t.Id))
                .Select(t => t.Id)
                .ToListAsync(ct);

        var batch = new ImportBatch
        {
            Source = ImportSource.Photos,
            TripLogId = options.TripLogId,
            ConfirmedByUserId = ctx.UserId,
            Mode = ImportBatchMode.Reviewed,
            Options = ImportJson.Serialize(options),
            SkippedCount = built.Count - selection.Count,
        };

        var failures = new List<ImportFailure>();
        var items = new List<ImportBatchItem>();
        var touchedCaves = new HashSet<Guid>();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        foreach (var key in selection)
        {
            if (!byKey.TryGetValue(key, out var candidate))
            {
                failures.Add(new ImportFailure(
                    0, null, "photo_import.candidate_missing",
                    "That group of pictures is no longer in the review — the grouping changed while it was open."));
                continue;
            }

            var decision = decisions.GetValueOrDefault(key) ?? new PhotoDecision();
            try
            {
                var item = await ApplyAsync(
                    candidate, decision, options, taxonomies, tagIds, ctx, touchedCaves, ct);
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
                // One odd place must not cost the other forty. The failure names the pictures it
                // came from, so it can be fixed and confirmed on its own.
                failures.Add(new ImportFailure(
                    0, candidate.Members[0].OriginalName, ex.Code, string.Join("; ", ex.Errors)));
            }
            catch (ImportCommitException ex)
            {
                failures.Add(new ImportFailure(0, candidate.Members[0].OriginalName, ex.Code, ex.Message));
            }
        }

        if (items.Count == 0 && failures.Count > 0)
        {
            await transaction.RollbackAsync(ct);
            throw new ImportCommitException(
                "photo_import.nothing_created",
                $"None of the {failures.Count} selected groups could be created. {failures[0].Reason}");
        }

        db.ImportBatches.Add(batch);
        db.ImportBatchItems.AddRange(items);
        await db.SaveChangesAsync(ct);

        foreach (var caveId in touchedCaves)
        {
            await writer.SyncCaveMirrorAsync(caveId, ct);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new ImportCommitResult(batch, failures);
    }

    // ---------- one candidate ----------

    private async Task<ImportBatchItem?> ApplyAsync(
        PhotoCandidate candidate,
        PhotoDecision decision,
        PhotoImportOptions options,
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
            SourceFileId = candidate.Members[0].FileId,
            Action = decision.Action,
        };
        var hung = new List<Guid>();

        if (decision.Action == ImportDecisionAction.Attach)
        {
            var targetId = decision.AttachToFeatureId
                ?? throw new ImportCommitException(
                    "photo_import.attach_target_missing", "Filing pictures needs the object they are of.");
            var target = await db.Features.AsNoTracking().FirstOrDefaultAsync(f => f.Id == targetId, ct)
                ?? throw new ImportCommitException(
                    "photo_import.target_not_found", "The chosen object no longer exists.");

            // Hanging pictures on something already in the registry is a write on that thing,
            // and is authorised against it rather than against this review. This is the one
            // place where the permission actually bites: creating is the caller's own to do.
            if (!(await access.DecideAsync(ctx, AccessAction.Write, target, ct)).Allowed)
            {
                throw new ImportCommitException(
                    "photo_import.target_forbidden", $"You may not add pictures to '{target.Name}'.");
            }

            item.AttachedToFeatureId = targetId;
            hung.AddRange(Attach(candidate, targetId, RoleFor(target.Kind), ctx));
            hung.AddRange(AttachToTrip(candidate, options, ctx));
            item.AttachmentIds = ImportBatchAttachments.Write(hung);
            return item;
        }

        var kind = decision.Kind ?? options.DefaultKind;
        var point = candidate.Geom
            ?? throw new ImportCommitException(
                "photo_import.position_missing",
                "Nothing places these pictures: the camera recorded no position, no track matched them, "
                + "and nobody has put them on the map.");

        var name = decision.Name ?? Prefixed(options.NamePrefix, candidate.ProposedName);
        decimal? altitude = KeepsElevation(options, decision) && candidate.AltitudeMeters is { } metres
            ? (decimal)metres
            : null;
        var quality = QualityOf(candidate.PositionSource);

        item.FeatureId = kind switch
        {
            ImportTargetKind.Cave => await CreateCaveAsync(
                decision, options, taxonomies, point, name, altitude, quality, ctx, touchedCaves, ct),
            ImportTargetKind.CaveEntrance => await CreateEntranceAsync(
                decision, options, taxonomies, point, name, altitude, quality, ctx, touchedCaves, ct),
            _ => await CreateSurfaceFeatureAsync(decision, options, taxonomies, point, name, ctx, ct),
        };

        hung.AddRange(Attach(candidate, item.FeatureId.Value, RoleFor(kind), ctx));
        hung.AddRange(AttachToTrip(candidate, options, ctx));
        item.AttachmentIds = ImportBatchAttachments.Write(hung);
        // An entrance added to a cave already in the registry produces an entrance feature, and it
        // is the cave that the trip is about: naming the entrance would be invisible to every
        // reader that asks which caves a trip names, and to the cave's own list of trips.
        var namedOnTrip = kind == ImportTargetKind.CaveEntrance
            ? decision.AttachToFeatureId ?? item.FeatureId.Value
            : item.FeatureId.Value;
        await LinkCreatedCaveToTripAsync(kind, namedOnTrip, options, ctx, ct);

        foreach (var tagId in tagIds)
        {
            db.Taggings.Add(new Tagging { TagId = tagId, FeatureId = item.FeatureId.Value, AddedBy = ctx.UserId });
        }

        return item;
    }

    // ---------- attaching the pictures ----------

    /// <summary>
    /// Hangs every picture of the place on the object it produced, in the order they were taken,
    /// so the first picture is the object's first. Answers with the attachments it made, because
    /// undoing the batch has to take exactly those back down.
    /// </summary>
    private List<Guid> Attach(PhotoCandidate candidate, Guid featureId, AttachmentRole role, AccessContext ctx)
    {
        var hung = new List<Guid>(candidate.Members.Count);
        var order = 0;
        foreach (var member in candidate.Members)
        {
            var attachment = new Attachment
            {
                FileId = member.FileId,
                FeatureId = featureId,
                Role = role,
                SortOrder = order++,
                AddedBy = ctx.UserId,
            };
            db.Attachments.Add(attachment);
            hung.Add(attachment.Id);
        }

        return hung;
    }

    /// <summary>
    /// Files the pictures under the trip as well, when the drop named one. This is what makes
    /// "these photographs are from Saturday" a single action instead of a second pass over the
    /// same list — and it is also what places a picture of a cave that has no object of its own
    /// yet, since a trip names the caves it visited.
    /// </summary>
    private List<Guid> AttachToTrip(PhotoCandidate candidate, PhotoImportOptions options, AccessContext ctx)
    {
        if (options.TripLogId is not { } tripId)
        {
            return [];
        }

        var hung = new List<Guid>(candidate.Members.Count);
        foreach (var member in candidate.Members)
        {
            var attachment = new Attachment
            {
                FileId = member.FileId,
                EntityType = AttachedEntityType.TripLog,
                EntityId = tripId,
                Role = AttachmentRole.Other,
                AddedBy = ctx.UserId,
            };
            db.Attachments.Add(attachment);
            hung.Add(attachment.Id);
        }

        return hung;
    }

    /// <summary>
    /// Records on the trip the cave a drop produced — the cave itself, never one of its
    /// entrances. The role says photographed rather than
    /// visited or discovered, because a photograph is exactly what is known here: the drop
    /// carries a picture of the place and nothing that says the trip was the first to reach it
    /// or what it did once there. The claim the import can prove is the one it makes.
    /// </summary>
    private async Task LinkCreatedCaveToTripAsync(
        ImportTargetKind kind, Guid featureId, PhotoImportOptions options, AccessContext ctx, CancellationToken ct)
    {
        if (options.TripLogId is { } tripId && kind is ImportTargetKind.Cave or ImportTargetKind.CaveEntrance)
        {
            await TripRoleLinks.NameFeatureAsync(db, tripId, featureId, "trip-photographed", ctx.UserId, ct);
        }
    }

    private static AttachmentRole RoleFor(ImportTargetKind kind) => kind switch
    {
        ImportTargetKind.Cave or ImportTargetKind.CaveEntrance => AttachmentRole.PhotoEntrance,
        _ => AttachmentRole.PhotoSurface,
    };

    private static AttachmentRole RoleFor(FeatureKind kind) => kind switch
    {
        FeatureKind.Cave or FeatureKind.CaveEntrance => AttachmentRole.PhotoEntrance,
        _ => AttachmentRole.PhotoSurface,
    };

    // ---------- creating ----------

    private async Task<Guid> CreateCaveAsync(
        PhotoDecision decision,
        PhotoImportOptions options,
        Taxonomies taxonomies,
        Point point,
        string? name,
        decimal? altitude,
        PositionQuality quality,
        AccessContext ctx,
        HashSet<Guid> touchedCaves,
        CancellationToken ct)
    {
        RequireCreate(ctx, options);
        var feature = new Feature
        {
            Name = name,
            Geom = point,
            OwnerUserId = ctx.UserId,
            CavingGroupId = options.CavingGroupId,
            Visibility = options.Visibility,
            LocationProtected = options.LocationProtected,
        };
        var cave = new Cave
        {
            CaveTypeId = taxonomies.CaveType(decision.CaveTypeCode ?? options.DefaultCaveTypeCode),
            Altitude = altitude,
        };
        feature.Cave = cave;
        await writer.CreateCaveAsync(feature, cave, [], ct);

        // A cave whose only geometry is the cache of a main entrance it does not have would not
        // draw at all — and a photograph is a position first, so the cave it becomes gets that
        // position as its first entrance.
        var entranceFeature = new Feature { Geom = point };
        var entrance = new CaveEntrance
        {
            CaveFeatureId = feature.Id,
            EntranceTypeId = taxonomies.EntranceType(
                decision.EntranceTypeCode ?? options.DefaultEntranceTypeCode),
            IsMain = true,
            Altitude = altitude,
            PositionQuality = quality,
        };
        await writer.CreateEntranceAsync(entranceFeature, entrance, ct);
        touchedCaves.Add(feature.Id);

        return feature.Id;
    }

    private async Task<Guid> CreateEntranceAsync(
        PhotoDecision decision,
        PhotoImportOptions options,
        Taxonomies taxonomies,
        Point point,
        string? name,
        decimal? altitude,
        PositionQuality quality,
        AccessContext ctx,
        HashSet<Guid> touchedCaves,
        CancellationToken ct)
    {
        if (decision.AttachToFeatureId is not { } caveId)
        {
            // An entrance with no cave named is a cave nobody has entered into the registry yet.
            return await CreateCaveAsync(
                decision, options, taxonomies, point, name, altitude, quality, ctx, touchedCaves, ct);
        }

        var cave = await db.Features.FirstOrDefaultAsync(f => f.Id == caveId && f.Kind == FeatureKind.Cave, ct)
            ?? throw new ImportCommitException("photo_import.cave_not_found", "The chosen cave no longer exists.");
        if (!(await access.DecideAsync(ctx, AccessAction.Write, cave, ct)).Allowed)
        {
            throw new ImportCommitException(
                "photo_import.cave_forbidden", $"You may not add an entrance to '{cave.Name}'.");
        }

        var entranceFeature = new Feature { Name = name, Geom = point };
        var entrance = new CaveEntrance
        {
            CaveFeatureId = caveId,
            EntranceTypeId = taxonomies.EntranceType(
                decision.EntranceTypeCode ?? options.DefaultEntranceTypeCode),
            Altitude = altitude,
            PositionQuality = quality,
        };
        await writer.CreateEntranceAsync(entranceFeature, entrance, ct);
        touchedCaves.Add(caveId);
        return entranceFeature.Id;
    }

    private async Task<Guid> CreateSurfaceFeatureAsync(
        PhotoDecision decision,
        PhotoImportOptions options,
        Taxonomies taxonomies,
        Point point,
        string? name,
        AccessContext ctx,
        CancellationToken ct)
    {
        RequireCreate(ctx, options);
        var feature = new Feature
        {
            Name = name,
            FeatureTypeId = taxonomies.FeatureType(
                decision.FeatureTypeCode ?? options.DefaultFeatureTypeCode),
            Geom = point,
            OwnerUserId = ctx.UserId,
            CavingGroupId = options.CavingGroupId,
            Visibility = options.Visibility,
            LocationProtected = options.LocationProtected,
        };
        await writer.CreateGenericAsync(feature, [], ct);
        return feature.Id;
    }

    private static void RequireCreate(AccessContext ctx, PhotoImportOptions options)
    {
        if (!CreateRules.MayCreate(ctx, AccessDomain.Features, options.CavingGroupId))
        {
            throw new ImportCommitException(CreateRules.ForbiddenCode, "You may not create features here.");
        }
    }

    /// <summary>
    /// How the entrance's position was arrived at, said honestly. A camera's own fix is a GPS
    /// reading and is recorded as one; a picture placed by matching a track, or by somebody
    /// dragging it onto the map, is an estimate — and an estimate that claimed to be a GPS
    /// reading is exactly the kind of number somebody walks into a forest trusting.
    /// </summary>
    private static PositionQuality QualityOf(PhotoPositionSource source) => source switch
    {
        PhotoPositionSource.Exif => PositionQuality.Gps,
        _ => PositionQuality.Estimated,
    };

    private static bool KeepsElevation(PhotoImportOptions options, PhotoDecision decision) =>
        decision.KeepElevation ?? options.Elevation == ImportElevationPolicy.Keep;

    private static string? Prefixed(string? prefix, string? name) =>
        ImportNameCleaner.WithPrefix(prefix, name);

    // ---------- taxonomy codes ----------

    /// <summary>
    /// The taxonomy rows a commit needs, read once. Codes rather than identities, for the same
    /// reason the vector importer uses them: a code is what survives being sent to another
    /// installation, and one that resolves to nothing names a taxonomy this one does not have.
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
                "photo_import.feature_type_missing", "A surface feature needs a kind, and none was chosen.")
            : Resolve(FeatureTypes, code, "feature type");

        private static long Resolve(IReadOnlyDictionary<string, long> map, string code, string what) =>
            map.TryGetValue(code, out var id)
                ? id
                : throw new ImportCommitException(
                    "photo_import.type_unknown", $"This installation has no {what} called '{code}'.");
    }
}
