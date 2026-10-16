// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Filters;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Filters;

/// <summary>One saved filter as one reader sees it: its document, if they can run it, and its state.</summary>
/// <param name="Document">
/// Null when the stored document could not be parsed at all. A parsed document is handed back even
/// when it is unreadable for this reader — they may read the saved filter, so its literals are
/// already theirs — but it is never run for them.
/// </param>
public sealed record SavedFilterReading(
    SavedFilter Row,
    FilterDocument? Document,
    SavedFilterState State);

/// <summary>
/// The saved filters a caller may read, and what each one reads as to them.
/// </summary>
/// <remarks>
/// <para>
/// Reading a saved filter is governed like reading a saved map view, by the same rules over the
/// same domain. Being able to read one says nothing about the rows it would match: those are
/// decided when it is run, by whoever runs it.
/// </para>
/// <para>
/// Whether what a document names still exists is asked only of the installation's own
/// vocabularies — cave, rock, feature, document and trip types, and tags — which every account may
/// read in full. Asking whether a person or a caving group named in a filter still exists would
/// answer a question about accounts the reader may not otherwise see, so those are not checked.
/// </para>
/// </remarks>
public sealed class SavedFilterReader(SilexGisDbContext db, FilterWorldRegistry registry)
{
    /// <summary>The saved filters this caller may read, before any ordering.</summary>
    public IQueryable<SavedFilter> Visible(AccessContext caller) =>
        db.SavedFilters.AsNoTracking().VisibleTo(caller, AccessDomain.MapViews);

    /// <summary>One saved filter as this reader sees it, or null when they may not read it.</summary>
    public async Task<SavedFilterReading?> ReadAsync(AccessContext caller, Guid id, CancellationToken ct)
    {
        var row = await Visible(caller).FirstOrDefaultAsync(x => x.Id == id, ct);
        return row is null ? null : (await DescribeAsync(caller, [row], ct))[0];
    }

    /// <summary>These rows as this reader sees them, in the order given.</summary>
    public async Task<IReadOnlyList<SavedFilterReading>> DescribeAsync(
        AccessContext caller, IReadOnlyList<SavedFilter> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var vocabularies = await VocabulariesAsync(caller, ct);
        var parsed = rows.Select(r => (Row: r, Document: FilterDocument.Deserialize(r.Document))).ToList();

        var references = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (_, document) in parsed)
        {
            if (!SavedFilterRules.IsReadableBy(document, vocabularies))
            {
                continue;
            }

            foreach (var (options, values) in SavedFilterRules.References(document!, vocabularies))
            {
                if (!references.TryGetValue(options, out var all))
                {
                    references[options] = all = new HashSet<string>(StringComparer.Ordinal);
                }

                all.UnionWith(values);
            }
        }

        var missing = await MissingAsync(references, ct);

        return
        [
            .. parsed.Select(p =>
            {
                if (!SavedFilterRules.IsReadableBy(p.Document, vocabularies))
                {
                    return new SavedFilterReading(p.Row, p.Document, SavedFilterState.Unreadable);
                }

                var stale = SavedFilterRules.References(p.Document!, vocabularies)
                    .Any(r => missing.TryGetValue(r.Key, out var gone) && r.Value.Overlaps(gone));
                return new SavedFilterReading(
                    p.Row, p.Document, stale ? SavedFilterState.Stale : SavedFilterState.Ready);
            }),
        ];
    }

    /// <summary>This caller's vocabularies by world key, which is what every check here reads.</summary>
    public async Task<IReadOnlyDictionary<string, WorldVocabulary>> VocabulariesAsync(
        AccessContext caller, CancellationToken ct) =>
        (await registry.VocabulariesAsync(caller, ct)).ToDictionary(v => v.World, StringComparer.Ordinal);

    /// <summary>Which of the named values no longer exist, per option source that can be asked.</summary>
    private async Task<Dictionary<string, HashSet<string>>> MissingAsync(
        Dictionary<string, HashSet<string>> references, CancellationToken ct)
    {
        var missing = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (options, values) in references)
        {
            if (values.Count == 0)
            {
                continue;
            }

            var present = options switch
            {
                "caveTypes" => await PresentIdsAsync(db.CaveTypes.Select(t => t.Id), values, ct),
                "rockTypes" => await PresentIdsAsync(db.RockTypes.Select(t => t.Id), values, ct),
                "featureTypes" => await PresentIdsAsync(db.FeatureTypes.Select(t => t.Id), values, ct),
                "documentTypes" => await PresentIdsAsync(db.DocumentTypes.Select(t => t.Id), values, ct),
                "tripTypes" => await PresentIdsAsync(db.TripTypes.Select(t => t.Id), values, ct),
                "tags" => await PresentSlugsAsync(values, ct),
                // Not asked, on purpose — see the remarks on the class.
                _ => null,
            };

            if (present is not null)
            {
                missing[options] = [.. values.Where(v => !present.Contains(v))];
            }
        }

        return missing;
    }

    private static async Task<HashSet<string>> PresentIdsAsync(
        IQueryable<long> ids, HashSet<string> values, CancellationToken ct)
    {
        var wanted = values.Select(v => long.TryParse(v, out var id) ? id : (long?)null)
            .Where(id => id is not null).Select(id => id!.Value).ToArray();
        if (wanted.Length == 0)
        {
            return [];
        }

        var found = await ids.Where(id => wanted.Contains(id)).ToListAsync(ct);
        return [.. found.Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture))];
    }

    private async Task<HashSet<string>> PresentSlugsAsync(HashSet<string> values, CancellationToken ct)
    {
        var wanted = values.ToArray();
        return [.. await db.Tags.AsNoTracking().Where(t => wanted.Contains(t.Slug)).Select(t => t.Slug).ToListAsync(ct)];
    }
}
