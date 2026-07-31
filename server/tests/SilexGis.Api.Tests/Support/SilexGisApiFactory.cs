// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SilexGis.Domain;
using SilexGis.Domain.Messaging;

namespace SilexGis.Api.Tests.Support;

/// <summary>Boots the real application against the test PostGIS container (migrations + seed run on start).</summary>
public sealed class SilexGisApiFactory(string connectionString, IDictionary<string, string?>? settings = null)
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
        // The OIDC client registration is (re)seeded from PublicUrl on startup and the DB is
        // shared across factories — every test factory must use the TestServer origin.
        builder.UseSetting("PublicUrl", "http://localhost");
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
