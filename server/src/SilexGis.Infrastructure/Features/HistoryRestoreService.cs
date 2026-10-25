// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Features;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Features;

/// <summary>A field a restore left as it is, and why (<see cref="HistoryRestoreReasons"/>).</summary>
public sealed record HistoryRestoreSkip(string Field, string Reason);

/// <summary>How a restore request was answered as a whole.</summary>
public enum HistoryRestoreStatus
{
    /// <summary>Answered field by field: some put back, some skipped (either list may be empty).</summary>
    Done,

    /// <summary>No such feature, or one the caller may not read; or no such change of it.</summary>
    NotFound,

    /// <summary>The caller may read the feature but not change it.</summary>
    Forbidden,

    /// <summary>The change cannot be restored from at all, or the restored values fail validation.</summary>
    Invalid,
}

/// <summary>The answer to a restore: the fields put back and the ones skipped, or why none could be.</summary>
public sealed record HistoryRestoreOutcome(
    HistoryRestoreStatus Status,
    IReadOnlyList<string> Applied,
    IReadOnlyList<HistoryRestoreSkip> Skipped,
    string? Code = null,
    string? Detail = null)
{
    public static HistoryRestoreOutcome Refused(HistoryRestoreStatus status, string code, string? detail = null) =>
        new(status, [], [], code, detail);
}

/// <summary>
/// Puts fields of a feature back to the value they had before one recorded change.
/// </summary>
/// <remarks>
/// <para>
/// A restore is an ordinary edit whose values come out of the history, so it is held to the
/// ordinary edit's rules: the Write permission on the feature (on its cave, for an entrance, as
/// the entrance's own edit asks), no position written by a caller without exact view, the
/// fields the kind's edit form writes and no others (<see cref="FeatureHistoryFields"/>), and the
/// write itself through <see cref="FeatureWriteService"/>, which keeps the cave's main entrance
/// and its point and validates the property document. It runs as a change set of its own, from
/// <see cref="ChangeSource.Restore"/>, so the timeline says what put the value back.
/// </para>
/// <para>
/// A caller cannot ask for a value their own history hides. The change is read through the same
/// redaction the timeline applies to that caller, and a field it removes is reported as
/// redacted rather than written — otherwise a restore would be a way to move a value the caller
/// was never allowed to see.
/// </para>
/// <para>
/// A large shape is recorded in the history only as a summary. Its full value is taken from the
/// copy kept beside the change, and only when that copy is the shape the summary fingerprints;
/// older history has no copy, and the field is reported as having no pre-image.
/// </para>
/// </remarks>
public sealed class HistoryRestoreService(
    SilexGisDbContext db,
    FeatureWriteService writer,
    FeatureProtection protection,
    IAccessService access,
    IChangeContext change)
{
    public const string EntityNotFoundCode = "history.entity_not_found";
    public const string EntryNotFoundCode = "history.entry_not_found";
    public const string EntryNotRestorableCode = "history.entry_not_restorable";

    public async Task<HistoryRestoreOutcome> RestoreAsync(
        AccessContext ctx,
        Guid featureId,
        long auditEntryId,
        IReadOnlyList<string>? fields,
        CancellationToken ct = default)
    {
        var feature = await db.Features
            .Include(f => f.Cave)
            .Include(f => f.Entrance)
            .FirstOrDefaultAsync(f => f.Id == featureId, ct);

        // An entrance is read and changed under its cave's permission, exactly as its own edit is.
        var governing = feature?.Entrance is { } own
            ? await db.Features.FirstOrDefaultAsync(f => f.Id == own.CaveFeatureId, ct)
            : feature;
        if (feature is null || governing is null
            || !(await access.DecideAsync(ctx, AccessAction.Read, governing, ct)).Allowed)
        {
            return HistoryRestoreOutcome.Refused(HistoryRestoreStatus.NotFound, EntityNotFoundCode);
        }

        var key = featureId.ToString();
        var entry = await db.AuditEntries.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == auditEntryId && a.EntityId == key, ct);
        if (entry is null || entry.EntityType is not { } entityType || !FeatureAudit.IsFeatureType(entityType))
        {
            return HistoryRestoreOutcome.Refused(HistoryRestoreStatus.NotFound, EntryNotFoundCode);
        }

        if (!(await access.DecideAsync(ctx, AccessAction.Write, governing, ct)).Allowed)
        {
            return HistoryRestoreOutcome.Refused(HistoryRestoreStatus.Forbidden, "acl.forbidden");
        }

        if (entry.Action != AuditActions.Updated)
        {
            return HistoryRestoreOutcome.Refused(
                HistoryRestoreStatus.Invalid,
                EntryNotRestorableCode,
                "Only an edit can be put back; a creation has no earlier value, and a deletion is undone by restoring the feature.");
        }

        var changes = ChangesOf(entry);
        var (exact, shown, redactedSet) = await ViewAsync(ctx, feature, entityType, changes, ct);

        List<string> requested = fields is { Count: > 0 }
            ? fields.Distinct(StringComparer.Ordinal).ToList()
            : [.. changes.Select(kv => kv.Key)
                .Where(k => (shown?.ContainsKey(k) ?? false) || redactedSet.Contains(k))];

        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        var skipped = new List<HistoryRestoreSkip>();
        foreach (var field in requested)
        {
            var reason = await DecideAsync(feature, entry, changes, field, exact, redactedSet, values, ct);
            if (reason is not null)
            {
                skipped.Add(new HistoryRestoreSkip(field, reason));
            }
        }

        if (values.Count == 0)
        {
            return new HistoryRestoreOutcome(HistoryRestoreStatus.Done, [], skipped);
        }

        change.Begin(ChangeSource.Restore);
        var heldMainRole = feature.Entrance?.IsMain == true;
        try
        {
            await writer.ApplyHistoricalValuesAsync(feature, values, ct);
            if (heldMainRole && feature.Entrance is { IsMain: false } entrance
                && await DemotedAlongsideAsync(ctx, entry, entrance, ct) is { } previous)
            {
                await writer.SetMainEntranceAsync(entrance.CaveFeatureId, previous, ct);
            }
        }
        catch (FeatureWriteException ex)
        {
            return HistoryRestoreOutcome.Refused(HistoryRestoreStatus.Invalid, ex.Code, string.Join("; ", ex.Errors));
        }

        await db.SaveChangesAsync(ct);
        return new HistoryRestoreOutcome(HistoryRestoreStatus.Done, [.. values.Keys], skipped);
    }

    /// <summary>
    /// The entrance whose main role the restored change took away when it promoted this one —
    /// recorded in the same change set — when it is still a live entrance of the same cave and the
    /// caller sees it and the cave exactly; else null, and the cave's own rule picks the main one.
    /// </summary>
    /// <remarks>
    /// Putting a promotion back is asking for the cave as it was before it, and the cave was then
    /// anchored at the entrance the promotion demoted, not at whichever entrance is oldest. Giving
    /// the role moves the cave's point onto that entrance, so it is a position write, held to the
    /// exact-view rule every position write is.
    /// </remarks>
    private async Task<Guid?> DemotedAlongsideAsync(
        AccessContext ctx, AuditEntry entry, CaveEntrance entrance, CancellationToken ct)
    {
        if (entry.ChangeSetId is not { } changeSet)
        {
            return null;
        }

        var entranceType = FeatureAudit.TypeName(FeatureKind.CaveEntrance);
        var self = entrance.Id.ToString();
        var alongside = await db.AuditEntries.AsNoTracking()
            .Where(a => a.ChangeSetId == changeSet && a.EntityType == entranceType
                && a.Action == AuditActions.Updated && a.EntityId != self)
            .OrderBy(a => a.Id)
            .ToListAsync(ct);
        foreach (var row in alongside)
        {
            if (ChangesOf(row)[nameof(CaveEntrance.IsMain)] is not JsonObject pair
                || pair["old"] is not JsonValue old || !old.TryGetValue<bool>(out var wasMain) || !wasMain
                || !Guid.TryParse(row.EntityId, out var demoted))
            {
                continue;
            }

            var live = await db.CaveEntrances.AsNoTracking()
                .AnyAsync(e => e.Id == demoted && e.CaveFeatureId == entrance.CaveFeatureId
                    && db.Features.Any(f => f.Id == e.Id), ct);
            return live && await SeesExactlyAsync(ctx, [demoted, entrance.CaveFeatureId], ct) ? demoted : null;
        }

        return null;
    }

    /// <summary>A history row's recorded diff, or an empty one.</summary>
    internal static JsonObject ChangesOf(AuditEntry entry) =>
        string.IsNullOrEmpty(entry.Changes) ? new JsonObject() : JsonNode.Parse(entry.Changes) as JsonObject ?? new JsonObject();

    /// <summary>
    /// The caller's view of one change, by the timeline's own rule: whether they see the feature's
    /// exact position, what of the change they are shown, and the fields it hides from them —
    /// which are the fields they may not have back.
    /// </summary>
    internal async Task<(bool Exact, JsonObject? Shown, HashSet<string> Redacted)> ViewAsync(
        AccessContext ctx, Feature feature, string entityType, JsonObject changes, CancellationToken ct)
    {
        var hidden = await protection.RedactedLinkTargetIdsAsync(ctx, [feature.Id], ct);
        var exact = !hidden.Contains(feature.Id);
        var (shown, redacted) = HistoryProtection.Redact(
            entityType, changes.DeepClone().AsObject(), governingHidden: !exact, hidden.Contains,
            associationHidden: false, mayWriteSubject: false, peopleHidden: false, memberHidden: _ => false);
        return (exact, shown, redacted.ToHashSet(StringComparer.Ordinal));
    }

    /// <summary>
    /// Whether the caller sees every one of these features' positions exactly, by the same rule
    /// <see cref="ViewAsync"/> asks of one change — for a write that moves a position without
    /// being a field of a recorded change, such as giving a cave's main role back to an entrance.
    /// </summary>
    internal async Task<bool> SeesExactlyAsync(AccessContext ctx, IReadOnlyCollection<Guid> featureIds, CancellationToken ct)
    {
        var hidden = await protection.RedactedLinkTargetIdsAsync(ctx, featureIds, ct);
        return featureIds.All(id => !hidden.Contains(id));
    }

    /// <summary>
    /// Whether one field may be put back: null and its decoded old value added to
    /// <paramref name="values"/> when it may, else the reason it may not. The order of the checks
    /// is the order of the reasons' precedence — what is never restorable, then what the caller
    /// may not write, then what their history hides, then what was never kept.
    /// </summary>
    private async Task<string?> DecideAsync(
        Feature feature,
        AuditEntry entry,
        JsonObject changes,
        string field,
        bool exact,
        HashSet<string> redacted,
        Dictionary<string, object?> values,
        CancellationToken ct) =>
        Gate(feature, changes, field, exact, redacted)
        ?? await ReadOldAsync(feature, entry, changes, field, values, ct);

    /// <summary>
    /// The checks that need nothing but the field and the caller: null when the field may be put
    /// back by this caller, else the reason it may not — not restorable, a position the caller
    /// may not write, or a value their history hides. Asked before anything that reads the
    /// field's value, so that what is answered about a field the caller may not see says nothing
    /// about that value.
    /// </summary>
    internal string? Gate(Feature feature, JsonObject changes, string field, bool exact, HashSet<string> redacted)
    {
        if (changes[field] is not JsonObject pair
            || !FeatureHistoryFields.IsRestorable(feature.Kind, field)
            || writer.FieldType(feature, field) is null)
        {
            return HistoryRestoreReasons.NotRestorable;
        }

        // A category moved together with the type it follows is not put back apart from it.
        if (field == nameof(Feature.Category) && changes.ContainsKey(nameof(Feature.FeatureTypeId)))
        {
            return HistoryRestoreReasons.NotRestorable;
        }

        if (FeatureHistoryFields.IsPositionWrite(feature.Kind, field, pair["old"]) && !exact)
        {
            return HistoryRestoreReasons.Forbidden;
        }

        return redacted.Contains(field) ? HistoryRestoreReasons.Redacted : null;
    }

    /// <summary>
    /// The value a field had before one change, decoded into its column's type and added to
    /// <paramref name="values"/>: null when it was, else why it could not be — a large shape whose
    /// full copy was never kept, or a value that no longer fits the feature.
    /// </summary>
    internal async Task<string?> ReadOldAsync(
        Feature feature,
        AuditEntry entry,
        JsonObject changes,
        string field,
        Dictionary<string, object?> values,
        CancellationToken ct)
    {
        if (changes[field] is not JsonObject pair || writer.FieldType(feature, field) is not { } type)
        {
            return HistoryRestoreReasons.NotRestorable;
        }

        var old = pair["old"];
        object? value;
        if (FeatureHistoryFields.IsGeometrySummary(old))
        {
            var kept = await db.AuditGeometryValues.AsNoTracking()
                .Where(v => v.AuditEntryId == entry.Id && v.Property == field)
                .Select(v => v.OldWkb)
                .FirstOrDefaultAsync(ct);
            if (kept is null)
            {
                return HistoryRestoreReasons.NoPreImage;
            }

            var shape = AuditGeometry.ReadPreImage(kept);
            if (!AuditGeometry.Matches(shape, FeatureHistoryFields.SummaryFingerprint(old)))
            {
                return HistoryRestoreReasons.NoPreImage;
            }

            value = shape;
        }
        else if (!HistoryValues.TryDecode(old, type, out value))
        {
            return HistoryRestoreReasons.NotRestorable;
        }

        if (!await StillFitsAsync(feature, field, value, ct))
        {
            return HistoryRestoreReasons.NotRestorable;
        }

        values[field] = value;
        return null;
    }

    // An old value that names a row which has since gone, or a shape the feature's type no longer
    // takes, cannot be put back without breaking what the ordinary edit guarantees.
    private async Task<bool> StillFitsAsync(Feature feature, string field, object? value, CancellationToken ct)
    {
        switch (field)
        {
            case nameof(CaveEntrance.EntranceTypeId):
                return value is long entranceType && await db.EntranceTypes.AnyAsync(t => t.Id == entranceType, ct);
            case nameof(Cave.CaveTypeId):
                return value is long caveType && await db.CaveTypes.AnyAsync(t => t.Id == caveType, ct);
            case nameof(Cave.RockTypeId):
                return value is null || (value is long rockType && await db.RockTypes.AnyAsync(t => t.Id == rockType, ct));
            case nameof(Feature.Geom) when feature.Kind == FeatureKind.CaveEntrance:
                return value is Point;
            case nameof(Feature.Geom) when value is Geometry geometry:
                var accepted = await db.FeatureTypes.AsNoTracking()
                    .Where(t => t.Id == feature.FeatureTypeId)
                    .Select(t => t.AcceptedGeometryClasses)
                    .FirstOrDefaultAsync(ct);
                return GeometryClasses.Of(geometry) is { } geometryClass && accepted?.Contains(geometryClass) == true;
            default:
                return true;
        }
    }
}
