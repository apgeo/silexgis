// SPDX-License-Identifier: AGPL-3.0-or-later
using Npgsql;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// Rows for a database migrated to an older step, written by SQL against that step's columns.
/// </summary>
/// <remarks>
/// A migration test fills a database as an older step left it and then migrates it on. Writing
/// those rows through today's model would name every column the model has now, so the first later
/// migration that adds a column to the table would break the test at the insert, for a reason that
/// has nothing to do with the migration it tests. A statement names only the columns it sets, and
/// the ones here are those every step since the baseline has: the rest are left to their defaults.
/// </remarks>
internal static class MigrationFixtureSql
{
    /// <summary>An invented account, with the columns the baseline requires.</summary>
    public static string User(Guid id, string userName) => $"""
        INSERT INTO users (id, access_failed_count, address_point_visibility, address_visibility,
            bio_visibility, caving_club_visibility, created_at, email_confirmed, email_visibility,
            locale, lockout_enabled, phone_number_confirmed, phone_visibility, real_name_visibility,
            two_factor_authenticator_enabled, two_factor_email_enabled, two_factor_enabled,
            two_factor_sms_enabled, updated_at, user_name, normalized_user_name, email)
        VALUES ('{id}', 0, 0, 0, 0, 0, now(), true, 0, 'en', false, false, 0, 0,
            false, false, false, false, now(), '{userName}', '{userName.ToUpperInvariant()}',
            '{userName}@example.invalid');
        """;

    /// <summary>
    /// An invented document with one current revision holding one file. The file's kind, name and
    /// creation moment are the caller's.
    /// </summary>
    public static string DocumentWithFile(
        Guid owner, Guid document, Guid version, Guid file, string title, short fileKind, string originalName) => $"""
        INSERT INTO documents (id, title, owner_user_id, visibility, created_at, updated_at)
        VALUES ('{document}', '{title}', '{owner}', 0, now(), now());
        INSERT INTO document_versions (id, document_id, version_number, is_current, created_at, updated_at)
        VALUES ('{version}', '{document}', 1, true, now(), now());
        INSERT INTO files (id, storage_path, original_name, mime_type, size_bytes, sha256,
            document_version_id, kind, created_at, updated_at)
        VALUES ('{file}', 'invented/{file:N}', '{originalName}', 'application/octet-stream', 12,
            '{new string('b', 64)}', '{version}', {fileKind}, now(), now());
        """;

    public static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task<long> CountAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    public static async Task<string?> TextAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
    }
}
