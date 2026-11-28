// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Domain.TestData;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.TestData;

/// <summary>
/// What one run carries from phase to phase: who it writes for, the ids it has made that later
/// phases hang things on, and the tally it reports. Every row it writes is listed through
/// <see cref="Register"/>, in the same save as the row itself, so the registry can never miss a
/// row that exists or name one that was never written.
/// </summary>
public sealed class TestDataSession(Guid runId, Guid ownerUserId, TestDataWorldPlan plan, long firstSequence = 0)
{
    private long sequence = firstSequence;

    public Guid RunId { get; } = runId;

    public Guid OwnerUserId { get; } = ownerUserId;

    public TestDataWorldPlan Plan { get; } = plan;

    public TestDataProfile Profile => Plan.Profile;

    public SortedDictionary<string, int> Counts { get; } = new(StringComparer.Ordinal);

    public List<string> Notes { get; } = [];

    /// <summary>The massif feature of each region, by region code.</summary>
    public Dictionary<string, Guid> Massifs { get; } = new(StringComparer.Ordinal);

    /// <summary>The sub-area features of each region, by region code, in the region's order.</summary>
    public Dictionary<string, List<Guid>> SubAreas { get; } = new(StringComparer.Ordinal);

    /// <summary>The generated caves, by their plan index.</summary>
    public Dictionary<int, Guid> Caves { get; } = [];

    /// <summary>The generated clubs and organisations, by their plan index.</summary>
    public Dictionary<int, Guid> Clubs { get; } = [];

    /// <summary>The generated roster entries, by their plan index.</summary>
    public Dictionary<int, Guid> Cavers { get; } = [];

    /// <summary>The generated accounts, by the plan index of the caver who holds them.</summary>
    public Dictionary<int, Guid> Users { get; } = [];

    /// <summary>The password every generated account shares, hashed; null when the accounts cannot be signed in to.</summary>
    public string? AccountPasswordHash { get; init; }

    /// <summary>Whether the database held data no run created when the run started; some things are then not generated.</summary>
    public bool AlongsideExistingData { get; init; }

    /// <summary>The vocabulary the run adds beside the shipped one; null until the vocabulary step has run.</summary>
    public TestDataExtraVocabulary? Extra { get; set; }

    public long? MarkingTagId { get; set; }

    public TestDataVocabularyIds Vocabulary { get; set; } = null!;

    public void Register(SilexGisDbContext db, string table, object rowId)
    {
        ArgumentNullException.ThrowIfNull(db);
        db.TestDataRows.Add(new TestDataRow
        {
            RunId = RunId,
            TableName = table,
            RowId = Convert.ToString(rowId, System.Globalization.CultureInfo.InvariantCulture)!,
            Sequence = ++sequence,
        });
    }

    /// <summary>
    /// The tables whose new rows <see cref="SaveAndRegisterAsync"/> lists by itself. Features are
    /// listed as they are written (with their marking) and accounts as the user manager saves them,
    /// so neither is here; rows that go with their parent — an account's roles, a feature's
    /// tagging — are not listed at all.
    /// </summary>
    public static IReadOnlySet<string> SelfListedTables { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "caving_groups", "cavers", "caving_group_memberships", "permission_groups", "permission_group_members",
        "access_entries", "user_addresses", "user_notification_preferences", "calendar_feed_tokens", "api_access_keys",
        "feature_links", "feature_external_ids", "feature_shares", "cave_qr_publications", "link_kinds",
        "cave_surveys", "survey_models", "survey_sources", "cave_depth_places", "cave_level_bands",
        "documents", "document_versions", "files", "cave_types", "entrance_types", "rock_types", "feature_types",
    };

    /// <summary>
    /// Saves what is staged on <paramref name="db"/> and lists every new row of a
    /// <see cref="SelfListedTables"/> table against the run, counting them by table. Rows staged
    /// by a shared helper — the permission groups a new club is given, say — are listed exactly as
    /// rows the generator staged itself, because the helper's rows are found in the change
    /// tracker rather than by knowing what the helper does.
    /// </summary>
    public async Task SaveAndRegisterAsync(SilexGisDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var added = db.ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Added)
            .Select(e => (Table: e.Metadata.GetTableName(), Entry: e))
            .Where(x => x.Table is not null && SelfListedTables.Contains(x.Table))
            .ToList();
        await db.SaveChangesAsync(ct);
        foreach (var (table, entry) in added)
        {
            Register(db, table!, entry.Property("Id").CurrentValue!);
            Count(CountKind(table!));
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>The tally's name for rows of a table: the table's name in camel case (<c>caving_groups</c> → <c>cavingGroups</c>).</summary>
    public static string CountKind(string table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var parts = table.Split('_');
        return parts[0] + string.Concat(parts.Skip(1).Select(p => p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..]));
    }

    public void Count(string kind, int by = 1) =>
        Counts[kind] = Counts.GetValueOrDefault(kind) + by;

    public void Note(string note)
    {
        if (!Notes.Contains(note))
        {
            Notes.Add(note);
        }
    }

    /// <summary>Adds to the number a <c>code:count</c> note carries, keeping one note per code.</summary>
    public void NoteCount(string code, int by = 1)
    {
        var at = Notes.FindIndex(n => n.StartsWith(code + ":", StringComparison.Ordinal));
        var before = at < 0 ? 0 : int.Parse(Notes[at][(code.Length + 1)..], System.Globalization.CultureInfo.InvariantCulture);
        var note = TestDataNotes.WithCount(code, before + by);
        if (at < 0)
        {
            Notes.Add(note);
        }
        else
        {
            Notes[at] = note;
        }
    }
}

/// <summary>The shipped vocabulary rows a run writes against, resolved once by code.</summary>
public sealed record TestDataVocabularyIds(
    IReadOnlyDictionary<string, long> CaveTypes,
    IReadOnlyDictionary<string, long> EntranceTypes,
    IReadOnlyDictionary<string, long> RockTypes,
    long MassifTypeId,
    long KarstAreaTypeId);
