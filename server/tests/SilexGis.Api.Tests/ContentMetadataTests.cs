// SPDX-License-Identifier: AGPL-3.0-or-later
using ImageMagick;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Files;

namespace SilexGis.Api.Tests;

/// <summary>
/// What a file states about itself, read from the file rather than from the upload. These
/// land in columns because document lists filter and order on them, so the reader has to be
/// exact where the format is exact and silent where it is not — a wrong duration is worse
/// than no duration.
/// </summary>
public sealed class ContentMetadataTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), $"silexgis-test-facts-{Guid.NewGuid():N}");

    private readonly ContentMetadataReader reader = new();

    public ContentMetadataTests() => Directory.CreateDirectory(root);

    [Fact]
    public async Task A_photo_states_its_photographer_its_software_and_when_it_was_taken()
    {
        using var image = new MagickImage(MagickColors.SteelBlue, 32, 32);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Artist, "A. Popescu");
        exif.SetValue(ExifTag.Software, "SilexCam 2.1");
        exif.SetValue(ExifTag.DateTimeOriginal, "2026:03:12 09:41:07");
        exif.SetValue(ExifTag.DateTime, "2026:03:14 18:02:00");
        image.SetProfile(exif);
        var path = Write("tagged.jpg", image.ToByteArray(MagickFormat.Jpeg));

        var facts = await reader.ReadAsync(path, FileKind.Image);

        facts.Author.ShouldBe("A. Popescu");
        facts.Producer.ShouldBe("SilexCam 2.1");
        facts.ContentCreatedAt.ShouldBe(new DateTimeOffset(2026, 3, 12, 9, 41, 7, TimeSpan.Zero));
        facts.ContentModifiedAt.ShouldBe(new DateTimeOffset(2026, 3, 14, 18, 2, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task A_photo_that_states_a_time_zone_is_not_read_as_if_it_were_universal_time()
    {
        using var image = new MagickImage(MagickColors.SteelBlue, 32, 32);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.DateTimeOriginal, "2026:03:12 09:41:07");
        exif.SetValue(ExifTag.OffsetTimeOriginal, "+03:00");
        image.SetProfile(exif);
        var path = Write("zoned.jpg", image.ToByteArray(MagickFormat.Jpeg));

        var facts = await reader.ReadAsync(path, FileKind.Image);

        facts.ContentCreatedAt.ShouldBe(
            new DateTimeOffset(2026, 3, 12, 9, 41, 7, TimeSpan.FromHours(3)));
    }

    [Fact]
    public async Task A_photo_with_no_tags_states_nothing_rather_than_guessing()
    {
        using var image = new MagickImage(MagickColors.SteelBlue, 8, 8);
        var path = Write("bare.png", image.ToByteArray(MagickFormat.Png));

        (await reader.ReadAsync(path, FileKind.Image)).ShouldBe(ContentFacts.None);
    }

    [Fact]
    public async Task A_recording_states_how_long_it_runs_and_in_what_encoding()
    {
        // 90 000 ticks at 30 000 per second: three seconds, written the way a camera writes it.
        var path = Write("descent.mp4", MediaSamples.IsoBaseMedia(timescale: 30_000, duration: 90_000, codec: "avc1"));

        var facts = await reader.ReadAsync(path, FileKind.Video);

        facts.DurationSeconds.ShouldNotBeNull();
        facts.DurationSeconds.Value.ShouldBe(3d, 0.0001);
        facts.Codec.ShouldBe("avc1");
    }

    [Fact]
    public async Task A_recording_whose_header_comes_after_its_samples_is_still_read()
    {
        // The common layout for anything recorded rather than authored: the sample data is
        // written first and the header that describes it only when recording stops. Reading
        // the opening bytes alone would find nothing.
        var path = Write(
            "trailing.mp4",
            MediaSamples.IsoBaseMedia(timescale: 1000, duration: 4500, codec: "mp4a", headerLast: true));

        var facts = await reader.ReadAsync(path, FileKind.Audio);

        facts.DurationSeconds.ShouldNotBeNull();
        facts.DurationSeconds.Value.ShouldBe(4.5d, 0.0001);
        facts.Codec.ShouldBe("mp4a");
    }

    [Fact]
    public async Task An_uncompressed_recording_states_its_length_from_its_own_sample_rate()
    {
        var path = Write("interview.wav", MediaSamples.Wave(bytesPerSecond: 16_000, dataBytes: 32_000));

        var facts = await reader.ReadAsync(path, FileKind.Audio);

        facts.DurationSeconds.ShouldNotBeNull();
        facts.DurationSeconds.Value.ShouldBe(2d, 0.0001);
        facts.Codec.ShouldBe("pcm");
    }

    [Fact]
    public async Task A_damaged_or_unknown_recording_states_nothing_instead_of_failing_the_upload()
    {
        // A container whose declared box length runs past the end of the file: exactly what a
        // truncated transfer leaves behind, and the shape a hostile upload would use.
        var truncated = MediaSamples.IsoBaseMedia(
            timescale: 1000, duration: 1000, codec: "avc1", headerLast: true);
        var path = Write("cut.mp4", truncated[..(truncated.Length / 2)]);

        (await reader.ReadAsync(path, FileKind.Video)).ShouldBe(ContentFacts.None);
        (await reader.ReadAsync(Write("noise.bin", [1, 2, 3, 4]), FileKind.Audio)).ShouldBe(ContentFacts.None);
        (await reader.ReadAsync(Path.Combine(root, "absent.mp4"), FileKind.Video)).ShouldBe(ContentFacts.None);
    }

    [Fact]
    public async Task A_recording_that_states_a_date_no_calendar_reaches_states_no_date()
    {
        // A long recording writes its timestamps as 64-bit second counts, and those counts
        // come straight out of the uploaded bytes — so they can hold a number far past the end
        // of representable time. Describing a file is never a reason to refuse it: this has to
        // read as "stated nothing", not escape as an error and leave the bytes in the store
        // with no row pointing at them.
        var impossible = Write("hostile.mp4", MediaSamples.IsoBaseMediaWideHeader(ulong.MaxValue, 1000, 4000));

        var facts = await reader.ReadAsync(impossible, FileKind.Video);

        facts.ContentCreatedAt.ShouldBeNull();
        facts.ContentModifiedAt.ShouldBeNull();

        // The positive twin, in the same 64-bit layout: a count a calendar does reach is read,
        // so the nulls above are the value being impossible rather than the wide header going
        // unread. 44 625 days after the start of 1904, which the format counts from.
        var plausible = Write("wide.mp4", MediaSamples.IsoBaseMediaWideHeader(3_855_600_000, 1000, 4000));

        var honest = await reader.ReadAsync(plausible, FileKind.Video);

        honest.ContentCreatedAt.ShouldBe(new DateTimeOffset(2026, 3, 6, 0, 0, 0, TimeSpan.Zero));
        honest.DurationSeconds.ShouldNotBeNull();
        honest.DurationSeconds.Value.ShouldBe(4d, 0.0001);
    }

    [Fact]
    public async Task Formats_nothing_here_can_read_state_nothing_rather_than_something_invented()
    {
        var path = Write("notes.txt", "Raport de tură\n"u8.ToArray());

        (await reader.ReadAsync(path, FileKind.Document)).ShouldBe(ContentFacts.None);
        (await reader.ReadAsync(path, FileKind.Other)).ShouldBe(ContentFacts.None);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(root, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
