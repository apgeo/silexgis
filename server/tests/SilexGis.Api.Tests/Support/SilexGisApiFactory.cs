// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using SilexGis.Domain;
using SilexGis.Domain.Messaging;

namespace SilexGis.Api.Tests.Support;

/// <summary>Boots the real application against the test PostGIS container (migrations + seed run on start).</summary>
/// <remarks>
/// <para>
/// In fast mode (<see cref="TestMode"/>) the factory a test class makes while it is being
/// constructed is handed the running application an earlier test of the class left, where that
/// test asked for the same database and settings, instead of building another. What a test then
/// still has to itself: its clients, an empty record of messages sent, and an application whose
/// memory cache has been emptied. What it shares with the tests before it: everything else the
/// application holds in memory — a limiter's window, what a worker has in hand — and its file
/// store.
/// </para>
/// <para>
/// Some factories never share, and each builds an application as it would in precise mode. One
/// that asks for <c>ownHost</c>. One whose <c>configureServices</c> holds something — a local, a
/// field, <c>this</c>: what such a delegate puts into the application is an object the test
/// holds, and a borrowed application would hold an earlier test's. One made inside a test rather
/// than while the class is constructed: a test that builds an application mid-way is usually
/// about what a newly started one does, and a borrowed one started long ago. And a second one
/// asking for an application that is in use — two nodes of one installation, a restart.
/// </para>
/// </remarks>
public sealed class SilexGisApiFactory : WebApplicationFactory<Program>
{
    private readonly string connectionString;
    private readonly IDictionary<string, string?>? settings;
    private readonly Action<IServiceCollection>? configureServices;
    private readonly SharedHosts.Entry? shared;
    private readonly string builtBecause;
    private readonly string? maker;
    private bool bound;
    private int released;

    /// <param name="connectionString">The class's database.</param>
    /// <param name="settings">Configuration applied over the suite's defaults.</param>
    /// <param name="configureServices">
    /// Replaces a service with a test double where the real one talks to something that is not
    /// there — an optional external service, for instance. Everything else stays the real wiring.
    /// </param>
    /// <param name="ownHost">
    /// Builds an application for this factory alone even in fast mode — for a test whose subject
    /// is what a newly started application does, or what one has in memory.
    /// </param>
    // Not inlined, so that the frame above this one is the caller's whatever the build.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public SilexGisApiFactory(
        string connectionString,
        IDictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configureServices = null,
        bool ownHost = false)
    {
        this.connectionString = connectionString;
        this.settings = settings;
        this.configureServices = configureServices;

        if (!TestMode.Fast)
        {
            builtBecause = "precise mode";
            Messages = new MessageCapture();
            return;
        }

        (var constructing, maker) = WhoIsMakingThis();
        builtBecause = ownHost ? "asked for its own"
            : CarriesSomething(configureServices) ? "given services of the test's own"
            : !constructing ? "made inside a test"
            : string.Empty;
        if (builtBecause.Length > 0)
        {
            Messages = new MessageCapture();
            return;
        }

        var stores = PerTestStores.In(settings);
        if (stores.AlreadyStandIns
            || SharedHosts.Take(connectionString, stores.Key + KeyOf(configureServices)) is not { } taken)
        {
            builtBecause = "a second application beside one in use";
            Messages = new MessageCapture();
            return;
        }

        var entry = taken.Entry;
        if (!taken.Running)
        {
            entry.Stores = [.. stores.Directories.Select((_, index) => StoreFor(connectionString, entry.Number, index))];
            this.settings = stores.Over(entry.Stores);
        }

        if (!PerTestStores.TryLink(stores.Directories, entry.Stores, entry.Linked))
        {
            GiveBack(entry, taken.Running);
            this.settings = settings;
            builtBecause = "its own directories could not be linked";
            Messages = new MessageCapture();
            return;
        }

        shared = entry;
        Messages = entry.Messages;
        // The one thing every test is still promised: nothing in here was sent before it began.
        Messages.Clear();
        Messages.MailConfigured = true;
        Messages.SmsConfigured = true;
        Messages.FailSendsTo = null;
        builtBecause = "the first to ask for it";
        try
        {
            // Now rather than at first use, so that the application this factory stands for is
            // settled before anything can be derived from it.
            StartServer();
        }
        catch
        {
            GiveBack(entry, taken.Running);
            throw;
        }

        if (taken.Running)
        {
            // What the application remembers of its database — the settings, the message
            // templates — is forgotten between tests. Many classes put those back by writing to
            // the database underneath the application, which a new application never noticed
            // and a running one would go on remembering for as long as it caches them.
            (Services.GetService<IMemoryCache>() as MemoryCache)?.Clear();
            SharedHosts.CountBorrowed(maker);
        }
    }

    /// <summary>
    /// Everything the application sent during the test. Only the two transports are replaced —
    /// the dispatcher, the templates and the settings driving them are the real ones, so a test
    /// that reads a code out of a message has proved the whole path produced it.
    /// </summary>
    public MessageCapture Messages { get; }

    /// <summary>
    /// True when what a delegate puts into the application can differ from one test to the next:
    /// when it holds something. A static method holds nothing, and neither does a lambda that
    /// refers to nothing around it — the compiler hangs that on an object with no fields. One that
    /// uses a local, a field or <c>this</c> holds it, and what it holds is the test's own.
    /// </summary>
    private static bool CarriesSomething(Action<IServiceCollection>? configureServices)
    {
        if (configureServices is null)
        {
            return false;
        }

        if (configureServices.GetInvocationList().Length != 1)
        {
            return true;
        }

        if (configureServices.Target is not { } holder)
        {
            return false;
        }

        // Only the compiler's own fieldless holder counts as nothing. Any other object is the
        // test's — the test class itself, when the lambda uses `this` — whatever its fields.
        var type = holder.GetType();
        return !type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
            || type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Length > 0;
    }

    private static void GiveBack(SharedHosts.Entry entry, bool wasRunning)
    {
        if (wasRunning)
        {
            SharedHosts.Release(entry);
        }
        else
        {
            SharedHosts.Abandon(entry);
        }
    }

    /// <summary>
    /// The directory standing in for the n-th directory the tests name for themselves, for one of
    /// a class's applications: beside the class's default store, which is named after its database.
    /// </summary>
    private static string StoreFor(string connectionString, int application, int index) =>
        Path.Combine(
            TestScratch.Root,
            $"{new NpgsqlConnectionStringBuilder(connectionString).Database}.store-{application}-{index}");

    /// <summary>
    /// A method that holds nothing stands for itself: the same method is the same change to the
    /// application whichever test names it. Anything else never reaches here.
    /// </summary>
    private static string KeyOf(Action<IServiceCollection>? configureServices) =>
        configureServices is null
            ? string.Empty
            : $"\nservices={configureServices.Method.Module.ModuleVersionId}:{configureServices.Method.MetadataToken}";

    /// <summary>
    /// Whether this factory is being made by a constructor of the test project — a test class
    /// being put together for its next test, or a fixture — rather than by a test as it runs; and
    /// the class doing it, for the account of what was built.
    /// </summary>
    /// <remarks>
    /// Read off the stack because that is where the difference is: xunit constructs the class
    /// afresh for every test, so what a constructor builds is by construction the same thing each
    /// time, while what a test builds halfway through is part of what that test is about. The
    /// walk stops at the first frame outside this assembly, so an asynchronous method — whose
    /// frames are the runtime's — is never mistaken for a constructor further up.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (bool Constructing, string Maker) WhoIsMakingThis()
    {
        // Two frames down: this method, then the factory's own constructor.
        var here = typeof(SilexGisApiFactory).Assembly;
        string? nearest = null;
        foreach (var frame in new StackTrace(skipFrames: 2, fNeedFileInfo: false).GetFrames())
        {
            var method = frame.GetMethod();
            if (method?.DeclaringType is not { } type || type.Assembly != here)
            {
                break;
            }

            // A lambda or an asynchronous method lives in a type nested in the one that wrote it.
            while (type.DeclaringType is { } outer)
            {
                type = outer;
            }

            if (method is ConstructorInfo { IsStatic: false })
            {
                return (true, type.Name);
            }

            nearest ??= type.Name;
        }

        return (false, nearest ?? "(not the test project)");
    }

    /// <summary>
    /// The application behind this factory: the class's shared one when this factory took it, and
    /// otherwise one built here — which is also what a factory derived from this one gets, since
    /// it asks for an application configured differently.
    /// </summary>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        if (shared is null || bound)
        {
            SharedHosts.CountBuilt(shared is null ? builtBecause : "derived from another factory", maker);
            return base.CreateHost(builder);
        }

        bound = true;
        if (shared.Host is { } running)
        {
            return running;
        }

        SharedHosts.CountBuilt(builtBecause, maker);
        var built = base.CreateHost(builder);
        shared.Started(built);
        return built;
    }

    /// <summary>
    /// Gives a shared application back rather than stopping it; the class's database fixture
    /// stops it when the class is over. Anything derived from this factory is its own and goes now.
    /// </summary>
    public override async ValueTask DisposeAsync()
    {
        if (shared is null)
        {
            await base.DisposeAsync();
            return;
        }

        if (Interlocked.Exchange(ref released, 1) == 1)
        {
            return;
        }

        foreach (var derived in Factories)
        {
            await derived.DisposeAsync();
        }

        SharedHosts.Release(shared);
        GC.SuppressFinalize(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (shared is null)
        {
            base.Dispose(disposing);
            return;
        }

        if (disposing)
        {
            DisposeAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// Every client this factory makes waits as long as the test itself is allowed to.
    ///
    /// <para>
    /// HttpClient's default deadline is 100 seconds, which is a statement about a network nobody
    /// here is crossing: these requests are in-process. Under a full parallel run the deadline is
    /// reachable anyway — hosts are being built and accounts hashed on every other thread — and
    /// when it is reached the request is cancelled client-side and reported as
    /// <c>TaskCanceledException … the client aborted the request</c> with a duration of one
    /// millisecond, in whichever class happened to be slowest. Two full runs recorded eight such
    /// failures each with no test in common between them, which is the signature of a deadline
    /// rather than of a defect.
    /// </para>
    /// <para>
    /// Ten minutes rather than no deadline at all. Nothing here has a per-test timeout, so an
    /// infinite one turns a request that genuinely never returns into a run that never ends —
    /// a worse failure than the one being fixed, and a silent one. Ten minutes is far outside
    /// anything a correct in-process request takes even on a saturated machine, and far inside
    /// the patience of whoever is waiting for the suite.
    /// </para>
    /// </summary>
    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.Timeout = TimeSpan.FromMinutes(10);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Db:ConnectionString", connectionString);
        builder.UseSetting("Db:AutoMigrate", "true");
        // The OIDC client registration is (re)seeded from PublicUrl on startup, and one database
        // is shared by every factory a class builds — so every test factory must use the
        // TestServer origin.
        builder.UseSetting("PublicUrl", "http://localhost");
        // The notification worker never runs in tests. A class owns its database, so a drain can
        // no longer reach another class's rows — but it still races the class that queued them,
        // settling an outbox row between the request and the assertion a few milliseconds later,
        // which turns "nothing was sent" into an intermittent failure about correct code. Tests
        // drive the delivery service directly instead.
        builder.UseSetting("Notifications:PollSeconds", "0");
        // Nor does the pass that looks for overdue parties, and for a sharper version of the same
        // reason: it does not merely read rows, it writes them. Left running it would move the
        // class's own armed trip to overdue and queue an alarm nobody asked for, at whichever tick
        // happened to land there. A class that wants the pass switched on passes its own interval,
        // which is applied after this.
        builder.UseSetting("TripCallout:SweepInterval", "00:00:00");
        // The reminder rides that same pass, and a pass a class runs by hand still reads every
        // trip in that class's database — so the run-up window is closed too. A class exercising
        // the reminder opens it deliberately.
        builder.UseSetting("TripCallout:ReminderLead", "00:00:00");
        // The file store and the key ring default to a path under the test binaries, which every
        // host in the process would otherwise share. Keyed by database name they are per test
        // class — the same place for every factory a class builds, and nowhere another class
        // reaches once each class owns its database.
        var scopeName = new NpgsqlConnectionStringBuilder(connectionString).Database!;
        // Under the build output rather than Path.GetTempPath(): /tmp here is a tmpfs, i.e. RAM,
        // and a hundred classes writing uploads into it takes memory from everything else on the
        // machine. `dotnet clean` reclaims this; a reboot reclaims the other.
        var scopeRoot = Path.Combine(AppContext.BaseDirectory, "test-data", scopeName);
        builder.UseSetting("Files:Root", Path.Combine(scopeRoot, "files"));
        builder.UseSetting("Keys:Path", Path.Combine(scopeRoot, "keys"));
        // Sign-in throttling is a property under test in a few classes, which set their own limit
        // below this line and therefore still win. Everywhere else it is an accident of the traffic
        // a class generates for itself: raise it so a class's own requests cannot trip it.
        builder.UseSetting("Auth:RateLimitPerMinute", "100000");
        if (settings is not null)
        {
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(Messages);
            services.AddScoped<CapturingEmailSender>();
            services.AddScoped<IEmailSender>(sp => sp.GetRequiredService<CapturingEmailSender>());
            services.AddScoped<IEmailDelivery>(sp => sp.GetRequiredService<CapturingEmailSender>());
            services.AddScoped<CapturingSmsSender>();
            services.AddScoped<ISmsSender>(sp => sp.GetRequiredService<CapturingSmsSender>());
            services.AddScoped<ISmsDelivery>(sp => sp.GetRequiredService<CapturingSmsSender>());
            // Password hashing is deliberately expensive in production and nothing here asserts
            // on its cost. At the shipped iteration count it is the single largest CPU item in a
            // run that creates an account for almost every test.
            services.Configure<PasswordHasherOptions>(o => o.IterationCount = 1000);
            configureServices?.Invoke(services);
        });
    }
}

internal sealed class CapturingEmailSender(MessageCapture capture) : IEmailSender, IEmailDelivery
{
    public ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default) =>
        ValueTask.FromResult(capture.MailConfigured);

    public Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        capture.Record("email", to, subject, body);
        return Task.CompletedTask;
    }
}

internal sealed class CapturingSmsSender(MessageCapture capture) : ISmsSender, ISmsDelivery
{
    public ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default) =>
        ValueTask.FromResult(capture.SmsConfigured);

    public Task SendAsync(string to, string text, CancellationToken ct = default)
    {
        capture.Record("sms", to, null, text);
        return Task.CompletedTask;
    }
}
