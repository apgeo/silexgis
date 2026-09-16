// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net.Sockets;
using DotNet.Testcontainers.Containers;
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
        try
        {
            await ExecuteAsync(
                maintenance, $"CREATE DATABASE \"{databaseName}\" TEMPLATE \"{TemplateDatabase}\"");
        }
        // PostgresException is deliberately excluded, and it derives from NpgsqlException so the
        // order matters: a server that answers with an error is a server that is there, and
        // reporting "the database has gone" over a missing template or a duplicate name would
        // bury the real fault under a confident wrong answer. Only a connection that cannot be
        // made or cannot be read counts.
        catch (Exception ex) when (
            ex is not PostgresException && ex is NpgsqlException or IOException or SocketException)
        {
            throw DatabaseHasGone(ex);
        }

        ConnectionString = WithDatabase(maintenance, databaseName);
    }

    /// <summary>
    /// Says once, in words, that the database has been taken away — rather than letting every
    /// remaining class discover it one socket at a time.
    ///
    /// <para>
    /// A container that disappears mid-run is not hypothetical here: it has happened three times,
    /// by three different routes — finalised by the garbage collector when nothing rooted it,
    /// removed by a Testcontainers reaper while several sessions ran suites at once, and killed
    /// with the process group that launched it. Each time the visible result was the same, and
    /// useless: every test still to run threw <c>SocketException</c> or
    /// <c>EndOfStreamException</c> from wherever it happened to be, filling a log of tens of
    /// thousands of lines that named the database in none of them. One run produced 1847 failures
    /// and 5.6 MB of stack traces for a cause that fits on one line.
    /// </para>
    /// <para>
    /// This does not make the suite survive it — nothing can, the database is gone — but a class
    /// that has not started yet now fails with the reason instead of the symptom, and those are
    /// the overwhelming majority.
    /// </para>
    /// </summary>
    private static InvalidOperationException DatabaseHasGone(Exception cause)
    {
        // Deliberately not asked of the container object: IContainer.State is whatever the last
        // inspect returned, so a container removed underneath the process still reports Running
        // and a check against it says nothing. What is known for certain is that this fixture
        // could not reach the server, and at this point in a class's life that is the same thing.
        var state = liveContainer is null ? "never started" : $"last seen {liveContainer.State}";
        return new InvalidOperationException(
            "THE TEST DATABASE IS UNREACHABLE — this is an environment failure, not a test "
                + $"failure. The container was {state}, and this class could not open a connection "
                + "to it for ninety seconds of retrying. Every class that has not yet started will "
                + "fail here for the same reason; the ones already running will fail wherever they "
                + "happened to be, on a socket, naming nothing. Re-run, and do not investigate the "
                + "individual failures — they are all one event. "
                + "A reset that clears on retry is ordinary here and is handled silently: the "
                + "postmaster accepts serially and eight classes building a host per test overflow "
                + "its accept queue. This message means the retries ran out, so look at whether "
                + "the container is still there and what else on the machine was competing for it.",
            cause);
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
            await WaitUntilAcceptingConnectionsAsync(maintenance);

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

    /// <summary>
    /// Waits for a server that will still be there a moment later.
    ///
    /// <para>
    /// The image runs <c>initdb</c>, announces that the database system is ready, and then bounces
    /// the server before serving properly — so the readiness the container reports can be the first
    /// of those two, and a connection opened against it is closed underneath whatever is using it.
    /// It surfaces as <c>Npgsql … Exception while reading from stream</c> out of
    /// <c>MigrateAsync</c>, in the first class to reach the template, and because the template is
    /// built once per process a single unlucky moment fails the entire run rather than one test.
    /// Observed: six of six in a class, all in migration, fourteen seconds in; the same command
    /// passed twice immediately afterwards.
    /// </para>
    /// <para>
    /// Opening and closing a connection is not enough to tell the two apart, so this asks a
    /// question and requires the answer to arrive.
    /// </para>
    /// </summary>
    private static async Task WaitUntilAcceptingConnectionsAsync(string connectionString)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (true)
        {
            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand("SELECT 1", connection);
                _ = await command.ExecuteScalarAsync();
                return;
            }
            catch (Exception) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(250);
            }
        }
    }

    /// <summary>
    /// Stops the container the process started, at process exit, because the reaper that would
    /// otherwise do it is switched off — see <see cref="TestHostDefaults"/> for why.
    /// </summary>
    internal static void StopContainer()
    {
        var container = Interlocked.Exchange(ref liveContainer, null);
        if (container is null)
        {
            return;
        }

        try
        {
            // Synchronous on purpose: process exit does not wait for anything this does not make
            // it wait for, and a container left running is a worse outcome than a slow exit.
            container.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch
        {
            // Exiting is not the moment to fail. A container that outlives this is removed by the
            // same prune that removes any other leftover.
        }
    }

    private static string WithDatabase(string connectionString, string database) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Database = database }.ConnectionString;

    /// <summary>
    /// Runs one statement, retrying a connection the server reset rather than refused.
    ///
    /// <para>
    /// Opening a connection is not reliable here and the reason is rate rather than capacity.
    /// Every test builds its own application host, every host opens its own pool, and eight
    /// classes do it at once: the postmaster accepts serially, its accept queue overflows, and the
    /// kernel resets the connections that did not fit. It arrives as
    /// <c>SocketException: Connection reset by peer</c> from inside
    /// <c>NpgsqlConnector.SetupEncryption</c> — before authentication, which is why the server
    /// never says "too many clients" and its log shows nothing wrong. Measured: 295 of 393 tests
    /// in one run, against a container that was still running when the run ended.
    /// </para>
    /// <para>
    /// A reset at that point means the server was busy, not broken, so the answer is to ask again.
    /// Anything the server answers — a real SQL error — is not retried and not disguised.
    /// </para>
    /// </summary>
    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);
        var delay = 50;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 300 };
                await command.ExecuteNonQueryAsync();
                return;
            }
            catch (Exception ex) when (
                ex is not PostgresException
                && ex is NpgsqlException or IOException or SocketException
                && DateTime.UtcNow < deadline)
            {
                await Task.Delay(delay);
                delay = Math.Min(delay * 2, 1000);
            }
        }
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
