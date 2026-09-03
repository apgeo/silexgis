// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Domain.Import.TripCsv;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Trips;

namespace SilexGis.Infrastructure.Import;

/// <summary>
/// Turns the neutral rows a trip spreadsheet was read into — names, dates and lists of words —
/// into what each row would mean here: which type, which people, which caves, which places.
///
/// <para>
/// It proposes and never writes. Nothing in this class creates a row, and the create toggles it
/// reads only decide what it says <em>would</em> be created if the import were confirmed;
/// matching against what the installation already holds happens whether they are on or off. That
/// separation is what makes a preview safe to run as often as the reviewer likes, and it is what
/// lets an option be changed and the sheet read again without anything having to be undone.
/// </para>
/// <para>
/// Every proposal is made from what this caller may be told about, and a caller who may not be
/// told about something is not told that there was something. Proposing a cave is a read of that
/// cave, and a trip carries its own geometry, so a cave reaches a proposal only after both of the
/// gates a trip's cave list already answers to: readable first, then placeable. The cost is
/// stated rather than hidden — a caller who may not place a cave will be offered the chance to
/// create a second one under the same name, and that is the honest answer, because the
/// alternative tells them where a guarded cave is by refusing to.
/// </para>
/// </summary>
public sealed class TripImportResolver(SilexGisDbContext db, FeatureProtection protection)
{
    /// <summary>
    /// Reads what every row would mean for one caller under one set of choices.
    ///
    /// <para>
    /// Rows carrying an error are resolved like any other. The preview does not offer them and
    /// the confirmation does not create them, but a reviewer looking at why a row failed is
    /// better served by seeing what the rest of it was taken for than by an empty line.
    /// </para>
    /// </summary>
    public async Task<TripImportResolutionSet> ResolveAsync(
        IReadOnlyList<TripCsvRow> rows,
        TripImportOptions options,
        AccessContext ctx,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(ctx);

        var types = await TripTypeIndexAsync(ct);
        var roles = await RoleIndexAsync(ct);
        var people = await CaverIndexAsync(ct);
        var caves = await CaveIndexAsync(rows, ctx, ct);
        var areas = await AreaIndexAsync(rows, ctx, ct);

        var resolutions = new Dictionary<int, TripImportRowResolution>();
        var newTypes = new OrderedNames();
        var newCavers = new OrderedNames();
        var newCaves = new OrderedNames();
        var newAreas = new OrderedNames();

        // The distinct-value tables the reviewer actually reads. Collected as the rows are
        // resolved rather than in a second pass, so a value can never appear in the summary
        // saying one thing and on its row saying another.
        var typeTable = new OrderedMatches<TripImportTermMatch>();
        var personTable = new OrderedMatches<TripImportPersonMatch>();
        var caveTable = new OrderedMatches<TripImportFeatureMatch>();
        var areaTable = new OrderedMatches<TripImportFeatureMatch>();

        foreach (var row in rows)
        {
            var type = MatchTerm(row.TripType, types, options);
            if (type is not null)
            {
                typeTable.Add(type.Source, type);
                if (type.WillCreate)
                {
                    newTypes.Add(type.Source);
                }
            }

            var rowCaves = row.Caves
                .Select(name => MatchFeature(name, caves, options, options.CreateMissingCaves))
                .Where(m => m is not null)
                .Select(m => m!)
                .ToList();
            foreach (var cave in rowCaves)
            {
                caveTable.Add(cave.Source, cave);
                if (cave.WillCreate)
                {
                    newCaves.Add(cave.Source);
                }
            }

            var massif = MatchFeature(row.Massif, areas, options, options.CreateMissingAreas);
            var subArea = MatchFeature(row.SubArea, areas, options, options.CreateMissingAreas);
            foreach (var area in new[] { massif, subArea }.Where(a => a is not null).Select(a => a!))
            {
                areaTable.Add(area.Source, area);
                if (area.WillCreate)
                {
                    newAreas.Add(area.Source);
                }
            }

            var proposers = row.Proposers.Select(n => MatchPerson(n, people, options))
                .Where(m => m is not null).Select(m => m!).ToList();
            var participants = row.Participants.Select(n => MatchPerson(n, people, options))
                .Where(m => m is not null).Select(m => m!).ToList();
            foreach (var person in proposers.Concat(participants))
            {
                personTable.Add(person.Source, person);
                if (person.WillCreate)
                {
                    newCavers.Add(person.Source);
                }
            }

            resolutions[row.Line] = new TripImportRowResolution(
                row.Line,
                type,
                rowCaves,
                massif,
                subArea,
                proposers,
                participants,
                LocationNote(row, rowCaves, massif, subArea));
        }

        return new TripImportResolutionSet(
            resolutions,
            roles.GetValueOrDefault(TripParticipantRoleSeeds.ParticipantCode),
            roles.GetValueOrDefault(TripParticipantRoleSeeds.ProposerCode),
            newTypes.Values,
            newCavers.Values,
            newCaves.Values,
            newAreas.Values,
            typeTable.Values,
            personTable.Values,
            caveTable.Values,
            areaTable.Values);
    }

    // ---------- what the row said that did not become a link ----------

    /// <summary>
    /// The words a trip keeps about where it went. Everything the row named and nothing here
    /// answered to goes in, together with anything a switched-off toggle declined to create, so
    /// that turning a toggle off loses no information from the sheet — only the row it would
    /// have made. The country goes in unconditionally: the trip model has no country of its own,
    /// and a sheet that bothered to write one is saying something a reader of the trip wants.
    /// </summary>
    private static string? LocationNote(
        TripCsvRow row,
        IReadOnlyList<TripImportFeatureMatch> caves,
        TripImportFeatureMatch? massif,
        TripImportFeatureMatch? subArea)
    {
        var parts = new List<string>();
        Add(row.Country);
        Add(Untaken(massif));
        Add(Untaken(subArea));
        foreach (var cave in caves)
        {
            Add(Untaken(cave));
        }

        return parts.Count == 0 ? null : string.Join(", ", parts);

        void Add(string? value)
        {
            var trimmed = value?.Trim();
            if (!string.IsNullOrEmpty(trimmed) && !parts.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                parts.Add(trimmed);
            }
        }

        // A match that will become a link needs no words; everything else does, including the
        // one left ambiguous, which is precisely the case where a reader has to be able to see
        // what the sheet actually wrote.
        static string? Untaken(TripImportFeatureMatch? match) =>
            match is null || match.State == TripImportMatchState.Matched || match.WillCreate
                ? null
                : match.Source;
    }

    // ---------- matching ----------

    private static TripImportTermMatch? MatchTerm(
        string? source, NameIndex<long> index, TripImportOptions options)
    {
        var text = source?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        // A term the reviewer pointed somewhere by hand wins over what the name matched, and it
        // is not held to answering to that name: pointing "topo" at the surveying type is the
        // whole point of the control, and a vocabulary carries no protection to walk around.
        if (options.ChosenTripTypeId(text) is { } chosen && index.Holds(chosen, out var chosenName))
        {
            return new TripImportTermMatch(text, TripImportMatchState.Matched, chosen, chosenName, false);
        }

        var hits = index.Lookup(text);
        return hits.Count switch
        {
            1 => new TripImportTermMatch(text, TripImportMatchState.Matched, hits[0].Key, hits[0].Name, false),
            0 => new TripImportTermMatch(
                text, TripImportMatchState.Unmatched, null, null, options.CreateMissingTripTypes),
            _ => new TripImportTermMatch(text, TripImportMatchState.Ambiguous, null, null, false),
        };
    }

    private static TripImportFeatureMatch? MatchFeature(
        string? source, NameIndex<Guid> index, TripImportOptions options, bool createMissing)
    {
        var text = source?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var hits = index.Lookup(text);

        // A choice settles which of the candidates the name meant. Honoured only among them, so
        // that naming an identifier in the request body can never reach a feature this caller was
        // not already offered under this name — both gates decided that set, and a choice is a
        // person picking from it rather than a second way in.
        if (options.ChosenFeatureId(text) is { } chosen && hits.Any(h => h.Key == chosen))
        {
            return new TripImportFeatureMatch(
                text,
                TripImportMatchState.Matched,
                chosen,
                hits.First(h => h.Key == chosen).Name,
                hits.Count,
                false);
        }

        return hits.Count switch
        {
            1 => new TripImportFeatureMatch(text, TripImportMatchState.Matched, hits[0].Key, hits[0].Name, 1, false),
            0 => new TripImportFeatureMatch(text, TripImportMatchState.Unmatched, null, null, 0, createMissing),
            _ => new TripImportFeatureMatch(text, TripImportMatchState.Ambiguous, null, null, hits.Count, false),
        };
    }

    private static TripImportPersonMatch? MatchPerson(
        string? source, NameIndex<Guid> index, TripImportOptions options)
    {
        var text = source?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var hits = index.Lookup(text);
        var createMissing = options.CreateMissingCavers;

        // The same rule as for a place: a choice picks one of the people this name answered to,
        // and a choice naming anybody else is no choice at all.
        if (options.ChosenCaverId(text) is { } chosen && hits.Any(h => h.Key == chosen))
        {
            return new TripImportPersonMatch(
                text,
                TripImportMatchState.Matched,
                chosen,
                hits.First(h => h.Key == chosen).Name,
                hits.Count,
                false);
        }

        if (hits.Count == 1)
        {
            return new TripImportPersonMatch(text, TripImportMatchState.Matched, hits[0].Key, hits[0].Name, 1, false);
        }

        if (hits.Count > 1)
        {
            // Two people share a name — the state the roster's merge exists to resolve — so the
            // row waits for somebody to say which. The rule that resolves a typed name to the
            // oldest of them is right at a keyboard, where the person typing knows who they
            // mean, and wrong here, where nobody is watching and the wrong answer becomes a
            // claim about who was underground on a day in 2014.
            return new TripImportPersonMatch(
                text, TripImportMatchState.Ambiguous, null, null, hits.Count, false);
        }

        return new TripImportPersonMatch(
            text,
            TripImportMatchState.Unmatched,
            null,
            null,
            0,
            createMissing && TripImportNames.MayCreatePerson(text));
    }

    // ---------- what the installation holds ----------

    private async Task<NameIndex<long>> TripTypeIndexAsync(CancellationToken ct)
    {
        var rows = await db.TripTypes.AsNoTracking()
            .Select(t => new { t.Id, t.Code, t.Name })
            .ToListAsync(ct);

        var index = new NameIndex<long>();
        foreach (var row in rows)
        {
            // A code is a name too, and the sheet's own column is as likely to carry one as a
            // label. Both are folded into the same index so that the code is looked up by what
            // it says rather than by the number it happens to have here — numbers are local to
            // an installation and mean nothing in somebody else's spreadsheet.
            index.Add(row.Name, row.Id, row.Name);
            index.Add(row.Code, row.Id, row.Name);
        }

        return index;
    }

    private async Task<Dictionary<string, TripImportTermMatch>> RoleIndexAsync(CancellationToken ct)
    {
        var wanted = new[] { TripParticipantRoleSeeds.ParticipantCode, TripParticipantRoleSeeds.ProposerCode };
        var rows = await db.TripParticipantRoles.AsNoTracking()
            .Where(r => wanted.Contains(r.Code))
            .Select(r => new { r.Id, r.Code, r.Name })
            .ToListAsync(ct);

        // By code, never by number: the shipped roles carry the same codes everywhere and
        // different keys everywhere, and a role resolved by key would silently record the
        // people on one installation's trips under another installation's job.
        return rows.ToDictionary(
            r => r.Code,
            r => new TripImportTermMatch(r.Code, TripImportMatchState.Matched, r.Id, r.Name, false),
            StringComparer.Ordinal);
    }

    private async Task<NameIndex<Guid>> CaverIndexAsync(CancellationToken ct)
    {
        // The whole roster, folded here rather than compared in the database. Folding is one
        // rule and it has one implementation; expressing it a second time in SQL would mean two
        // rules that have to agree character for character forever, and the day they stop
        // agreeing the importer matches nothing while reporting no error at all. A roster is a
        // club's people, which is a list this comfortably holds.
        var rows = await db.Cavers.AsNoTracking()
            .Select(c => new { c.Id, c.FullName })
            .ToListAsync(ct);

        var index = new NameIndex<Guid>();
        foreach (var row in rows)
        {
            index.Add(row.FullName, row.Id, row.FullName);
        }

        return index;
    }

    /// <summary>
    /// The caves this caller may be told about, by name. Both gates in the order the trip's own
    /// cave list applies them, and through the one place that rule lives rather than a second
    /// copy of it: what a preview offers and what a confirmation writes have to be the same set,
    /// and two predicates are two answers waiting to drift apart.
    /// </summary>
    private async Task<NameIndex<Guid>> CaveIndexAsync(
        IReadOnlyList<TripCsvRow> rows, AccessContext ctx, CancellationToken ct)
    {
        var wanted = rows.SelectMany(r => r.Caves).Select(TripImportNames.Key)
            .Where(k => k.Length > 0).ToHashSet(StringComparer.Ordinal);
        if (wanted.Count == 0)
        {
            return new NameIndex<Guid>();
        }

        var candidates = await db.Features.AsNoTracking()
            .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .Where(f => f.Kind == FeatureKind.Cave && f.Name != null)
            .Select(f => new { f.Id, f.Name })
            .ToListAsync(ct);

        var named = candidates.Where(c => wanted.Contains(TripImportNames.Key(c.Name))).ToList();
        var disclosable = await TripCaveDisclosure.DisclosableCaveIdsAsync(
            db, protection, ctx, [.. named.Select(c => c.Id)], ct);

        var index = new NameIndex<Guid>();
        foreach (var row in named.Where(c => disclosable.Contains(c.Id)))
        {
            index.Add(row.Name, row.Id, row.Name);
        }

        return index;
    }

    /// <summary>
    /// The area features this caller may be told about, by name. The placement gate applies here
    /// too: an area is a smaller disclosure than a cave, but a trip linked to a guarded one is
    /// still a statement about where that trip was, and the rule does not get to be weaker
    /// because the object is bigger.
    /// </summary>
    /// <remarks>
    /// Narrowed to the kinds this importer would itself write rather than to everything filed
    /// under the area category, because the category also holds a club's work areas — a
    /// declaration that the club works somewhere. A spreadsheet's massif column is not that
    /// declaration, and a sheet naming a massif a club happens to have declared would otherwise
    /// file every cave it invents inside the declaration and put the trip's name on it.
    /// </remarks>
    private async Task<NameIndex<Guid>> AreaIndexAsync(
        IReadOnlyList<TripCsvRow> rows, AccessContext ctx, CancellationToken ct)
    {
        var wanted = rows.SelectMany(r => new[] { r.Massif, r.SubArea })
            .Select(TripImportNames.Key).Where(k => k.Length > 0).ToHashSet(StringComparer.Ordinal);
        if (wanted.Count == 0)
        {
            return new NameIndex<Guid>();
        }

        var codes = TripImportKinds.Areas;
        var kinds = db.FeatureTypes.AsNoTracking()
            .Where(t => codes.Contains(t.Code))
            .Select(t => t.Id);

        var candidates = await db.Features.AsNoTracking()
            .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .Where(f => f.Kind == FeatureKind.Generic
                && f.Category == FeatureCategory.Area
                && f.FeatureTypeId != null
                && kinds.Contains(f.FeatureTypeId.Value)
                && f.Name != null)
            .Select(f => new { f.Id, f.Name })
            .ToListAsync(ct);

        var named = candidates.Where(c => wanted.Contains(TripImportNames.Key(c.Name))).ToList();
        var redacted = await protection.RedactedLinkTargetIdsAsync(ctx, [.. named.Select(c => c.Id)], ct);

        var index = new NameIndex<Guid>();
        foreach (var row in named.Where(c => !redacted.Contains(c.Id)))
        {
            index.Add(row.Name, row.Id, row.Name);
        }

        return index;
    }

    /// <summary>Names folded once, with everything that answers to each of them.</summary>
    private sealed class NameIndex<TKey>
    {
        private readonly Dictionary<string, List<(TKey Key, string Name)>> byName = new(StringComparer.Ordinal);

        public void Add(string? name, TKey key, string? display)
        {
            var folded = TripImportNames.Key(name);
            if (folded.Length == 0)
            {
                return;
            }

            if (!byName.TryGetValue(folded, out var hits))
            {
                hits = [];
                byName[folded] = hits;
            }

            // The same thing indexed under both its code and its name must not look like two
            // things when the two happen to fold alike.
            if (!hits.Any(h => EqualityComparer<TKey>.Default.Equals(h.Key, key)))
            {
                hits.Add((key, display ?? name ?? string.Empty));
            }
        }

        public IReadOnlyList<(TKey Key, string Name)> Lookup(string? name) =>
            byName.GetValueOrDefault(TripImportNames.Key(name)) ?? [];

        /// <summary>
        /// Whether this index holds the thing at all, under any name. Asked where a reviewer has
        /// pointed a value at something by hand and the answer has to be that the thing exists,
        /// not that it answers to the word the sheet wrote.
        /// </summary>
        public bool Holds(TKey key, out string? name)
        {
            foreach (var hits in byName.Values)
            {
                foreach (var hit in hits)
                {
                    if (EqualityComparer<TKey>.Default.Equals(hit.Key, key))
                    {
                        name = hit.Name;
                        return true;
                    }
                }
            }

            name = null;
            return false;
        }
    }

    /// <summary>
    /// One entry per distinct value the sheet wrote, in the order it first wrote them, folded for
    /// distinctness the same way everything else here is. The first reading of a value is kept:
    /// the same word resolves the same way on every row, so a later one would say the same thing.
    /// </summary>
    private sealed class OrderedMatches<TMatch>
    {
        private readonly HashSet<string> seen = new(StringComparer.Ordinal);
        private readonly List<TMatch> values = [];

        public IReadOnlyList<TMatch> Values => values;

        public void Add(string source, TMatch match)
        {
            if (seen.Add(TripImportNames.Key(source)))
            {
                values.Add(match);
            }
        }
    }

    /// <summary>Distinct names in the order the sheet first wrote them, folded for distinctness.</summary>
    private sealed class OrderedNames
    {
        private readonly HashSet<string> seen = new(StringComparer.Ordinal);
        private readonly List<string> values = [];

        public IReadOnlyList<string> Values => values;

        public void Add(string name)
        {
            if (seen.Add(TripImportNames.Key(name)))
            {
                values.Add(name.Trim());
            }
        }
    }
}
