// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Compression;
using System.Text;
using Shouldly;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Tests;

/// <summary>
/// The survey archive reader on archives the test builds in memory, and on one genuine TopoDroid
/// archive of an invented survey. Every survey here is invented.
/// </summary>
public sealed class TopoDroidArchiveReaderTests
{
    private const long Cap = 10 * 1024 * 1024;

    private const string Manifest = "6.5.34 605034\n60\ninventata\n2026.10.25\n";

    private const string Dump =
        "INSERT into surveys values( 1, \"inventata\", \"2026.10.20\", \"Test A.\", 0.0000, \"\", \"0\", 1, 0, 0, 0 0 );\n"
        + "INSERT into shots values( 1, 1, \"0\", \"1\", 5.000 );\n"
        + "INSERT into fixeds values( 1, 1, \"0\", 24.0000000, 46.0000000, 541.33, 500.00, \"\", 0, 2, \"\" );\n";

    [Fact]
    public async Task A_genuine_archive_of_an_invented_survey_is_read_from_its_manifest_and_dump()
    {
        await using var archive = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "topodroid", "pestera_inventata.zip"));

        var reading = await TopoDroidArchiveReader.ReadAsync(archive, Cap, CancellationToken.None);

        reading.EntryCount.ShouldBe(5);
        reading.Manifest.ShouldBe(new TopoDroidManifest("6.5.34", 605034, 60, "pestera_inventata"));
        reading.Survey.Name.ShouldBe("pestera_inventata");
        reading.Survey.Date.ShouldBe(new DateOnly(2026, 10, 25));
        reading.Survey.Team.ShouldBe("Test A.; Test B.");
        reading.Survey.Declination.ShouldBe(0);
        reading.Survey.FixedRowCount.ShouldBe(1);
        reading.Survey.FixedPoints.ShouldHaveSingleItem().Station.ShouldBe("0");
    }

    [Fact]
    public async Task An_archive_built_the_way_topodroid_builds_it_is_read()
    {
        var reading = await ReadAsync(Zip(("manifest", Manifest), ("survey.sql", Dump), ("inventata.txt", "note"), ("photo/1.jpg", "x")));

        reading.Manifest.SurveyName.ShouldBe("inventata");
        reading.Survey.Date.ShouldBe(new DateOnly(2026, 10, 20));
        reading.Survey.FixedPoints.Count.ShouldBe(1);
    }

    [Fact]
    public async Task An_archive_without_a_manifest_is_invalid()
    {
        await ShouldRefuseAsync(Zip(("survey.sql", Dump)), SurveyPipelineFailures.ArchiveInvalid, "no manifest");
    }

    [Fact]
    public async Task An_archive_whose_manifest_is_not_its_first_entry_is_invalid()
    {
        await ShouldRefuseAsync(Zip(("survey.sql", Dump), ("manifest", Manifest)), SurveyPipelineFailures.ArchiveInvalid, "first entry");
    }

    [Fact]
    public async Task An_archive_without_a_dump_or_a_survey_row_is_invalid()
    {
        await ShouldRefuseAsync(Zip(("manifest", Manifest), ("note.txt", "x")), SurveyPipelineFailures.ArchiveInvalid, "no survey dump");
        await ShouldRefuseAsync(
            Zip(("manifest", Manifest), ("survey.sql", "INSERT into shots values( 1 );\n")),
            SurveyPipelineFailures.ArchiveInvalid,
            "holds no survey");
    }

    [Fact]
    public async Task An_archive_from_a_database_older_than_topodroid_still_reads_is_invalid()
    {
        await ShouldRefuseAsync(
            Zip(("manifest", "5.0.0 500000\n14\ninventata\n"), ("survey.sql", Dump)),
            SurveyPipelineFailures.ArchiveInvalid,
            "older than version");
    }

    [Theory]
    [InlineData("../outside.jpg")]
    [InlineData("photo/../../outside.jpg")]
    [InlineData("/etc/outside")]
    [InlineData("photo\\..\\..\\outside.jpg")]
    public async Task An_archive_naming_an_entry_outside_itself_is_invalid(string entry)
    {
        await ShouldRefuseAsync(
            Zip(("manifest", Manifest), ("survey.sql", Dump), (entry, "x")),
            SurveyPipelineFailures.ArchiveInvalid,
            "outside itself");
    }

    [Fact]
    public async Task An_archive_with_more_entries_than_allowed_is_refused_before_anything_is_read()
    {
        var entries = new List<(string, string)> { ("manifest", Manifest), ("survey.sql", Dump) };
        entries.AddRange(Enumerable.Range(0, 5).Select(i => ($"photo/{i}.jpg", "x")));

        var refusal = await Should.ThrowAsync<SurveyPipelineException>(
            TopoDroidArchiveReader.ReadAsync(new MemoryStream(Zip([.. entries])), Cap, CancellationToken.None, maxEntries: 5));
        refusal.Code.ShouldBe(SurveyPipelineFailures.ArchiveTooLarge);
    }

    [Fact]
    public async Task A_decompression_bomb_is_refused_before_it_is_inflated()
    {
        // Eight megabytes of one repeated byte compress to a few kilobytes: far past any ratio a
        // survey, a sketch or a photo has.
        var bomb = new string('0', 8 * 1024 * 1024);

        await ShouldRefuseAsync(
            Zip(("manifest", Manifest), ("survey.sql", Dump), ("photo/big.jpg", bomb)),
            SurveyPipelineFailures.ArchiveTooLarge,
            "unpacks to more");
    }

    [Fact]
    public async Task An_archive_declaring_more_than_the_cap_is_refused()
    {
        var photo = RandomText(64 * 1024);

        var refusal = await Should.ThrowAsync<SurveyPipelineException>(TopoDroidArchiveReader.ReadAsync(
            new MemoryStream(Zip(("manifest", Manifest), ("survey.sql", Dump), ("photo/1.jpg", photo))),
            maxUnpackedBytes: 32 * 1024,
            CancellationToken.None));
        refusal.Code.ShouldBe(SurveyPipelineFailures.ArchiveTooLarge);
    }

    [Fact]
    public async Task Bytes_that_are_not_a_zip_archive_are_invalid()
    {
        await ShouldRefuseAsync(Encoding.UTF8.GetBytes("survey inventata\nendsurvey\n"), SurveyPipelineFailures.ArchiveInvalid, "not a ZIP");
    }

    private static Task<TopoDroidArchiveReading> ReadAsync(byte[] archive) =>
        TopoDroidArchiveReader.ReadAsync(new MemoryStream(archive), Cap, CancellationToken.None);

    private static async Task ShouldRefuseAsync(byte[] archive, string code, string messagePart)
    {
        var refusal = await Should.ThrowAsync<SurveyPipelineException>(ReadAsync(archive));
        refusal.Code.ShouldBe(code);
        refusal.Message.ShouldContain(messagePart);
    }

    /// <summary>A ZIP archive with these entries, in this order.</summary>
    internal static byte[] Zip(params (string Name, string Text)[] entries)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
                writer.Write(text);
            }
        }

        return memory.ToArray();
    }

    /// <summary>Text that does not compress, so an entry's declared and compressed sizes stay close.</summary>
    private static string RandomText(int length)
    {
        var random = new Random(7);
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = (char)random.Next('!', '~');
        }

        return new string(chars);
    }
}
