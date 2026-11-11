// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Infrastructure.Jobs;

/// <summary>
/// How the background job workers poll the queue and how long a job they took stays theirs.
/// </summary>
/// <remarks>
/// A worker that takes a job holds a lease on it and renews it while the job runs. A job whose
/// lease runs out was lost by whatever was running it — the process died under it, or the outcome
/// could not be saved — and a periodic sweep returns it to the queue, or fails it once it has used
/// up its attempts. The lease is what lets a second process, or a process restarting while the
/// old one is still draining, tell a job somebody is running from a job nobody is.
/// </remarks>
public sealed class JobQueueOptions
{
    public const string SectionName = "Jobs";

    /// <summary>
    /// Seconds between polls for queued work. Zero or less switches the workers off entirely,
    /// which a test host that runs its own jobs relies on.
    /// </summary>
    public int PollSeconds { get; set; } = 2;

    /// <summary>
    /// How long a job stays its worker's without being renewed, in seconds.
    /// </summary>
    /// <remarks>
    /// The time a lost job waits before it is taken again — after a restart, a job the old process
    /// was running is picked up within this plus <see cref="SweepSeconds"/>. Too short, and a
    /// renewal delayed by a busy machine or a briefly unreachable database hands a live job to
    /// a second runner; two minutes is many missed renewals away from that.
    /// </remarks>
    public int LeaseSeconds { get; set; } = 120;

    /// <summary>
    /// How often a running job's lease is renewed, in seconds. A value of zero or less, or one
    /// more than half the lease, is replaced by a third of the lease: a renewal must be able to
    /// miss at least once without the job being taken away.
    /// </summary>
    public int HeartbeatSeconds { get; set; } = 30;

    /// <summary>
    /// How often a worker looks for jobs whose lease has run out, in seconds, besides once at
    /// start. Zero or less means at start only.
    /// </summary>
    public int SweepSeconds { get; set; } = 60;

    /// <summary>The lease length in force, never under two seconds.</summary>
    public TimeSpan Lease => TimeSpan.FromSeconds(Math.Max(2, LeaseSeconds));

    /// <summary>The renewal interval in force — see <see cref="HeartbeatSeconds"/>.</summary>
    public TimeSpan Heartbeat =>
        HeartbeatSeconds <= 0 || HeartbeatSeconds * 2 > Lease.TotalSeconds
            ? Lease / 3
            : TimeSpan.FromSeconds(HeartbeatSeconds);
}
