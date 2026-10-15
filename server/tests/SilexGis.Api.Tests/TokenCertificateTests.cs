// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using SilexGis.Api.Auth;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;

namespace SilexGis.Api.Tests;

/// <summary>
/// The token certificates live in the keys directory: two instances over one directory accept each
/// other's tokens, and an instance over another directory does not — which is what tells the
/// certificate apart from one kept per machine, which every host in this process would share.
/// </summary>
public sealed class TokenCertificateTests(PostgresFixture postgres) : IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory first = new(postgres.ConnectionString);

    [Fact]
    public async Task A_token_from_one_instance_is_accepted_by_another_sharing_the_keys_directory_only()
    {
        var email = $"cert-{Guid.NewGuid():N}@t.local";
        _ = await AuthHelper.CreateUserAsync(first, GlobalRoles.Editor, email);
        var token = (await AuthHelper.BearerClientAsync(first, email)).DefaultRequestHeaders.Authorization!;

        // A second instance of the same installation: same database, same keys directory.
        using var second = new SilexGisApiFactory(postgres.ConnectionString, ownHost: true);
        using var onSecond = second.CreateClient();
        onSecond.DefaultRequestHeaders.Authorization = token;
        (await onSecond.GetAsync("/api/v1/me/external-logins/")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // An instance with a keys directory of its own made its own certificate, and the token
        // signed by the other one means nothing to it.
        var elsewhere = Path.Combine(TestScratch.Root, $"keys-elsewhere-{Guid.NewGuid():N}");
        using var stranger = new SilexGisApiFactory(
            postgres.ConnectionString, new Dictionary<string, string?> { ["Keys:Path"] = elsewhere }, ownHost: true);
        using var onStranger = stranger.CreateClient();
        onStranger.DefaultRequestHeaders.Authorization = token;
        (await onStranger.GetAsync("/api/v1/me/external-logins/")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        File.Exists(Path.Combine(elsewhere, TokenCertificates.FileName(TokenCertificatePurpose.Signing))).ShouldBeTrue();
    }

    public void Dispose() => first.Dispose();
}

/// <summary>
/// Making, keeping and renewing the certificates, against a directory of the test's own and a
/// clock it sets.
/// </summary>
public sealed class TokenCertificateStoreTests : IDisposable
{
    private readonly string keys = Path.Combine(TestScratch.Root, $"token-certificates-{Guid.NewGuid():N}");
    private readonly DateTimeOffset now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(TokenCertificatePurpose.Signing, X509KeyUsageFlags.DigitalSignature)]
    [InlineData(TokenCertificatePurpose.Encryption, X509KeyUsageFlags.KeyEncipherment)]
    public void A_fresh_directory_gets_a_certificate_once_and_later_starts_reuse_it(
        TokenCertificatePurpose purpose, X509KeyUsageFlags usage)
    {
        var made = Load(purpose, now);
        var file = Path.Combine(keys, TokenCertificates.FileName(purpose));

        made.Count.ShouldBe(1);
        made[0].HasPrivateKey.ShouldBeTrue();
        made[0].Extensions.OfType<X509KeyUsageExtension>().Single().KeyUsages.ShouldBe(usage);
        made[0].NotAfter.ToUniversalTime().ShouldBe(now.Add(TokenCertificates.Validity).UtcDateTime, TimeSpan.FromSeconds(1));
        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(file).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        var written = File.GetLastWriteTimeUtc(file);
        var again = Load(purpose, now.AddDays(200));

        again.Single().Thumbprint.ShouldBe(made[0].Thumbprint);
        File.GetLastWriteTimeUtc(file).ShouldBe(written);
    }

    [Fact]
    public void Near_its_end_a_certificate_is_succeeded_and_kept_behind_the_successor()
    {
        var original = Load(TokenCertificatePurpose.Signing, now).Single();

        // Outside the renewal window: nothing changes.
        var early = now.Add(TokenCertificates.Validity - TokenCertificates.RenewalWindow - TimeSpan.FromDays(1));
        Load(TokenCertificatePurpose.Signing, early).Single().Thumbprint.ShouldBe(original.Thumbprint);

        // Inside it: a successor signs, the original stays for what it already covered.
        var late = now.Add(TokenCertificates.Validity - TimeSpan.FromDays(10));
        var renewed = Load(TokenCertificatePurpose.Signing, late);
        renewed.Count.ShouldBe(2);
        renewed[0].Thumbprint.ShouldNotBe(original.Thumbprint);
        renewed[0].NotAfter.ToUniversalTime().ShouldBe(late.Add(TokenCertificates.Validity).UtcDateTime, TimeSpan.FromSeconds(1));
        renewed[1].Thumbprint.ShouldBe(original.Thumbprint);

        // And the renewal itself happened once.
        var after = Load(TokenCertificatePurpose.Signing, late.AddDays(1));
        after.Select(c => c.Thumbprint).ShouldBe(renewed.Select(c => c.Thumbprint));
    }

    [Fact]
    public void A_configured_file_is_used_as_it_is_and_nothing_is_made()
    {
        var supplied = Path.Combine(TestScratch.Root, $"supplied-{Guid.NewGuid():N}.pfx");
        var source = Load(TokenCertificatePurpose.Signing, now).Single();
        File.WriteAllBytes(supplied, source.Export(X509ContentType.Pkcs12, "secret"));
        Directory.Delete(keys, recursive: true);

        try
        {
            var used = TokenCertificates.Load(
                TokenCertificatePurpose.Signing, keys, supplied, "secret", now, NullLogger.Instance);

            used.Single().Thumbprint.ShouldBe(source.Thumbprint);
            Directory.Exists(keys).ShouldBeFalse();
        }
        finally
        {
            File.Delete(supplied);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(keys))
        {
            Directory.Delete(keys, recursive: true);
        }
    }

    private IReadOnlyList<X509Certificate2> Load(TokenCertificatePurpose purpose, DateTimeOffset at) =>
        TokenCertificates.Load(purpose, keys, configuredPath: null, configuredPassword: null, at, NullLogger.Instance);
}
