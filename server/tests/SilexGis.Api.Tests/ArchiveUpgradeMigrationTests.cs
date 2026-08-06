// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// What happens to an archive that already holds files when the schema that gives every
/// upload a document arrives.
///
/// Every other test starts from an empty database, where the step below has nothing to carry
/// and cannot be wrong. The case that can go wrong is an installation with years of uploads
/// in it: each file row has to become a revision of a document, the chains that files kept
/// among themselves have to survive as version numbers, and the column that says which
/// revision a file belongs to has to be filled before it is made required — get the order
/// wrong and the upgrade fails on the first row, or worse, succeeds having invented one.
///
/// It runs on a database of its own, migrated only as far as the schema that predates
/// documents, so the upgrade can be driven forwards over rows that were written before it.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ArchiveUpgradeMigrationTests : IAsyncLifetime
{
    /// <summary>The last schema that knew nothing about documents. Matched by name so it
    /// survives being renumbered.</summary>
    private const string BeforeDocumentsSuffix = "_InitialSchema";

    private static readonly Guid Uploader = new("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid OlderFile = new("00000000-0000-0000-0000-0000000000f1");
    private static readonly Guid NewerFile = new("00000000-0000-0000-0000-0000000000f2");
    private static readonly Guid Photo = new("00000000-0000-0000-0000-0000000000f3");
    private static readonly Guid ReportChain = new("00000000-0000-0000-0000-0000000000c1");
    private static readonly Guid PhotoChain = new("00000000-0000-0000-0000-0000000000c2");

    private readonly string adminConnectionString;
    private readonly string databaseName = $"silexgis_archive_{Guid.NewGuid():N}";
    private string connectionString = null!;

    public ArchiveUpgradeMigrationTests(PostgresFixture postgres) =>
        adminConnectionString = postgres.ConnectionString;

    public async Task InitializeAsync()
    {
        await using (var admin = new NpgsqlConnection(adminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        connectionString =
            new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = databaseName }
                .ConnectionString;
    }

    [Fact]
    public async Task An_archive_of_files_becomes_an_archive_of_documents()
    {
        await using (var db = CreateContext())
        {
            var before = db.Database.GetMigrations()
                .SingleOrDefault(m => m.EndsWith(BeforeDocumentsSuffix, StringComparison.Ordinal));
            before.ShouldNotBeNull(
                "the schema that predates documents is gone — the upgrade path it starts from " +
                "could not be exercised");

            await db.Database.GetService<IMigrator>().MigrateAsync(before);
            await WriteArchiveAsync(db);

            // Which is the whole point: before the upgrade there is no document anywhere, so a
            // passing assertion afterwards cannot be something that was already true.
            (await TableExistsAsync(db, "documents")).ShouldBeFalse();

            await db.Database.MigrateAsync();
        }

        await using var upgraded = CreateContext();

        // One document per chain, keeping the chain's identity, titled after the file at the
        // head of it — so a link anchored on the old version-group id still resolves.
        var report = await upgraded.Documents.SingleAsync(d => d.Id == ReportChain);
        report.Title.ShouldBe("report-v2.pdf");
        report.OwnerUserId.ShouldBe(Uploader);

        // A file whose uploader is unknown still needs an owner, because the column is
        // required; it falls back to the installation's oldest account rather than failing.
        var photo = await upgraded.Documents.SingleAsync(d => d.Id == PhotoChain);
        photo.OwnerUserId.ShouldBe(Uploader);

        // One revision per file row, keeping that file's id, in the order the files were in,
        // with exactly one of them current.
        var revisions = await upgraded.DocumentVersions
            .Where(v => v.DocumentId == ReportChain)
            .OrderBy(v => v.VersionNumber)
            .ToListAsync();
        revisions.Select(v => (v.Id, v.VersionNumber, v.IsCurrent))
            .ShouldBe([(OlderFile, 1, false), (NewerFile, 2, true)]);

        // The date and the uploader moved from the file onto the revision rather than being
        // dropped with the columns that held them.
        revisions[0].DocumentDate.ShouldBe(new DateOnly(2020, 1, 1));
        revisions[1].DocumentDate.ShouldBe(new DateOnly(2021, 1, 1));
        revisions.ShouldAllBe(v => v.UploadedBy == Uploader);

        // Every stored file now points at its revision, which is what lets the column be
        // required at all.
        var files = await upgraded.StoredFiles.AsNoTracking().ToListAsync();
        files.Count.ShouldBe(3);
        files.ShouldAllBe(f => f.DocumentVersionId != default);
        files.Single(f => f.Id == NewerFile).DocumentVersionId.ShouldBe(NewerFile);

        // An image is one page by definition and gets it here; a paged format waits for the
        // reader, which is the only thing that knows the real count.
        var pages = await upgraded.DocumentPages.AsNoTracking().ToListAsync();
        pages.Select(p => (p.FileId, p.PageNumber)).ShouldBe([(Photo, 1)]);
        files.Single(f => f.Id == Photo).PageCount.ShouldBe(1);
        files.Single(f => f.Id == NewerFile).PageCount.ShouldBeNull();

        // And the archive is queued to be read, once, so the documents that were already
        // there become findable by their words rather than only the ones uploaded afterwards.
        var queued = await upgraded.ProcessingJobs.AsNoTracking()
            .Where(j => j.Kind == ProcessingJobKinds.TextExtractionBackfill)
            .CountAsync();
        queued.ShouldBe(1);
    }

    // ---- helpers ----

    private SilexGisDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<SilexGisDbContext>()
            .UseNpgsql(connectionString, o => o.UseNetTopologySuite())
            .UseSnakeCaseNamingConvention()
            .UseOpenIddict()
            .Options);

    /// <summary>
    /// The rows an installation would already have: one account, one two-file version chain,
    /// and one image whose uploader is gone. Written as SQL because at this point the tables
    /// have the shape the older schema gave them, which today's entities no longer describe.
    /// </summary>
    private static async Task WriteArchiveAsync(SilexGisDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO users (
                id, locale, two_factor_authenticator_enabled, two_factor_email_enabled,
                two_factor_sms_enabled, real_name_visibility, bio_visibility, email_visibility,
                phone_visibility, caving_club_visibility, address_visibility,
                address_point_visibility, notify_digest, created_at, updated_at, email_confirmed,
                phone_number_confirmed, two_factor_enabled, lockout_enabled, access_failed_count)
            VALUES ('00000000-0000-0000-0000-0000000000a1', 'en', false, false, false,
                    0, 0, 0, 0, 0, 0, 0, 0, now(), now(), true, false, false, true, 0);
            """);

        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO files (
                id, storage_path, original_name, mime_type, size_bytes, sha256, uploaded_by,
                version_group_id, version_number, kind, document_date, metadata, created_at,
                updated_at)
            VALUES
                ('00000000-0000-0000-0000-0000000000f1', 'a/1.pdf', 'report.pdf',
                 'application/pdf', 10, 'aa', '00000000-0000-0000-0000-0000000000a1',
                 '00000000-0000-0000-0000-0000000000c1', 1, 1, DATE '2020-01-01', jsonb_build_object(),
                 now(), now()),
                ('00000000-0000-0000-0000-0000000000f2', 'a/2.pdf', 'report-v2.pdf',
                 'application/pdf', 20, 'bb', '00000000-0000-0000-0000-0000000000a1',
                 '00000000-0000-0000-0000-0000000000c1', 2, 1, DATE '2021-01-01', jsonb_build_object(),
                 now(), now()),
                ('00000000-0000-0000-0000-0000000000f3', 'a/3.jpg', 'photo.jpg',
                 'image/jpeg', 30, 'cc', NULL,
                 '00000000-0000-0000-0000-0000000000c2', 1, 0, NULL, jsonb_build_object(), now(), now());
            """);
    }

    private static async Task<bool> TableExistsAsync(SilexGisDbContext db, string table)
    {
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT to_regclass('public.' || @name) IS NOT NULL";
        var name = command.CreateParameter();
        name.ParameterName = "name";
        name.Value = table;
        command.Parameters.Add(name);
        var exists = (bool)(await command.ExecuteScalarAsync())!;
        await db.Database.CloseConnectionAsync();
        return exists;
    }

    public async Task DisposeAsync()
    {
        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }
}
