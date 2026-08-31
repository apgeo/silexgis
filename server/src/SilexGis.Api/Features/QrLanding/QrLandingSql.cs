// SPDX-License-Identifier: AGPL-3.0-or-later
using Dapper;
using Microsoft.EntityFrameworkCore;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.QrLanding;

/// <summary>
/// The stored-code lookup behind the landing route (raw SQL lives only in *Sql.cs files).
/// </summary>
/// <remarks>
/// <para>
/// The codes live in the feature's property document, which no expression the object mapper
/// can build reaches: the column is held as text and the only JSON operators available through
/// the mapper are containment and key existence, neither of which can express "these two keys,
/// compared without regard to case". So this is written out, parameterised, and given the two
/// expression indexes that make it a lookup rather than a scan of every feature.
/// </para>
/// <para>
/// Both codes are matched, and matched case-insensitively, because that is what the device that
/// prints them does when it reads one back: the hashed form is lowercase by construction but a
/// place code can carry whatever case somebody typed, and a QR reader may hand back a different
/// case than the one that was stored.
/// </para>
/// <para>
/// Nothing here recomputes a code from the data it names. A stored code may legally disagree
/// with what the settings in force today would produce — the generator's mode, length and salt
/// all live in the dataset that generated it and may have changed since, and a code that is
/// already bolted to a cave wall is not made wrong by that. Only the stored value resolves.
/// </para>
/// </remarks>
public static class QrLandingSql
{
    /// <summary>
    /// Whether some feature carrying this code sits under a cave whose codes a person decided
    /// may be resolved by anyone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The answer is a yes or a no and deliberately not a row. Nothing constrains a code to be
    /// unique — two datasets syncing into one installation can legally land the same one on two
    /// features, and no index anywhere forbids it — so more than one feature may match. That is
    /// answered as a single match, which is only harmless while the answer names nothing: the
    /// caller learns that the code resolves and which installation it resolves at, and both of
    /// those are the same for every match. <b>The day this route's answer names the thing it
    /// found, a second match stops being a duplicate and becomes an ambiguity that has to be
    /// resolved before anything can be shown.</b>
    /// </para>
    /// <para>
    /// A code on a place inside a cave resolves through the cave, because the containment
    /// ancestry a feature carries includes itself: the decision is taken about a cave and
    /// everything under it inherits it, and no label can be live under a cave nobody published.
    /// Both the coded feature and the published cave must still exist — deletion here stamps a
    /// row rather than removing it, so a code on a deleted place, or under a deleted cave, has
    /// nothing left to answer about and answers as an unknown code does.
    /// </para>
    /// </remarks>
    public static async Task<bool> ResolvesAsync(SilexGisDbContext db, string code, CancellationToken ct)
    {
        const string sql = """
            SELECT 1
            FROM features f
            JOIN cave_qr_publications p
              ON p.feature_id = ANY(f.ancestor_ids) AND p.revoked_at IS NULL
            JOIN features c
              ON c.id = p.feature_id AND c.deleted_at IS NULL
            WHERE f.deleted_at IS NULL
              AND (lower(f.properties->>'speleolocQcri') = @code
                OR lower(f.properties->>'speleolocPci') = @code)
            LIMIT 1
            """;

        var connection = db.Database.GetDbConnection();
        var hit = await connection.ExecuteScalarAsync<int?>(
            new CommandDefinition(sql, new { code }, cancellationToken: ct));

        return hit is not null;
    }
}
