// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Shouldly;
using SilexGis.Api.Features.Sync;
using SilexGis.Api.Tests.Support;

namespace SilexGis.Api.Tests;

/// <summary>
/// Describes the recorded contract directory, so that a copy of it living in another repository
/// can tell whether it is current.
/// </summary>
/// <remarks>
/// <para>
/// The byte comparison that guards the recordings only looks at files a test names. It is
/// therefore one-directional: a file nobody asserts about — an exchange that was renamed, a
/// directory left behind by a case that was deleted, a half-copied tree in another checkout — is
/// invisible to it. The manifest is the other direction. It is generated from a walk of the
/// directory as it stands, so a file that is present and unclaimed shows up as an entry no test
/// put there, and a file that has gone shows up as an entry that vanished.
/// </para>
/// <para>
/// The reason it exists at all is a repository boundary. The application on the other side of
/// this protocol lives in its own repository, with its own branches and its own build; the two
/// share no continuous integration and no way to notice that one has moved. A digest per file
/// plus one roll-up over all of them lets that side answer "is my copy the one this server
/// speaks?" with a single comparison, and, when the answer is no, name the exchanges that
/// differ rather than re-reading the whole tree.
/// </para>
/// <para>
/// It deliberately carries no generation time and no commit identifier. This file is itself held
/// byte for byte, so a stamp that moved on every run would either fail the comparison constantly
/// or have to be excluded from it, and neither is a check. What a reader on the other side needs
/// is not when it was made but whether it matches, which the digests answer exactly.
/// </para>
/// <para>
/// Two files in the directory are outside the manifest and are named inside it as such. The
/// changelog is one, because its whole purpose is to be written when the recordings change:
/// hashing it would mean every entry describing a change also changed the manifest and needed a
/// second pass to describe itself. The manifest is the other, because a file cannot state its
/// own digest.
/// </para>
/// <para>
/// <b>It is taken in a pass of its own, and this class refuses to take it in a recording run.</b>
/// A manifest is a walk of the directory, and a recording run is a set of tests rewriting the
/// files in that directory — from another xunit collection, in parallel, each write truncating
/// its target before it fills it again. A walk crossing that window hashes a file mid-write or
/// misses one that had not been created yet, and the committed manifest then disagrees with the
/// committed recordings in a way that only shows up as a red test on somebody else's full run.
/// Putting this class in the recording tests' collection would not fix it either: a collection
/// serialises its tests but promises nothing about their order, so the walk could still run
/// first. So the recording procedure is two passes, and the second one needs neither a database
/// nor the recording switch:
/// </para>
/// <code>
/// SILEXGIS_CONTRACT_RECORD=1   dotnet test                                    # rewrite the exchanges
/// SILEXGIS_CONTRACT_MANIFEST=1 dotnet test --filter FullyQualifiedName~ContractManifestTests
/// </code>
/// <para>
/// The second pass deliberately does not set the recording switch, so it stays harmless if it is
/// ever run unfiltered: nothing else in the suite writes anything in that mode.
/// </para>
/// </remarks>
public sealed class ContractManifestTests
{
    /// <summary>
    /// Files that are about the recordings rather than part of them. Held here rather than
    /// inferred from an extension: the prose in the directory is part of the package and is
    /// covered, and only these two have a reason not to be.
    /// </summary>
    private static readonly string[] Excluded = ["CHANGELOG.md", "manifest.json"];

    /// <summary>Set to <c>1</c> in the second pass, the one that rewrites the manifest.</summary>
    private const string ManifestVariable = "SILEXGIS_CONTRACT_MANIFEST";

    private static bool TakingTheManifest =>
        Environment.GetEnvironmentVariable(ManifestVariable) is "1" or "true";

    /// <summary>
    /// The exchanges that come before a download — signing in, and creating the selection. They
    /// are named in the manifest so that a copy of the directory holding none of them is judged
    /// complete rather than truncated, and so that a later recording lands in the slot it was
    /// meant for instead of renumbering everything after it.
    /// </summary>
    private static readonly string[] Reserved =
    [
        "01-login",
        "02-login-mfa-required",
        "03-authorize-code",
        "04-token-exchange",
        "05-token-refresh",
        "06-sync-set-create",
    ];

    [Fact]
    public async Task The_manifest_describes_every_recorded_file_and_nothing_else()
    {
        // A recording run is rewriting the very files this walk would read, from another
        // collection and in parallel. Refusing loudly is the point: a manifest taken across that
        // window is wrong in a way nothing here would notice, and the cross-repository staleness
        // check it exists for would then be answering from a file that describes nothing.
        (ContractFixture.Recording && !TakingTheManifest).ShouldBeFalse(
            $"The manifest is not taken during a run with {ContractFixture.RecordVariable} set, "
            + "because that run is rewriting the files this test walks. Record first, then take "
            + $"the manifest in a second pass:  {ManifestVariable}=1 dotnet test "
            + "--filter FullyQualifiedName~ContractManifestTests");

        var root = ContractFixture.ContractRoot();
        Directory.Exists(root).ShouldBeTrue($"No contract directory at {root}.");

        var files = Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Relative(root, path))
            .Where(relative => !Excluded.Contains(relative, StringComparer.Ordinal))
            .OrderBy(relative => relative, StringComparer.Ordinal)
            .ToArray();

        files.ShouldNotBeEmpty();

        var digests = files.ToDictionary(
            relative => relative,
            relative => Sha256(File.ReadAllBytes(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)))),
            StringComparer.Ordinal);

        await ContractFixture.CompareOrWriteAsync(
            Path.Combine(root, "manifest.json"),
            Render(root, files, digests),
            TakingTheManifest,
            $"re-run this test alone with {ManifestVariable}=1 — the manifest is a walk of the "
            + "recorded directory and is deliberately taken in a pass of its own, not in the pass "
            + "that rewrites the recordings");
    }

    /// <summary>
    /// The whole manifest as it lands on disk. Rendered by hand rather than serialised from a
    /// type so that the ordering is the one a reader gets — the summary first, the per-file
    /// digests last — and so that the file diffs a line at a time when one exchange changes.
    /// </summary>
    private static string Render(
        string root, IReadOnlyList<string> files, IReadOnlyDictionary<string, string> digests)
    {
        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
            buffer,
            new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteString("recordings", "speleoloc-sync/v1");
            writer.WriteNumber("contractVersion", SyncEndpoints.ContractVersion);

            writer.WriteStartArray("features");
            foreach (var feature in SyncEndpoints.ServedFeatures)
            {
                writer.WriteStringValue(feature);
            }

            writer.WriteEndArray();

            writer.WriteStartArray("reserved");
            foreach (var slot in Reserved)
            {
                writer.WriteStringValue(slot);
            }

            writer.WriteEndArray();

            writer.WriteStartArray("notCovered");
            foreach (var excluded in Excluded)
            {
                writer.WriteStringValue(excluded);
            }

            writer.WriteEndArray();

            writer.WriteNumber("fileCount", files.Count);
            writer.WriteString("digest", RollUp(files, digests));

            writer.WriteStartObject("files");
            foreach (var relative in files)
            {
                writer.WriteStartObject(relative);
                writer.WriteNumber(
                    "bytes",
                    new FileInfo(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))).Length);
                writer.WriteString("sha256", digests[relative]);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray()).ReplaceLineEndings("\n") + "\n";
    }

    /// <summary>
    /// One value answering "same or not" before anybody diffs twenty-odd entries. Over the paths
    /// as well as the digests, so a file that was renamed and not otherwise touched moves it.
    /// </summary>
    private static string RollUp(
        IReadOnlyList<string> files, IReadOnlyDictionary<string, string> digests)
    {
        var lines = new StringBuilder();
        foreach (var relative in files)
        {
            lines.Append(relative).Append(' ').Append(digests[relative]).Append('\n');
        }

        return Sha256(Encoding.UTF8.GetBytes(lines.ToString()));
    }

    /// <summary>
    /// Hashed as bytes, never as decoded text. The repository normalises line endings on the way
    /// in and out, so a manifest generated from text read on a platform that checks these files
    /// out with carriage returns would disagree with one generated on a platform that does not,
    /// and the disagreement would look like a contract change.
    /// </summary>
    private static string Sha256(byte[] content) =>
        Convert.ToHexStringLower(SHA256.HashData(content));

    /// <summary>
    /// A path as the manifest states it: relative to the directory, with forward slashes on every
    /// platform. The separator is the one thing about these paths that the repository's own line
    /// normalisation cannot fix, so it is fixed here.
    /// </summary>
    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
}
