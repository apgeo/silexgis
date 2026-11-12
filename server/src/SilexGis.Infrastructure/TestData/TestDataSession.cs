// SPDX-License-Identifier: AGPL-3.0-or-later
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

    public void Count(string kind, int by = 1) =>
        Counts[kind] = Counts.GetValueOrDefault(kind) + by;

    public void Note(string note)
    {
        if (!Notes.Contains(note))
        {
            Notes.Add(note);
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
