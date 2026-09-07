// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SilexGis.Domain;
using SilexGis.Domain.Messaging;

namespace SilexGis.Api.Tests.Support;

/// <summary>Boots the real application against the test PostGIS container (migrations + seed run on start).</summary>
/// <param name="configureServices">
/// Replaces a service with a test double where the real one talks to something that is not
/// there — an optional external service, for instance. Everything else stays the real wiring.
/// </param>
public sealed class SilexGisApiFactory(
    string connectionString,
    IDictionary<string, string?>? settings = null,
    Action<IServiceCollection>? configureServices = null)
    : WebApplicationFactory<Program>
{
    /// <summary>
    /// Everything the application sent during the test. Only the two transports are replaced —
    /// the dispatcher, the templates and the settings driving them are the real ones, so a test
    /// that reads a code out of a message has proved the whole path produced it.
    /// </summary>
    public MessageCapture Messages { get; } = new();

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
