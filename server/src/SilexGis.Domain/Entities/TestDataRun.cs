// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>Where a test-data run stands. Stored as a number; append, never renumber.</summary>
public enum TestDataRunStatus : short
{
    /// <summary>Asked for; its job waits in the queue.</summary>
    Queued = 0,

    /// <summary>Generating.</summary>
    Running = 1,

    /// <summary>Every phase finished. Its rows are in the database and listed in the registry.</summary>
    Succeeded = 2,

    /// <summary>A phase failed. What it wrote before failing is still listed, so it can be removed.</summary>
    Failed = 3,

    /// <summary>Stopped on request between two items. What it wrote is listed, as for a failure.</summary>
    Cancelled = 4,

    /// <summary>Its removal is queued or running.</summary>
    Removing = 5,

    /// <summary>Everything it wrote is gone; the run row stays as the record that it happened.</summary>
    Removed = 6,
}

/// <summary>
/// One generation of invented test data: the profile it ran from, how far it got and what it
/// wrote. Every row the run creates is listed against it in <see cref="TestDataRow"/>, which is
/// what removal walks — never a name, a code or the visible tag, which anybody can type.
/// </summary>
public class TestDataRun
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public TestDataRunStatus Status { get; set; } = TestDataRunStatus.Queued;

    /// <summary>The profile document as submitted (jsonb), so a run can be repeated exactly.</summary>
    public required string Profile { get; set; }

    public long Seed { get; set; }

    /// <summary>The phase being worked on, by its code; null before the first and after the last.</summary>
    public string? Phase { get; set; }

    public int ProgressDone { get; set; }

    public int ProgressTotal { get; set; }

    /// <summary>Rows written so far, by kind (jsonb object of counts). Counts only, never contents.</summary>
    public string Counts { get; set; } = "{}";

    /// <summary>What the run could not do and why (jsonb array of short sentences).</summary>
    public string Notes { get; set; } = "[]";

    public string? Error { get; set; }

    /// <summary>Whether the administrator confirmed generating beside data no run created.</summary>
    public bool AlongsideExistingData { get; set; }

    public Guid? RequestedBy { get; set; }

    public long? ProcessingJobId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>Set by a cancel request; the running job looks at it between items.</summary>
    public DateTimeOffset? CancelRequestedAt { get; set; }

    public DateTimeOffset? RemovedAt { get; set; }
}

/// <summary>
/// One row a test-data run created, named by its table and its key as text (most keys are
/// uuids, the vocabularies' are numbers). Rows that share their key with a row listed here —
/// a cave's satellite row, an entrance's — are covered by that listing and are not listed again.
/// </summary>
public class TestDataRow
{
    public Guid RunId { get; set; }

    public required string TableName { get; set; }

    public required string RowId { get; set; }

    /// <summary>The order the run wrote it in, so removal can walk back the other way.</summary>
    public long Sequence { get; set; }
}
