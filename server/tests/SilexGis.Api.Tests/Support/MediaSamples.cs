// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Text;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// Recording containers with real structure — the boxes and chunks a player reads to learn
/// how long a file runs and how it is encoded. Built here rather than committed as binary
/// fixtures so nothing depends on how a checkout treats an unfamiliar extension, and so the
/// declared length is a number the test chose rather than one it had to trust.
/// </summary>
internal static class MediaSamples
{
    /// <summary>
    /// An ISO base media file (the MP4 family): a brand box, then the movie box holding the
    /// header that states the time base and running time and one track whose sample
    /// description names the encoding.
    /// </summary>
    /// <param name="headerLast">
    /// Writes the sample data before the movie box, which is what anything recorded rather
    /// than authored does — the header cannot be written until recording stops.
    /// </param>
    public static byte[] IsoBaseMedia(uint timescale, ulong duration, string codec, bool headerLast = false)
    {
        var ftyp = Box("ftyp", [.. Ascii("isom"), 0, 0, 2, 0, .. Ascii("isomiso2mp41")]);

        var mvhd = new byte[100];
        BinaryPrimitives.WriteUInt32BigEndian(mvhd.AsSpan(12), timescale);
        BinaryPrimitives.WriteUInt32BigEndian(mvhd.AsSpan(16), (uint)duration);
        var stsd = Box("stsd", [0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 8, .. Ascii(codec)]);
        var moov = Box(
            "moov",
            [.. Box("mvhd", mvhd), .. Box("trak", Box("mdia", Box("minf", Box("stbl", stsd))))]);

        // A sample-data box large enough that a reader looking only at the opening bytes
        // would never reach the header behind it.
        var mdat = Box("mdat", new byte[4096]);
        return headerLast ? [.. ftyp, .. mdat, .. moov] : [.. ftyp, .. moov, .. mdat];
    }

    /// <summary>
    /// An ISO base media file whose movie header uses the 64-bit layout — the one a long
    /// recording uses — with both of its timestamps set to <paramref name="secondsSince1904"/>.
    /// The count is a parameter because it comes out of the uploaded bytes in reality, so a
    /// test can write one no calendar reaches just as easily as a plausible one.
    /// </summary>
    public static byte[] IsoBaseMediaWideHeader(ulong secondsSince1904, uint timescale, ulong duration)
    {
        var ftyp = Box("ftyp", [.. Ascii("isom"), 0, 0, 2, 0, .. Ascii("isomiso2mp41")]);

        // Version 1: a version/flags word, then 64-bit creation and modification times, the
        // time base, and a 64-bit duration.
        var mvhd = new byte[108];
        mvhd[0] = 1;
        BinaryPrimitives.WriteUInt64BigEndian(mvhd.AsSpan(4), secondsSince1904);
        BinaryPrimitives.WriteUInt64BigEndian(mvhd.AsSpan(12), secondsSince1904);
        BinaryPrimitives.WriteUInt32BigEndian(mvhd.AsSpan(20), timescale);
        BinaryPrimitives.WriteUInt64BigEndian(mvhd.AsSpan(24), duration);

        return [.. ftyp, .. Box("moov", Box("mvhd", mvhd)), .. Box("mdat", new byte[256])];
    }

    /// <summary>
    /// A still image in the same container family — what a modern phone camera writes by
    /// default. Structurally an ISO base media file like the recordings above; the brand box
    /// is the only thing that says "photograph" rather than "recording", which is exactly the
    /// distinction a reader has to get right.
    /// </summary>
    public static byte[] IsoBaseMediaImage(string brand, string compatibleBrands)
    {
        var ftyp = Box("ftyp", [.. Ascii(brand), 0, 0, 0, 0, .. Ascii(compatibleBrands)]);

        // The metadata box a decoder reads to find the picture: a version/flags word followed
        // by a handler declaring the contents to be images.
        var hdlr = Box("hdlr", [0, 0, 0, 0, 0, 0, 0, 0, .. Ascii("pict"), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        var meta = Box("meta", [0, 0, 0, 0, .. hdlr]);
        return [.. ftyp, .. meta, .. Box("mdat", new byte[256])];
    }

    /// <summary>
    /// A WAVE file: the format chunk states the encoding and the bytes it plays per second,
    /// and the data chunk's length divided by that rate is the running time exactly.
    /// </summary>
    public static byte[] Wave(uint bytesPerSecond, int dataBytes)
    {
        var fmt = new byte[16];
        BinaryPrimitives.WriteUInt16LittleEndian(fmt.AsSpan(0), 1);      // uncompressed
        BinaryPrimitives.WriteUInt16LittleEndian(fmt.AsSpan(2), 1);      // one channel
        BinaryPrimitives.WriteUInt32LittleEndian(fmt.AsSpan(4), 8000);   // samples per second
        BinaryPrimitives.WriteUInt32LittleEndian(fmt.AsSpan(8), bytesPerSecond);
        BinaryPrimitives.WriteUInt16LittleEndian(fmt.AsSpan(12), 2);     // block alignment
        BinaryPrimitives.WriteUInt16LittleEndian(fmt.AsSpan(14), 16);    // bits per sample

        byte[] body = [.. Chunk("fmt ", fmt), .. Chunk("data", new byte[dataBytes])];
        var riffSize = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(riffSize, (uint)(body.Length + 4));
        return [.. Ascii("RIFF"), .. riffSize, .. Ascii("WAVE"), .. body];
    }

    /// <summary>A length-prefixed box: a big-endian byte count, a four-character name, contents.</summary>
    private static byte[] Box(string name, ReadOnlySpan<byte> contents)
    {
        var size = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(size, (uint)(contents.Length + 8));
        return [.. size, .. Ascii(name), .. contents];
    }

    /// <summary>A RIFF chunk: a four-character name, a little-endian byte count, contents.</summary>
    private static byte[] Chunk(string name, ReadOnlySpan<byte> contents)
    {
        var size = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)contents.Length);
        return [.. Ascii(name), .. size, .. contents];
    }

    private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);
}
