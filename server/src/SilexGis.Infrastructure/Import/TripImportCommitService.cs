// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Domain.Import.TripCsv;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Trips;

namespace SilexGis.Infrastructure.Import;

/// <summary>
/// The stable code and the readable reason of the three refusals a row can meet, read the same
/// way whichever of them it was, so the failure list has one shape.
/// </summary>
internal static class TripImportRowRefusal
{
    public static string Code(this Exception e) => e switch
    {
        TripWriteException x => x.Code,
        FeatureWriteException x => x.Code,
        TripImportCommitException x => x.Code,
        _ => "trip_import.row_failed",
    };

    public static string Reason(this Exception e) =>
        e is FeatureWriteException x ? string.Join("; ", x.Errors) : e.Message;
}

/// <summary>One row of the sheet that could not be recorded, and why.</summary>
/// <param name="Line">The row's physical line in the file, the number its problems carry.</param>
public sealed record TripImportFailure(int Line, string? Title, string Code, string Reason);

/// <summary>
/// What one confirmation did: the batch it wrote, what it counted, and the rows it could not
/// write. Trips and features are counted apart because they answer different questions — how much
/// of the sheet landed, and how much of the registry the confirmation was allowed to grow.
/// </summary>
public sealed record TripImportCommitResult(
    ImportBatch Batch,
    int CreatedTripCount,
    int CreatedFeatureCount,
    IReadOnlyList<TripImportFailure> Failures);

/// <summary>A confirmation refused as a whole, rather than one row of it.</summary>
public sealed class TripImportCommitException(string code, string message) : Exception(message)
{
    /// <summary>Nothing was chosen, so there is nothing to confirm.</summary>
    public const string SelectionEmptyCode = "trip_import.selection_empty";

    /// <summary>More rows than one confirmation records.</summary>
    public const string SelectionTooLargeCode = "trip_import.selection_too_large";

    /// <summary>Every chosen row failed, so the batch would record only failures.</summary>
    public const string NothingCreatedCode = "trip_import.nothing_created";

    /// <summary>A chosen line is not a readable row of the sheet as it reads now.</summary>
    public const string RowMissingCode = "trip_import.row_missing";

    /// <summary>The sheet says a trip happened but not when, or not what it was called.</summary>
    public const string RowIncompleteCode = "trip_import.row_incomplete";

    /// <summary>The installation has no area kind to file a massif or a sub-area under.</summary>
    public const string KindMissingCode = "trip_import.kind_missing";

    /// <summary>The installation has no ordinary cave kind to create a named cave as.</summary>
    public const string CaveTypeMissingCode = "trip_import.cave_type_missing";

    public string Code { get; } = code;
}

/// <summary>
/// Turns a reviewed trip spreadsheet into trips, in one transaction, with a batch recording what
/// it did so the whole import can be taken back in one act.
///
/// <para>
/// Three properties are the whole of this class and each of them is a rule somebody could
/// plausibly write differently, so each is stated here once.
/// </para>
/// <para>
/// It is silent. A trip somebody writes up names the people who were on it and tells them so. A
/// spreadsheet of a club's last fifteen years names hundreds of people on hundreds of trips, and
/// telling all of them is not a louder version of the same thing, it is a different event and one
/// nobody asked for. So every trip is created with the write path's silent argument, passed
/// explicitly rather than relied on: the announcing value is the default, and a created trip that
/// is already finished is exactly the shape that would otherwise announce.
/// </para>
/// <para>
/// One bad row does not cost the rest. A row that cannot be recorded becomes a failure line
/// carrying its physical line number, its code and its reason, and the confirmation carries on.
/// Only a confirmation in which nothing at all landed is refused outright, because a batch
/// recording nothing but failures is a row nobody can act on when the failures themselves are the
/// answer. What a failed row half-created is unpicked from the unit of work before the next row
/// starts, so a row that fell over between creating a cave and creating its trip leaves neither.
/// </para>
/// <para>
/// It creates nothing through a second path. Trips go through the one trip write service, caves
/// and areas through the one feature write service, and people through the roster reconciliation
/// the trip write already performs. Each kind of creation is behind its own switch and every
/// switch is off unless the reviewer turned it on; matching happens either way, because a switch
/// decides only what becomes of what did not match.
/// </para>
/// </summary>
public sealed class TripImportCommitService(
    SilexGisDbContext db,
    TripCsvFileReader reader,
    TripImportResolver resolver,
    TripLogWriteService trips,
    FeatureWriteService features,
    IOptions<ImportLimitOptions> limits)
{
    /// <summary>The area kind a massif column becomes: a stretch of country, with no geometry yet.</summary>
    private const string MassifTypeCode = TripImportKinds.Massif;

    /// <summary>
    /// The area kind a sub-area column becomes. A valley or a sector inside a massif is a karst
    /// area rather than a massif of its own, and it is a plain area rather than a work area: a
    /// work area is a club declaring that it works somewhere, which a column of somebody's
    /// spreadsheet is not saying.
    /// </summary>
    private const string SubAreaTypeCode = TripImportKinds.SubArea;

    /// <summary>The cave kind a name with nothing else known about it becomes.</summary>
    private const string CaveTypeCode = TripImportKinds.Cave;

    /// <summary>
    /// The relation an imported trip names its massif and its sub-area through. The shipped
    /// "Worked in" role, because that is what the sheet's place columns say — a trip happened in
    /// that stretch of country — and because a relation nothing else writes would be a second
    /// answer to a question the trip model already answers.
    /// </summary>
    private const string AreaRoleCode = "trip-work-area";

    public async Task<TripImportCommitResult> CommitAsync(
        StoredFile file,
        TripImportOptions options,
        IReadOnlyCollection<int> selection,
        IReadOnlyDictionary<int, TripImportDecision> decisions,
        AccessContext ctx,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(ctx);

        if (selection.Count == 0)
        {
            throw new TripImportCommitException(
                TripImportCommitException.SelectionEmptyCode, "No rows were chosen.");
        }

        var ceiling = limits.Value.MaxCommitItems;
        if (selection.Count > ceiling)
        {
            throw new TripImportCommitException(
                TripImportCommitException.SelectionTooLargeCode,
                $"A single confirmation records at most {ceiling} trips; {selection.Count} were chosen. "
                + "Confirm them in smaller batches, each one reverts on its own.");
        }

        // Asked again here and not only at the route, because a group the caller may read is not
        // a group they may bind content to, and this is the layer that knows what is being bound.
        RequireCreate(ctx, options);

        // The sheet is read again rather than trusted from the preview: what the reviewer
        // approved is a set of line numbers under a set of options, and reading the file under
        // those options is the only thing that turns them back into rows.
        var parsed = await reader.ParseAsync(file, options, ct);
        var readable = parsed.Rows.Where(r => !r.HasError).ToDictionary(r => r.Line);
        var chosen = selection.Distinct().Order().ToList();

        // Resolved over the rows this confirmation will actually try to record, and no others. A
        // vocabulary is append-only and an undo deliberately leaves what a confirmation added to
        // it, so a type word appearing only on a row nobody chose — or on a row the reading
        // refused — must not permanently grow a list every trip form in the installation shows.
        var wanted = chosen
            .Where(line => decisions.GetValueOrDefault(line)?.Action != TripImportRowAction.Skip)
            .Select(readable.GetValueOrDefault)
            .Where(row => row is not null)
            .Select(row => row!)
            .ToList();
        var resolution = await resolver.ResolveAsync(wanted, options, ctx, ct);
        var (participantRole, proposerRole) = await TripLogWriteService.ShippedRosterRolesAsync(db, ct);

        var batch = new ImportBatch
        {
            Source = ImportSource.TripCsv,
            ConfirmedByUserId = ctx.UserId,
            Mode = ImportBatchMode.Reviewed,
            Options = ImportJson.Serialize(options),
        };

        var items = new List<ImportBatchItem>();
        var failures = new List<TripImportFailure>();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // The vocabulary grows once for the whole confirmation rather than once per row: a type
        // written on forty rows is one new term, and creating it per row would depend on the
        // order the rows happen to be in.
        var types = await GrowTripTypesAsync(resolution, options, ct);
        var areas = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var caves = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var skipped = 0;

        foreach (var line in chosen)
        {
            if (decisions.GetValueOrDefault(line)?.Action == TripImportRowAction.Skip)
            {
                skipped++;
                continue;
            }

            var row = readable.GetValueOrDefault(line);
            if (row is null || !resolution.Rows.TryGetValue(line, out var meaning))
            {
                // A line the reviewer chose that the file no longer reads as an importable row:
                // the options moved, or the upload was replaced. Reported, never guessed.
                failures.Add(new TripImportFailure(
                    line, null, TripImportCommitException.RowMissingCode,
                    "That row is not one the sheet reads as importable under the current choices."));
                continue;
            }

            // A savepoint per row, because a row is not all in the change tracker: the places it
            // creates have to reach the database before the trip write can check that the caves
            // it names exist, and a write that has happened cannot be taken back by detaching
            // anything. The savepoint takes back what reached the database and the mark takes
            // back what had not, which between them leave the confirmation as if the row had
            // never been read.
            //
            // Released the moment the row lands, and that is not tidiness. A savepoint is a
            // PostgreSQL subtransaction nested in the one before it, so a confirmation of a few
            // thousand rows that never released one would end thousands of levels deep. Past
            // sixty-four the backend's subtransaction cache overflows and every *other* backend
            // in the cluster starts consulting pg_subtrans to judge what this transaction wrote —
            // an installation-wide slowdown lasting as long as the import.
            var savepoint = $"trip_row_{line}";
            await transaction.CreateSavepointAsync(savepoint, ct);
            var mark = new UnitOfWorkMark(db);

            // What the caches held before this row, so a row that fails takes back only the
            // places it invented. Clearing them outright would make a later row naming a place an
            // earlier row already created invent a second copy of it — and split the hierarchy,
            // because its sub-area would then nest under the second massif.
            var cavesBefore = new Dictionary<string, Guid>(caves, StringComparer.Ordinal);
            var areasBefore = new Dictionary<string, Guid>(areas, StringComparer.Ordinal);
            try
            {
                items.AddRange(await RecordAsync(
                    row, meaning, options, types, areas, caves, participantRole, proposerRole, ctx, ct));
                await transaction.ReleaseSavepointAsync(savepoint, ct);
            }
            catch (Exception e) when (e is TripWriteException or FeatureWriteException or TripImportCommitException)
            {
                await transaction.RollbackToSavepointAsync(savepoint, ct);

                // Released on this branch too, and for the same reason as on the one above:
                // rolling back to a savepoint does not destroy it, it re-establishes its
                // subtransaction, so a failed row that only rolled back would leave the
                // confirmation one level deeper than it found it. A sheet with hundreds of
                // refused rows would then commit hundreds of levels deep — the accumulation this
                // pair of calls exists to prevent, arriving through the failure path instead.
                await transaction.ReleaseSavepointAsync(savepoint, ct);
                mark.Undo();
                Restore(caves, cavesBefore);
                Restore(areas, areasBefore);
                failures.Add(new TripImportFailure(line, row.Title, e.Code(), e.Reason()));
            }
        }

        if (items.Count == 0)
        {
            // Nothing landed. A batch recording only failures is a row nobody can act on and the
            // failures themselves are the answer; a batch recording nothing at all because every
            // chosen row was set aside is worse still, because committing it would spend a
            // review that recorded not one trip. Either way the transaction goes back, which is
            // what leaves the reviewer's saved decisions where they were.
            await transaction.RollbackAsync(ct);
            throw new TripImportCommitException(
                TripImportCommitException.NothingCreatedCode,
                failures.Count > 0
                    ? $"None of the {failures.Count} chosen rows could be recorded. {failures[0].Reason}"
                    : "Every chosen row was set aside, so nothing would be recorded.");
        }

        foreach (var item in items)
        {
            item.ImportBatchId = batch.Id;
        }

        batch.CreatedCount = items.Count;
        batch.SkippedCount = skipped;
        db.ImportBatches.Add(batch);
        db.ImportBatchItems.AddRange(items);
        await db.SaveChangesAsync(ct);

        // The review is spent: its decisions describe rows that are now trips, and leaving it
        // would offer to record them a second time.
        await db.TripImportSessions
            .Where(s => s.StoredFileId == file.Id && s.UserId == ctx.UserId)
            .ExecuteDeleteAsync(ct);

        await transaction.CommitAsync(ct);
        return new TripImportCommitResult(
            batch,
            items.Count(i => i.TripLogId != null),
            items.Count(i => i.FeatureId != null),
            failures);
    }

    /// <summary>Puts a cache back the way it was before a row that failed touched it.</summary>
    private static void Restore(Dictionary<string, Guid> cache, Dictionary<string, Guid> before)
    {
        cache.Clear();
        foreach (var (key, value) in before)
        {
            cache[key] = value;
        }
    }

    // ---------- one row ----------

    private async Task<List<ImportBatchItem>> RecordAsync(
        TripCsvRow row,
        TripImportRowResolution meaning,
        TripImportOptions options,
        IReadOnlyDictionary<string, long> types,
        Dictionary<string, Guid> areas,
        Dictionary<string, Guid> caves,
        long participantRole,
        long proposerRole,
        AccessContext ctx,
        CancellationToken ct)
    {
        if (row.StartDate is not { } start || string.IsNullOrWhiteSpace(row.Title))
        {
            throw new TripImportCommitException(
                TripImportCommitException.RowIncompleteCode,
                "A trip needs a title and a date this row does not carry.");
        }

        var created = new List<ImportBatchItem>();

        // Where the row says it was, as far as this installation can be made to hold it. The
        // sub-area sits inside the massif through the containment hierarchy every feature
        // already has rather than as a second kind of thing, and a cave this row invents is
        // filed under the finer of the two that exists.
        var massif = await AreaAsync(meaning.Massif, MassifTypeCode, null, options, areas, ctx, created, ct);
        var subArea = await AreaAsync(meaning.SubArea, SubAreaTypeCode, massif, options, areas, ctx, created, ct);
        var caveParent = subArea ?? massif;

        var caveIds = new List<Guid>();
        foreach (var cave in meaning.Caves)
        {
            if (cave.State == TripImportMatchState.Matched && cave.FeatureId is { } matched)
            {
                caveIds.Add(matched);
                continue;
            }

            if (!cave.WillCreate)
            {
                continue;
            }

            var key = TripImportNames.Key(cave.Source);
            if (!caves.TryGetValue(key, out var id))
            {
                id = await CreateCaveAsync(cave.Source, caveParent, options, ctx, ct);
                caves[key] = id;
                created.Add(new ImportBatchItem
                {
                    FeatureId = id,
                    Action = ImportDecisionAction.Create,
                    SourceProperties = ImportJson.Serialize(new { row.Line, Cave = cave.Source }),
                });
            }

            caveIds.Add(id);
        }

        if (created.Count > 0)
        {
            // The trip write checks that every cave it is asked to name exists and may be read,
            // and it checks in the database rather than in the change tracker. A cave this row
            // has just invented has to be there before that question is asked.
            await db.SaveChangesAsync(ct);
        }

        var input = new TripWriteInput
        {
            Title = row.Title!.Trim(),
            TripDate = start,
            TripDateEnd = row.EndDate,
            TripTypeId = TypeIdOf(meaning.TripType, types),
            LocationText = meaning.LocationNote,
            Description = Trimmed(row.Details),
            Results = Remarks(row, meaning),
            CaveIds = caveIds,
            Participants = [.. Roster(meaning.Participants, participantRole)],
            Proposers = [.. Roster(meaning.Proposers, proposerRole)],
            CavingGroupId = options.CavingGroupId,
            Visibility = options.Visibility,
        };

        // Silent, explicitly. The default announces, and an imported trip is finished rather
        // than a draft, which is precisely the shape that would otherwise send a message to
        // every person the sheet ever named.
        var trip = await trips.CreateAsync(
            TripCreationIntent.Report, input, ctx, ctx.UserId, TripWriteNotice.Silent, ct);

        // A trip out of a club's records is something that already happened, not something
        // somebody is still writing.
        trip.State = ActivityState.Done;

        // The row is finished, so it is written. Row by row rather than once at the end, because
        // the row after this one may fail and a savepoint can only take back what reached the
        // database; the transaction still makes the confirmation one act.
        await db.SaveChangesAsync(ct);

        // Where the trip was, said as a relation rather than only as words. Without this the
        // sheet's place columns reach nothing at all when they match — the location note leaves
        // out anything that was matched or created, on the understanding that a link says it —
        // and a row whose only statement about where it went is its massif would lose that
        // statement entirely. Named after the trip is saved, because a link is two rows pointing
        // at things that have to exist.
        var placed = new[] { massif, subArea }
            .Where(a => a is not null).Select(a => a!.Value).Distinct().ToList();
        foreach (var areaId in placed)
        {
            await TripRoleLinks.NameFeatureAsync(db, trip.Id, areaId, AreaRoleCode, ctx.UserId, ct);
        }

        if (placed.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        created.Add(new ImportBatchItem
        {
            TripLogId = trip.Id,
            Action = ImportDecisionAction.Create,
            SourceProperties = ImportJson.Serialize(row),
        });

        return created;
    }

    /// <summary>
    /// The people of one list, as the roster reconciliation wants them. A name that matched is
    /// sent as the person it matched; a name a switch says to create is sent as a name; and
    /// everything else, ambiguous or too little of a name to make a person out of under the
    /// choices this import was reviewed under, is sent as
    /// nothing at all, because the reconciliation resolves a repeated name to the oldest person
    /// who holds it. That is the right answer at a keyboard, where somebody knows who they mean,
    /// and the wrong one here, where the guess becomes a claim about who was underground on a day
    /// years ago. Those names are not lost: they are in the trip's own words.
    /// </summary>
    private static IEnumerable<TripRosterEntry> Roster(
        IReadOnlyList<TripImportPersonMatch> people, long roleId)
    {
        foreach (var person in people)
        {
            if (person.State == TripImportMatchState.Matched && person.CaverId is { } caverId)
            {
                yield return new TripRosterEntry { CaverId = caverId, RoleId = roleId };
            }
            else if (person.WillCreate)
            {
                yield return new TripRosterEntry { NewCaverName = person.Source.Trim(), RoleId = roleId };
            }
        }
    }

    /// <summary>
    /// Everything else the row said, in the trip's own words. The sheet's own remarks column goes
    /// in with the rest: showing a club's data-quality notes to the people who can act on them is
    /// the point of keeping the column at all, and a column nothing else claimed is still
    /// something somebody typed on purpose.
    /// </summary>
    private static string? Remarks(TripCsvRow row, TripImportRowResolution meaning)
    {
        var parts = new List<string>();
        Add(row.Details2);
        Add(row.Errors);

        var unnamed = meaning.Participants.Concat(meaning.Proposers)
            .Where(p => p.State != TripImportMatchState.Matched && !p.WillCreate)
            .Select(p => p.Source.Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (unnamed.Count > 0)
        {
            // The names alone, with no sentence around them. What goes in here is the trip's own
            // report text — a field people read and edit — and a label written in one language at
            // import time cannot be translated afterwards by anything: a thousand imported trips
            // would each carry an English sentence forever, on an installation whose records are
            // in Romanian. The names are what the sheet said; a caption for them is the client's
            // to add from a translated key.
            parts.Add(string.Join(", ", unnamed));
        }

        foreach (var (column, value) in row.Unmapped)
        {
            Add($"{column}: {value}");
        }

        return parts.Count == 0 ? null : string.Join("\n\n", parts);

        void Add(string? value)
        {
            var trimmed = value?.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                parts.Add(trimmed);
            }
        }
    }

    private static string? Trimmed(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static long? TypeIdOf(TripImportTermMatch? match, IReadOnlyDictionary<string, long> created)
    {
        if (match is null)
        {
            return null;
        }

        if (match.State == TripImportMatchState.Matched)
        {
            return match.Id;
        }

        return created.TryGetValue(TripImportNames.Key(match.Source), out var id) ? id : null;
    }

    // ---------- what a confirmation may add ----------

    /// <summary>
    /// Adds the type values nothing here answered to, when the switch says so. Append-only: a
    /// term is added, never renamed and never merged into another, because a vocabulary an
    /// importer may edit is a vocabulary one bad sheet can rewrite.
    /// </summary>
    private async Task<Dictionary<string, long>> GrowTripTypesAsync(
        TripImportResolutionSet resolution, TripImportOptions options, CancellationToken ct)
    {
        var created = new Dictionary<string, long>(StringComparer.Ordinal);
        if (!options.CreateMissingTripTypes || resolution.NewTripTypes.Count == 0)
        {
            return created;
        }

        var sort = await db.TripTypes.AnyAsync(ct) ? await db.TripTypes.MaxAsync(t => t.SortOrder, ct) : 0;
        var taken = new HashSet<string>(
            await db.TripTypes.Select(t => t.Code).ToListAsync(ct), StringComparer.OrdinalIgnoreCase);

        var added = new List<(string Key, TripType Type)>();
        foreach (var name in resolution.NewTripTypes)
        {
            // The reviewer's spelling if they gave one, the sheet's otherwise. This is the only
            // moment the name can be got right: the vocabulary is append-only, so nothing here
            // renames a term afterwards, and every trip form in the installation shows the list.
            var type = new TripType
            {
                Code = FreeCode(name, taken),
                Name = options.ChosenTripTypeName(name) ?? name.Trim(),
                SortOrder = ++sort,
            };
            db.TripTypes.Add(type);
            added.Add((TripImportNames.Key(name), type));
        }

        // Saved here rather than with the batch, because the rows that follow need the keys and
        // a taxonomy key is assigned by the database rather than on construction. It is the same
        // transaction, so a confirmation that goes on to fail takes these back with it.
        await db.SaveChangesAsync(ct);
        foreach (var (key, type) in added)
        {
            created[key] = type.Id;
        }

        return created;
    }

    /// <summary>A code nothing else here holds, derived from the name the sheet wrote.</summary>
    private static string FreeCode(string name, HashSet<string> taken)
    {
        var stem = new string([.. TripImportNames.Key(name).Select(c => char.IsLetterOrDigit(c) ? c : '_')])
            .Trim('_');
        if (stem.Length == 0)
        {
            stem = "imported";
        }

        stem = stem[..Math.Min(stem.Length, 48)];
        var candidate = stem;
        var suffix = 1;
        while (!taken.Add(candidate))
        {
            candidate = $"{stem}_{++suffix}";
        }

        return candidate;
    }

    /// <summary>
    /// The area a column names, created when the switch says so and nothing answered to it. An
    /// area created here carries no geometry: the sheet says a trip was in a massif and says
    /// nothing whatever about where that massif's boundary runs, and a shape invented to fill the
    /// column would be a claim the source never made.
    /// </summary>
    private async Task<Guid?> AreaAsync(
        TripImportFeatureMatch? match,
        string typeCode,
        Guid? parent,
        TripImportOptions options,
        Dictionary<string, Guid> cache,
        AccessContext ctx,
        List<ImportBatchItem> created,
        CancellationToken ct)
    {
        if (match is null)
        {
            return null;
        }

        if (match.State == TripImportMatchState.Matched && match.FeatureId is { } matched)
        {
            return matched;
        }

        if (!match.WillCreate)
        {
            return null;
        }

        var key = $"{typeCode}|{parent}|{TripImportNames.Key(match.Source)}";
        if (cache.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var feature = new Feature
        {
            Name = match.Source.Trim(),
            FeatureTypeId = await FeatureTypeIdAsync(typeCode, ct),
            OwnerUserId = ctx.UserId,
            CavingGroupId = options.CavingGroupId,
            Visibility = options.Visibility,
        };

        IReadOnlyList<ParentSpec> parents = parent is { } parentId ? [new ParentSpec(parentId, true)] : [];
        await features.CreateGenericAsync(feature, parents, ct);
        cache[key] = feature.Id;
        created.Add(new ImportBatchItem
        {
            FeatureId = feature.Id,
            Action = ImportDecisionAction.Create,
            SourceProperties = ImportJson.Serialize(new { Area = match.Source, Kind = typeCode }),
        });

        return feature.Id;
    }

    private async Task<Guid> CreateCaveAsync(
        string name, Guid? parent, TripImportOptions options, AccessContext ctx, CancellationToken ct)
    {
        var feature = new Feature
        {
            Name = name.Trim(),
            OwnerUserId = ctx.UserId,
            CavingGroupId = options.CavingGroupId,
            Visibility = options.Visibility,
        };

        var cave = new Cave { CaveTypeId = await CaveTypeIdAsync(ct) };
        feature.Cave = cave;
        IReadOnlyList<ParentSpec> parents = parent is { } parentId ? [new ParentSpec(parentId, true)] : [];
        await features.CreateCaveAsync(feature, cave, parents, ct);
        return feature.Id;
    }

    private async Task<long> FeatureTypeIdAsync(string code, CancellationToken ct)
    {
        var id = await db.FeatureTypes.Where(t => t.Code == code)
            .Select(t => (long?)t.Id).FirstOrDefaultAsync(ct);
        return id ?? throw new TripImportCommitException(
            TripImportCommitException.KindMissingCode,
            $"This installation has no '{code}' kind to file an area under.");
    }

    private async Task<long> CaveTypeIdAsync(CancellationToken ct)
    {
        var id = await db.CaveTypes.Where(t => t.Code == CaveTypeCode)
            .Select(t => (long?)t.Id).FirstOrDefaultAsync(ct);
        return id ?? throw new TripImportCommitException(
            TripImportCommitException.CaveTypeMissingCode,
            "This installation has no ordinary cave kind to create caves as.");
    }

    private static void RequireCreate(AccessContext ctx, TripImportOptions options)
    {
        if (TripImportCreateRights.Refusal(ctx, options) is { } refusal)
        {
            throw new TripImportCommitException(refusal.Code, refusal.Message);
        }
    }

    /// <summary>
    /// Where the unit of work stood before a row started, so a row that fails part-way can be
    /// unpicked from it.
    ///
    /// <para>
    /// A confirmation is one transaction and one change tracker, and the trip write deliberately
    /// does not save, which is what lets a thousand trips commit together. The cost is that a row
    /// which creates a cave and then fails to create its trip has already put the cave in the
    /// unit of work, and saving the batch would save the cave with it: an orphan nothing points
    /// at, inside a batch that reports the row as a failure.
    /// </para>
    /// <para>
    /// The half of that which is easy to get wrong is what the row created <em>and saved</em>
    /// before it failed. A row saves part-way on purpose — the places it invents have to be in
    /// the database before the trip write can ask whether the caves it names are there — and a
    /// successful save leaves everything it wrote tracked as unchanged. The savepoint then takes
    /// those rows back out of the database, so a mark that looked only at what was still pending
    /// would leave the tracker holding features, edges and closure rows that no longer exist.
    /// Such an entry writes no SQL of its own, which is why the damage is quiet: it is read back
    /// as live by everything that merges tracked state over stored state, and the hierarchy
    /// recompute does exactly that when it works out what the edge set holds and which features
    /// are protected. So this records <em>every</em> tracked entry whatever state it is in, and
    /// takes back everything the row introduced whatever state the row left it in.
    /// </para>
    /// <para>
    /// It also puts an entry the row found and then changed back the way it was, rather than
    /// assuming there is none. Today there is none: this path only ever creates, and the
    /// recompute it drives stamps the containment subtree of a brand-new identifier, which has no
    /// descendants and so reaches nothing that existed before. But that is a property of what the
    /// row currently does rather than of the mark, and a row that later learns to move a feature
    /// or flip its protection would otherwise leave those edits sitting in the tracker for the
    /// next row's save to write.
    /// </para>
    /// </summary>
    private sealed class UnitOfWorkMark(SilexGisDbContext db)
    {
        // Keyed by the entity and compared by reference. The tracker hands out a newly
        // constructed entry object for the same entity on every call, so a set of entries is a
        // set nothing can be looked up in afterwards.
        private readonly Dictionary<object, EntityState> before = db.ChangeTracker.Entries()
            .ToDictionary(e => e.Entity, e => e.State, ReferenceEqualityComparer.Instance);

        public void Undo()
        {
            foreach (var entry in db.ChangeTracker.Entries().ToList())
            {
                if (!before.TryGetValue(entry.Entity, out var was))
                {
                    entry.State = EntityState.Detached;
                }
                else if (entry.State != was)
                {
                    // Values before state: putting the state back on its own would leave the
                    // row's edits in the entry, for the next save to write against a row nobody
                    // asked to change.
                    entry.CurrentValues.SetValues(entry.OriginalValues);
                    entry.State = was;
                }
            }
        }
    }
}
