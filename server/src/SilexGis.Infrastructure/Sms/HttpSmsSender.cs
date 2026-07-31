// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Settings;

namespace SilexGis.Infrastructure.Sms;

/// <summary>
/// Sends text messages by making the HTTP request the operator described, falling back to the log
/// when no gateway is configured.
/// </summary>
/// <remarks>
/// <para>
/// Every SMS gateway worth using — the global ones and the regional ones a Romanian caving club is
/// more likely to have an account with — exposes "POST these fields and we send a message". Rather
/// than bind the project to one vendor's SDK, the request itself is the configuration: a URL, a
/// method, headers, and a body with <c>{to}</c>, <c>{text}</c> and <c>{from}</c> in it.
/// </para>
/// <para>
/// The substitution is where this could go wrong, so it is not string concatenation: each value is
/// escaped for the body's content type before it goes in. A message containing an ampersand would
/// otherwise split a form body into extra fields, and one containing a quote would break a JSON
/// body outright — with the text under partial control of whoever chose a display name.
/// </para>
/// </remarks>
public sealed partial class HttpSmsSender(
    IHttpClientFactory httpClientFactory,
    IAppSettingsService settings,
    ILogger<HttpSmsSender> logger) : ISmsSender, ISmsDelivery
{
    /// <summary>Name of the typed client; registered in <c>DependencyInjection</c>.</summary>
    public const string HttpClientName = "sms-gateway";

    public async ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default) =>
        (await settings.GetSmsAsync(ct)).IsUsable;

    public async Task SendAsync(string to, string text, CancellationToken ct = default)
    {
        var sms = await settings.GetSmsAsync(ct);
        if (!sms.IsUsable)
        {
            LogUnsent(logger, to, text);
            return;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["to"] = to,
            ["text"] = text,
            ["from"] = sms.From ?? string.Empty,
        };

        using var request = new HttpRequestMessage(
            new HttpMethod(string.IsNullOrWhiteSpace(sms.Method) ? "POST" : sms.Method.ToUpperInvariant()),
            // The URL is escaped as a URL regardless of the body's content type: a number goes in
            // the path or query of plenty of gateways.
            Substitute(sms.Url, values, "application/x-www-form-urlencoded"));

        if (!string.IsNullOrWhiteSpace(sms.AuthHeader))
        {
            request.Headers.TryAddWithoutValidation("Authorization", sms.AuthHeader);
        }

        foreach (var (name, value) in sms.Headers)
        {
            request.Headers.TryAddWithoutValidation(name, Substitute(value, values, "text/plain"));
        }

        if (request.Method != HttpMethod.Get && !string.IsNullOrWhiteSpace(sms.BodyTemplate))
        {
            var body = Substitute(sms.BodyTemplate, values, sms.ContentType);
            request.Content = new StringContent(body, Encoding.UTF8);
            request.Content.Headers.ContentType =
                MediaTypeHeaderValue.Parse(string.IsNullOrWhiteSpace(sms.ContentType)
                    ? "application/x-www-form-urlencoded"
                    : sms.ContentType);
        }

        var client = httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(sms.TimeoutSeconds, 1, 120));

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            // Gateways put the reason in the body; carrying a bounded slice of it makes the
            // administrator's test-send able to say what was actually wrong.
            var detail = await ReadTruncatedAsync(response, ct);
            throw new InvalidOperationException(
                $"SMS gateway returned {(int)response.StatusCode} {response.ReasonPhrase}. {detail}".TrimEnd());
        }

        logger.LogDebug("Sent SMS to {Recipient}", to);
    }

    /// <summary>
    /// Replaces <c>{to}</c>, <c>{text}</c> and <c>{from}</c>, escaping each value for the body
    /// format so no value can alter the request's structure.
    /// </summary>
    internal static string Substitute(
        string template, IReadOnlyDictionary<string, string> values, string? contentType)
    {
        var escaped = values.ToDictionary(
            pair => pair.Key,
            pair => Escape(pair.Value, contentType),
            StringComparer.Ordinal);
        return MessageTemplateRenderer.Render(template, escaped);
    }

    private static string Escape(string value, string? contentType)
    {
        var type = contentType?.Split(';')[0].Trim().ToLowerInvariant();
        return type switch
        {
            "application/json" or "text/json" => JsonEscape(value),
            "text/plain" => value,
            // Form encoding is the default because it is what the overwhelming majority of
            // gateways accept, and it is the safest thing to do with an unrecognised type.
            _ => Uri.EscapeDataString(value),
        };
    }

    /// <summary>
    /// Produces the inside of a JSON string — the template supplies the surrounding quotes, so
    /// only the content is escaped.
    /// </summary>
    private static string JsonEscape(string value)
    {
        var encoded = JsonSerializer.Serialize(value, JsonOptions);
        return encoded[1..^1];
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static async Task<string> ReadTruncatedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            return body.Length <= 300 ? body : body[..300] + "…";
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            return string.Empty;
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "SMS gateway not configured — message not sent. To: {To} | Text: {Text}")]
    private static partial void LogUnsent(ILogger logger, string to, string text);
}
