// SPDX-License-Identifier: AGPL-3.0-or-later
using Npgsql;
using Testcontainers.PostgreSql;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// A database of its own for every test class, cloned from one already-migrated template.
///
/// <para>
/// The schema is built once per test process — the migrations and every seeder, a couple of
/// seconds — into a template database nothing else writes to. Each class then takes a copy with
/// <c>CREATE DATABASE ... TEMPLATE</c>, which PostgreSQL serves by copying the template's files:
/// around a tenth of a second for this schema. So a class pays a tenth of a second for an
/// installation of its own rather than two seconds to build one, or nothing at all to share
/// everybody else's.
/// </para>
/// <para>
/// Sharing was the older arrangement and it cost more than it saved. One database for the whole
/// run put every class in a single xunit collection, which runs its classes strictly in turn, so
/// the suite could never use a second core however much hardware was under it. It also let a
/// class pass on rows another class happened to leave behind, and several classes carried blocks
/// of compensating deletion that existed only to undo their neighbours. A class that owns its
/// database needs none of that.
/// </para>
/// <para>
/// One container still serves the whole process; it is the database inside it that is per class.
/// No fixture disposes it — which class finishes last is not knowable from inside one — so it is
/// rooted in a static field for the process lifetime and left to the Testcontainers reaper at exit.
/// </para>
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string TemplateDatabase = "silexgis_template";

    private static readonly SemaphoreSlim TemplateGate = new(1, 1);
    private static string? maintenanceConnectionString;

    /// <summary>
    /// Keeps the container alive for as long as the process runs, and this field is the whole
    /// reason it does.
    ///
    /// <para>
    /// Nothing else refers to it once the template has been built, so without a root here the
    /// object becomes collectable — and <c>DockerContainer</c> stops its container when it is
    /// finalised. The result is a database that disappears in the middle of a run: the collection
    /// lands at whatever moment the heap happens to fill, every test still to run fails instantly
    /// on "connection refused", and the cause is three hundred failures away from the class that
    /// was executing when it happened. Observed once, at the ten-hour mark of a full run, as 358
    /// socket failures behind a container that had exited cleanly with status 0.
    /// </para>
    /// </summary>
    private static PostgreSqlContainer? liveContainer;

    private readonly string databaseName = $"silexgis_{Guid.NewGuid():N}";

    /// <summary>The database this test class owns. Nothing else in the run can see it.</summary>
    public string ConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var maintenance = await EnsureTemplateAsync();
        await ExecuteAsync(
            maintenance, $"CREATE DATABASE \"{databaseName}\" TEMPLATE \"{TemplateDatabase}\"");
        ConnectionString = WithDatabase(maintenance, databaseName);
    }

    public async Task DisposeAsync()
    {
        if (maintenanceConnectionString is null)
        {
            return;
        }

        // This pool and no other. ClearAllPools is process-wide, and with the classes running in
        // parallel it would throw away the pooled connections of every class still mid-test —
        // once per class, a hundred times over a run.
        await using (var mine = new NpgsqlConnection(ConnectionString))
        {
            NpgsqlConnection.ClearPool(mine);
        }

        await ExecuteAsync(
            maintenanceConnectionString, $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)");
    }

    private static async Task<string> EnsureTemplateAsync()
    {
        if (maintenanceConnectionString is not null)
        {
            return maintenanceConnectionString;
        }

        await TemplateGate.WaitAsync();
        try
        {
            if (maintenanceConnectionString is not null)
            {
                return maintenanceConnectionString;
            }

            var container = new PostgreSqlBuilder("postgis/postgis:17-3.5")
                .WithDatabase("postgres")
                .WithUsername("silexgis")
                .WithPassword("silexgis")
                // One connection pool per class and the classes running together: the stock 100 is
                // reached at around a dozen. Durability settings were measured and deliberately
                // left alone — they bought nothing, because the time goes on building hosts rather
                // than on writing to disk, and turning fsync off leaves a cluster whose corruption
                // nobody can afterwards explain.
                .WithCommand("-c", "max_connections=400")
                .Build();
            await container.StartAsync();
            liveContainer = container;
            var maintenance = container.GetConnectionString();

            await ExecuteAsync(maintenance, $"CREATE DATABASE \"{TemplateDatabase}\"");

            // Booting the real application against the template is what puts the schema and the
            // seeded rows into it, so a clone needs neither migration nor seeding. No installation
            // administrator is configured: without Admin:Email the application makes none, so every
            // class starts from the same administrator-less installation and a class that wants one
            // makes it. Under the shared database a class could instead find whichever
            // administrator a neighbour had happened to create first.
            await using (var factory =
                new SilexGisApiFactory(WithDatabase(maintenance, TemplateDatabase)))
            {
                _ = factory.CreateClient();
            }

            // A template cannot be copied while anything is connected to it, and the host just
            // built left connections behind in the pool that outlive its disposal.
            await using (var template =
                new NpgsqlConnection(WithDatabase(maintenance, TemplateDatabase)))
            {
                NpgsqlConnection.ClearPool(template);
            }

            await ExecuteAsync(
                maintenance,
                "SELECT pg_terminate_backend(pid) FROM pg_stat_activity "
                    + $"WHERE datname = '{TemplateDatabase}'");

            maintenanceConnectionString = maintenance;
            return maintenance;
        }
        finally
        {
            TemplateGate.Release();
        }
    }

    private static string WithDatabase(string connectionString, string database) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Database = database }.ConnectionString;

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 300 };
        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>
/// Groups the classes that must not run beside anything else.
///
/// <para>
/// A class asserting that an endpoint stays responsive is measuring the machine, not the code, and
/// it cannot hold while seven other classes are seeding their own installations on the same box.
/// Left in the general pool it fails on a busy run and passes on a quiet one, which is a test
/// reporting the load average. Its own collection, with parallelisation off, is the only place
/// such an assertion means anything.
/// </para>
/// <para>
/// Membership is a cost: every class in here runs in turn, so it holds only classes whose subject
/// is time. A class that merely takes a while belongs in the general pool.
/// </para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SerialCollection
{
    public const string Name = "serial";
}
