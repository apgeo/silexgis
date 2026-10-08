// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Persistence.Configurations;

namespace SilexGis.Infrastructure.Trips;

/// <summary>
/// The one writer of a trip's party numbers: gives each newly named person the next number, and
/// decides what becomes of a number when two entries for one person are folded together.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every path that puts somebody on a trip's roster calls <see cref="AssignAsync"/></b> — a
/// trip being saved, answers being turned into the list of people who went, the demonstration
/// data — so that nobody is ever numbered by two rules. A path that forgets is not a missing
/// person: the reads list such a person after the numbered ones, as the whole party was listed
/// before numbers were stored, and the next save of the trip's roster gives them a number.
/// </para>
/// <para>
/// Like everything beside it that prepares a trip write, nothing here saves: the numbers belong to
/// the same unit of work as the roster rows they answer for, and the caller decides where that
/// unit ends. Two writers naming new people on one trip at the same instant would both reach for
/// the same next number, and the second to save is refused by the table's key rather than being
/// allowed to share it. Nothing is held between the read and the save to prevent that; the caller
/// that saves asks <see cref="LostTheRace"/> of a failed save and answers the loser with
/// <see cref="LostRaceCode"/>, whose remedy is to read the trip again and repeat the write.
/// </para>
/// </remarks>
public static class TripPartyNumbers
{
    /// <summary>
    /// The stable code a write is refused with when another write numbered the same trip's party
    /// first.
    /// </summary>
    public const string LostRaceCode = "trip_log.concurrent_roster_write";

    /// <summary>What the loser of that race is told. It names nobody and no number.</summary>
    public const string LostRaceMessage =
        "Somebody else changed who is on this trip at the same moment. Reload the trip and save again.";

    /// <summary>
    /// Whether a save failed because another write gave out this trip's next party number, or
    /// numbered the same person, first.
    /// </summary>
    /// <remarks>
    /// Told apart by the names of the two rules of the numbers' table and by nothing looser, so
    /// that no other failure of a save is taken for a race and answered as something a retry
    /// cures. The whole unit of work failed with it — the roster rows included — so the loser has
    /// written nothing and may simply repeat the write against what the winner left.
    /// </remarks>
    public static bool LostTheRace(DbUpdateException e) =>
        e.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: TripPartyNumberConfiguration.KeyName or TripPartyNumberConfiguration.OnePerPersonIndex,
        };

    /// <summary>
    /// Gives a number to each of these people who holds none on this trip, in the order given, each
    /// one past the highest the trip has ever given. People who already hold one are left alone.
    /// </summary>
    /// <param name="caverIds">
    /// The people the trip names, in the order they are to be numbered when new. Naming somebody
    /// twice is harmless.
    /// </param>
    public static async Task AssignAsync(
        SilexGisDbContext db, Guid tripLogId, IEnumerable<Guid> caverIds, CancellationToken ct)
    {
        // What is stored, and what an earlier call in this same unit of work added and has not
        // saved yet: the second is invisible to a query and is precisely what a caller that
        // prepares several writes before one save would otherwise number twice.
        var stored = await db.TripPartyNumbers.Where(x => x.TripLogId == tripLogId).ToListAsync(ct);
        var known = stored
            .Concat(db.TripPartyNumbers.Local.Where(x =>
                x.TripLogId == tripLogId && db.Entry(x).State == EntityState.Added))
            .Distinct()
            .ToList();

        var holders = known.Where(x => x.CaverId is not null).Select(x => x.CaverId!.Value).ToHashSet();
        var next = TripPartyNumbering.Next(known.Select(x => x.Number));
        foreach (var caverId in caverIds)
        {
            if (!holders.Add(caverId)) continue;
            db.TripPartyNumbers.Add(new TripPartyNumber
            {
                TripLogId = tripLogId,
                CaverId = caverId,
                Number = next++,
            });
        }
    }

    /// <summary>
    /// Folds the numbers of an entry that is being merged away into the entry that survives it, on
    /// every trip either of them holds one — deleted trips included.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Where only the entry being merged away holds a number, the survivor takes it: they are one
    /// person, and the page that has been showing that number goes on showing it for them. Where
    /// both hold one, <b>the survivor keeps the lower</b> — the earlier of the two to be named, and
    /// so the one a page has shown for longest — and the higher stays taken with nobody holding
    /// it, so that the next person the trip names is not handed a number a page already used.
    /// </para>
    /// <para>
    /// The rows are handed in rather than read here, because reading a deleted trip's rows is a
    /// decision that belongs to the merge and is made, and accounted for, where the merge makes it
    /// for the roster, the reports and the captions.
    /// </para>
    /// </remarks>
    public static void Fold(
        IReadOnlyCollection<TripPartyNumber> ofTheMergedAway,
        IReadOnlyCollection<TripPartyNumber> ofTheSurvivor,
        Guid survivorId)
    {
        var survivorByTrip = ofTheSurvivor.ToDictionary(x => x.TripLogId);
        foreach (var row in ofTheMergedAway)
        {
            if (!survivorByTrip.TryGetValue(row.TripLogId, out var kept))
            {
                row.CaverId = survivorId;
                continue;
            }

            // Persons change rows; numbers never change. The row holding the lower number becomes
            // the survivor's and the other is left holding nobody.
            if (row.Number < kept.Number)
            {
                kept.CaverId = null;
                row.CaverId = survivorId;
            }
            else
            {
                row.CaverId = null;
            }
        }
    }
}
