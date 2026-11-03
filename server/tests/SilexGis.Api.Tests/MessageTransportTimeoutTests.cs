// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Auth;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain.Auth;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Sms;

namespace SilexGis.Api.Tests;

/// <summary>
/// The real SMS sender against a gateway that never answers: the timeout the HTTP client raises
/// is a cancellation nobody asked for, and it has to come back as a failed send rather than
/// escape as one.
/// </summary>
/// <remarks>
/// The gateway is a handler that waits forever, so what times out is the client's own timeout
/// with the exception shape .NET really gives it — not a hand-made exception standing in for it.
/// </remarks>
public sealed class MessageTransportTimeoutTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task A_gateway_that_never_answers_is_a_failed_send_not_an_escaped_cancellation()
    {
        var gateway = new SilentGateway();
        using var factory = Host(gateway);
        await using var scope = factory.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IMessageDispatcher>();

        var result = await AccountMessages.SendTwoFactorCodeAsync(
            dispatcher, Person(), TwoFactorMethod.Sms, "123456", 10);

        gateway.Asked.ShouldBe(1, "the request did reach the gateway");
        result.Sent.ShouldBeFalse();
        result.Permanent.ShouldBeFalse("a gateway that is slow today may answer tomorrow");
        result.Error.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_cancellation_the_caller_asked_for_still_leaves_as_one()
    {
        var gateway = new SilentGateway();
        using var factory = Host(gateway);
        await using var scope = factory.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IMessageDispatcher>();

        // Cancelled while the request is waiting on the gateway, well inside the client's timeout.
        using var shutdown = new CancellationTokenSource();
        gateway.WhenAsked = () => shutdown.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Should.ThrowAsync<OperationCanceledException>(() =>
            AccountMessages.SendTwoFactorCodeAsync(
                dispatcher, Person(), TwoFactorMethod.Sms, "123456", 10, shutdown.Token));
        gateway.Asked.ShouldBe(1);
    }

    /// <summary>
    /// A text carrying the characters that would break each body format: a quote and a newline for
    /// JSON, an ampersand and an equals sign for a form.
    /// </summary>
    private const string AwkwardText = "Meet at \"the gate\" & bring rope\nlights=on";

    [Fact]
    public async Task A_json_body_carries_quotes_and_newlines_as_one_string_value()
    {
        var gateway = new RecordingGateway();
        using var factory = Host(gateway, """{"to":"{to}","text":"{text}"}""", "application/json");
        await using var scope = factory.Services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<ISmsSender>()
            .SendAsync("+40700000000", AwkwardText, CancellationToken.None);

        var body = JsonDocument.Parse(gateway.Bodies.ShouldHaveSingleItem()).RootElement;
        body.EnumerateObject().Select(p => p.Name).ShouldBe(["to", "text"]);
        body.GetProperty("text").GetString().ShouldBe(AwkwardText);
        body.GetProperty("to").GetString().ShouldBe("+40700000000");
    }

    [Fact]
    public async Task A_form_body_carries_an_ampersand_inside_its_field_rather_than_as_a_new_one()
    {
        var gateway = new RecordingGateway();
        using var factory = Host(gateway, "To={to}&Body={text}", "application/x-www-form-urlencoded");
        await using var scope = factory.Services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<ISmsSender>()
            .SendAsync("+40700000000", AwkwardText, CancellationToken.None);

        var fields = System.Web.HttpUtility.ParseQueryString(gateway.Bodies.ShouldHaveSingleItem());
        fields.AllKeys.ShouldBe(["To", "Body"]);
        fields["Body"].ShouldBe(AwkwardText);
        fields["To"].ShouldBe("+40700000000");
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task A_gateway_that_answers_with_an_error_is_a_failed_send_that_may_be_tried_again(HttpStatusCode status)
    {
        // Not one of these says the number does not exist — a gateway that is overloaded, that
        // refused the credentials or the request shape is a fault the operator can fix — so none
        // of them settles the message for good.
        var gateway = new RecordingGateway { Answer = status };
        using var factory = Host(gateway);
        await using var scope = factory.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IMessageDispatcher>();

        var result = await AccountMessages.SendTwoFactorCodeAsync(
            dispatcher, Person(), TwoFactorMethod.Sms, "123456", 10);

        gateway.Bodies.Count.ShouldBe(1);
        result.Sent.ShouldBeFalse();
        result.Permanent.ShouldBeFalse();
        result.Error.ShouldNotBeNull();
        result.Error.ShouldContain(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("http")]
    [InlineData("io")]
    [InlineData("timeout")]
    [InlineData("cancelled")]
    [InlineData("socket")]
    public async Task Whatever_a_transport_throws_comes_back_as_a_failed_send_and_never_escapes(string kind)
    {
        using var factory = new SilexGisApiFactory(postgres.ConnectionString);
        await using var scope = factory.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IMessageDispatcher>();
        const string Address = "throws@example.test";
        factory.Messages.ThrowSendsTo = (Address, () => kind switch
        {
            "http" => new HttpRequestException("No route to the relay."),
            "io" => new IOException("The connection was reset."),
            "timeout" => new TimeoutException("The relay did not answer."),
            // A cancellation nobody asked for, the shape a client's own timeout takes.
            "cancelled" => new TaskCanceledException("The request was canceled."),
            _ => new System.Net.Sockets.SocketException(),
        });

        var result = await dispatcher.SendAsync(
            MessageTemplateCatalog.NotifyPermissionGranted,
            Address,
            "en",
            new Dictionary<string, string> { ["actorName"] = "Someone", ["objectName"] = "Something" });

        result.Sent.ShouldBeFalse();
        result.Permanent.ShouldBeFalse();
        result.Error.ShouldNotBeNullOrWhiteSpace();
        factory.Messages.Messages.ShouldBeEmpty();
    }

    private SilexGisApiFactory Host(
        HttpMessageHandler gateway, string? bodyTemplate = null, string? contentType = null) =>
        new(
            postgres.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Sms:Enabled"] = "true",
                ["Sms:Url"] = "http://sms-gateway.invalid/send",
                // The shortest the sender allows; a second is long enough to be sure the timeout,
                // not anything else, ended the wait.
                ["Sms:TimeoutSeconds"] = "1",
                ["Sms:BodyTemplate"] = bodyTemplate ?? new SmsSettings().BodyTemplate,
                ["Sms:ContentType"] = contentType ?? new SmsSettings().ContentType,
            },
            services =>
            {
                services.AddScoped<HttpSmsSender>();
                services.AddScoped<ISmsSender>(sp => sp.GetRequiredService<HttpSmsSender>());
                services.AddScoped<ISmsDelivery>(sp => sp.GetRequiredService<HttpSmsSender>());
                services.AddHttpClient(HttpSmsSender.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => gateway);
            });

    private static SilexGisUser Person() => new()
    {
        Email = "timeout@example.test",
        UserName = "timeout@example.test",
        DisplayName = "Person",
        PhoneNumber = "+40700000000",
        Locale = "en",
    };

    /// <summary>Answers every request with <see cref="Answer"/>, keeping each body it was sent.</summary>
    private sealed class RecordingGateway : HttpMessageHandler
    {
        private readonly List<string> bodies = [];

        public HttpStatusCode Answer { get; init; } = HttpStatusCode.OK;

        public IReadOnlyList<string> Bodies
        {
            get
            {
                lock (bodies)
                {
                    return [.. bodies];
                }
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (bodies)
            {
                bodies.Add(body);
            }

            return new HttpResponseMessage(Answer) { Content = new StringContent("gateway says no") };
        }
    }

    /// <summary>Accepts the request and never answers it.</summary>
    private sealed class SilentGateway : HttpMessageHandler
    {
        private int asked;

        public int Asked => Volatile.Read(ref asked);

        public Action? WhenAsked { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref asked);
            WhenAsked?.Invoke();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("Unreachable: the wait only ends by cancellation.");
        }
    }
}
