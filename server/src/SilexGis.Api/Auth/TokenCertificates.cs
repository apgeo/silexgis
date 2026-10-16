// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;

namespace SilexGis.Api.Auth;

/// <summary>What a token certificate is for: signing the tokens, or encrypting the ones only the server reads.</summary>
public enum TokenCertificatePurpose
{
    Signing,
    Encryption,
}

/// <summary>
/// The certificates the sign-in server signs and encrypts its tokens with, kept where they survive
/// the process: either files the operator supplies, or certificates this application makes once
/// and keeps in the keys directory beside the data-protection key ring.
/// </summary>
/// <remarks>
/// <para>
/// Certificates made per process — or per machine profile, which inside a container is the same
/// thing — sign everybody out whenever the container is recreated, because every token in
/// circulation was signed by a key that no longer exists; and a second instance behind the same
/// address cannot validate the first one's tokens at all. Keeping them in the directory that
/// already has to persist (it holds the key ring cookies depend on) fixes both with nothing new
/// for an operator to mount.
/// </para>
/// <para>
/// A made certificate is valid for five years. When fewer than thirty days remain at start-up a
/// successor is made and becomes the one that signs, and the outgoing certificate is kept as the
/// single previous one: tokens it signed still validate and refresh tokens it encrypted still
/// decrypt, and every one of those has expired long before the next renewal replaces it. An
/// operator's own certificate cannot be renewed here, so for one of those the same thirty days
/// bring a warning in the log at every start instead.
/// </para>
/// <para>
/// The files are written readable by their owner only, the way the key ring's own files should
/// be, and carry no password: the protection is the file's permissions and the volume it lives
/// on, exactly as for the key ring. Several instances starting together over one directory take
/// turns through a lock file, so they cannot each make a different certificate.
/// </para>
/// </remarks>
public static class TokenCertificates
{
    public static readonly TimeSpan Validity = TimeSpan.FromDays(5 * 365);

    /// <summary>How long before expiry a made certificate is renewed and an operator's is warned about.</summary>
    public static readonly TimeSpan RenewalWindow = TimeSpan.FromDays(30);

    private const string LockFileName = "token-certificates.lock";

    public static string FileName(TokenCertificatePurpose purpose) =>
        purpose == TokenCertificatePurpose.Signing ? "token-signing.pfx" : "token-encryption.pfx";

    public static string PreviousFileName(TokenCertificatePurpose purpose) =>
        purpose == TokenCertificatePurpose.Signing ? "token-signing.previous.pfx" : "token-encryption.previous.pfx";

    /// <summary>
    /// The certificates for one purpose, the one to use first. A configured file is used as it is;
    /// otherwise the kept certificate is loaded, made on first use, or renewed when near its end.
    /// </summary>
    public static IReadOnlyList<X509Certificate2> Load(
        TokenCertificatePurpose purpose,
        string keysDirectory,
        string? configuredPath,
        string? configuredPassword,
        DateTimeOffset now,
        ILogger logger)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var supplied = Read(configuredPath, configuredPassword);
            WarnIfEnding(supplied, purpose, configuredPath, now, logger);
            return [supplied];
        }

        Directory.CreateDirectory(keysDirectory);
        using var turn = TakeTurn(Path.Combine(keysDirectory, LockFileName));

        var currentPath = Path.Combine(keysDirectory, FileName(purpose));
        var previousPath = Path.Combine(keysDirectory, PreviousFileName(purpose));

        if (!File.Exists(currentPath))
        {
            Write(currentPath, Make(purpose, now));
            logger.LogInformation(
                "Made a new token {Purpose} certificate in {Path}; it is kept and reused from now on.",
                purpose, currentPath);
        }

        var current = Read(currentPath, password: null);
        if (current.NotAfter.ToUniversalTime() - now.UtcDateTime < RenewalWindow)
        {
            var successor = Make(purpose, now);
            var staged = currentPath + ".new";
            Write(staged, successor);
            File.Move(currentPath, previousPath, overwrite: true);
            File.Move(staged, currentPath, overwrite: true);
            logger.LogWarning(
                "The token {Purpose} certificate expires {Expiry:u}; a successor valid until {Next:u} now takes over, "
                + "and the outgoing one is kept so the tokens it covered stay usable.",
                purpose, current.NotAfter.ToUniversalTime(), successor.NotAfter.ToUniversalTime());
            current = Read(currentPath, password: null);
        }

        return File.Exists(previousPath) ? [current, Read(previousPath, password: null)] : [current];
    }

    /// <summary>Registers the certificates with the sign-in server, as its own certificate helpers would.</summary>
    public static void Register(
        OpenIddictServerOptions options,
        IReadOnlyList<X509Certificate2> signing,
        IReadOnlyList<X509Certificate2> encryption)
    {
        foreach (var certificate in signing)
        {
            Require(certificate, X509KeyUsageFlags.DigitalSignature, TokenCertificatePurpose.Signing);
            options.SigningCredentials.Add(
                new SigningCredentials(new X509SecurityKey(certificate), SecurityAlgorithms.RsaSha256));
        }

        foreach (var certificate in encryption)
        {
            Require(certificate, X509KeyUsageFlags.KeyEncipherment, TokenCertificatePurpose.Encryption);
            options.EncryptionCredentials.Add(new EncryptingCredentials(
                new X509SecurityKey(certificate), SecurityAlgorithms.RsaOAEP, SecurityAlgorithms.Aes256CbcHmacSha512));
        }
    }

    private static void Require(X509Certificate2 certificate, X509KeyUsageFlags usage, TokenCertificatePurpose purpose)
    {
        if (!certificate.HasPrivateKey)
        {
            throw new InvalidOperationException(
                $"The token {purpose} certificate {certificate.Subject} has no private key.");
        }

        // A certificate that states its uses must state this one; one that states none may be used for anything.
        var usages = certificate.Extensions.OfType<X509KeyUsageExtension>().ToList();
        if (usages.Count > 0 && !usages.Any(u => u.KeyUsages.HasFlag(usage)))
        {
            throw new InvalidOperationException(
                $"The token {purpose} certificate {certificate.Subject} does not allow {usage}.");
        }
    }

    private static X509Certificate2 Make(TokenCertificatePurpose purpose, DateTimeOffset now)
    {
        using var key = RSA.Create(3072);
        var request = new CertificateRequest(
            new X500DistinguishedName($"CN=SilexGIS token {purpose.ToString().ToLowerInvariant()}"),
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            purpose == TokenCertificatePurpose.Signing
                ? X509KeyUsageFlags.DigitalSignature
                : X509KeyUsageFlags.KeyEncipherment,
            critical: true));

        // Valid from a little before now, so a clock a few minutes behind on a second instance
        // does not see a certificate from the future.
        return request.CreateSelfSigned(now.AddHours(-1), now.Add(Validity));
    }

    private static void Write(string path, X509Certificate2 certificate)
    {
        var bytes = certificate.Export(X509ContentType.Pkcs12);
        var staged = path + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            // Created owner-only rather than narrowed afterwards, so there is no moment at which
            // the private key is readable by anyone else.
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using (var stream = new FileStream(staged, options))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        File.Move(staged, path, overwrite: true);
    }

    private static X509Certificate2 Read(string path, string? password)
    {
        // Ephemeral where the platform allows it, so loading a key never leaves a copy of it in a
        // user profile's key store.
        var flags = OperatingSystem.IsMacOS() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet;
        return X509CertificateLoader.LoadPkcs12FromFile(path, password, flags);
    }

    private static void WarnIfEnding(
        X509Certificate2 certificate, TokenCertificatePurpose purpose, string path, DateTimeOffset now, ILogger logger)
    {
        var left = certificate.NotAfter.ToUniversalTime() - now.UtcDateTime;
        if (left < TimeSpan.Zero)
        {
            logger.LogError(
                "The configured token {Purpose} certificate {Path} expired {Expiry:u}. Replace it; until then sign-in cannot work.",
                purpose, path, certificate.NotAfter.ToUniversalTime());
        }
        else if (left < RenewalWindow)
        {
            logger.LogWarning(
                "The configured token {Purpose} certificate {Path} expires {Expiry:u}. Replace it before then; "
                + "tokens the old one covered stop working when it is replaced, so everybody signs in again once.",
                purpose, path, certificate.NotAfter.ToUniversalTime());
        }
    }

    /// <summary>Waits, briefly, for any other instance starting over the same directory to finish.</summary>
    private static FileStream TakeTurn(string lockPath)
    {
        // Measured on a stopwatch, not the wall clock: a wait has a length, not a date, and the
        // wall clock can be moved under it.
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (waited.Elapsed < TimeSpan.FromSeconds(30))
            {
                Thread.Sleep(100);
            }
        }
    }
}

/// <summary>Hands the kept or configured certificates to the sign-in server when its options are built.</summary>
internal sealed class TokenCertificateSetup(
    IOptions<AuthOptions> authOptions,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<TokenCertificateSetup> logger) : IConfigureOptions<OpenIddictServerOptions>
{
    public void Configure(OpenIddictServerOptions options)
    {
        var auth = authOptions.Value;
        var keys = Common.KeysDirectory.Resolve(configuration);
        var now = time.GetUtcNow();
        TokenCertificates.Register(
            options,
            TokenCertificates.Load(
                TokenCertificatePurpose.Signing, keys, auth.SigningCertificatePath, auth.SigningCertificatePassword, now, logger),
            TokenCertificates.Load(
                TokenCertificatePurpose.Encryption, keys, auth.EncryptionCertificatePath, auth.EncryptionCertificatePassword, now, logger));
    }
}
