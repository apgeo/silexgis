// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace SilexGis.Api.Features.Sync;

/// <summary>
/// Where a device resumes a download: the selection the position was issued against, and the
/// change key and identifier of the last row it was given.
/// </summary>
/// <remarks>
/// <para>
/// The whole of the download's position lives in this value and travels with the device, so the
/// server keeps nothing between requests. That is not an optimisation: a phone in a cave loses
/// its connection mid-page and comes back hours later on a different address, and a server-side
/// session would either have expired or would have to be kept per device indefinitely.
/// </para>
/// <para>
/// The key is a pair, never a timestamp alone. Two rows written in the same transaction share a
/// stamp to the microsecond, and a cursor that could not tell them apart would either hand one of
/// them out twice or skip it — which reads to the device as a cave that will not go away, or one
/// that never arrives.
/// </para>
/// <para>
/// The selection's revision travels with the pair because the pair alone cannot see a change of
/// selection. A cave added to a set does not touch a single feature row, so nothing under it moves
/// past a caught-up device's watermark and the whole cave would be invisible to that device for
/// ever — until some unrelated edit happened to bump a row. Carrying the revision is what turns
/// that silent hole into an answer the device can act on.
/// </para>
/// <para>
/// It is opaque to the client on purpose. A device that parsed it would be pinning the ordering
/// key, and the ordering key is the server's to change; a device that composed one could ask the
/// server to resume from a position no read ever produced.
/// </para>
/// <para>
/// A position names the stream it was issued by, and is refused by every other. Each stream
/// orders its own rows on its own key, so a position from one says nothing about where to resume
/// another: honoured, it would skip every row of the second stream that happened to sort before
/// a time read off the first — rows the device would then never be sent. The feature stream's
/// positions carry no name, which keeps them exactly as devices were already holding them.
/// </para>
/// </remarks>
public readonly record struct SyncCursor(long SetRevision, DateTimeOffset ChangedAt, Guid Id)
{
    /// <summary>
    /// Marks the encoding this value was written with. A later shape can be told from this one
    /// rather than guessed at, and a device holding an old cursor is refused rather than resumed
    /// from a position that means something else now.
    /// </summary>
    private const string Version = "2";

    /// <summary>The opaque token a device sends back to resume <paramref name="stream"/> from here.</summary>
    public string Encode(SyncStream stream)
    {
        var plain = string.Create(
            CultureInfo.InvariantCulture,
            $"{Version}|{SetRevision}|{ChangedAt.UtcTicks}|{Id:N}");
        if (NameOf(stream) is { } name)
        {
            plain += "|" + name;
        }

        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(plain));
    }

    /// <summary>
    /// Reads a token a device sent back. Returns false for anything this server did not write —
    /// truncated, re-encoded, hand-made or from an older shape — so a bad cursor is answered
    /// rather than silently treated as "start from the beginning", which would hand a device a
    /// full re-download it did not ask for and could not tell from an incremental one. A token
    /// another stream issued is refused the same way.
    /// </summary>
    public static bool TryDecode(string? token, SyncStream stream, out SyncCursor cursor)
    {
        cursor = default;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[128];
        if (token.Length > 160 || !Base64Url.TryDecodeFromChars(token, buffer, out var written))
        {
            return false;
        }

        var name = NameOf(stream);
        var parts = Encoding.UTF8.GetString(buffer[..written]).Split('|');
        if (parts.Length != (name is null ? 4 : 5)
            || (name is not null && parts[4] != name)
            || parts[0] != Version
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var revision)
            || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks < 0
            || ticks > DateTimeOffset.MaxValue.UtcTicks
            || !Guid.TryParseExact(parts[3], "N", out var id))
        {
            return false;
        }

        cursor = new SyncCursor(revision, new DateTimeOffset(ticks, TimeSpan.Zero), id);
        return true;
    }

    /// <summary>
    /// The name a stream's positions carry. None for the feature stream, whose positions were
    /// issued before there was a second stream to tell them from.
    /// </summary>
    private static string? NameOf(SyncStream stream) => stream switch
    {
        SyncStream.Features => null,
        SyncStream.Documents => "documents",
        SyncStream.Trips => "trips",
        _ => throw new ArgumentOutOfRangeException(nameof(stream), stream, "A stream with no cursor name."),
    };
}
