// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using SilexGis.Domain.Documents;

namespace SilexGis.Domain.ResLinks;

/// <summary>
/// A picture as a phone measures pins on it: its stored size, before any orientation, and how
/// every rendition is drawn upright from that frame — a mirror first, then a clockwise turn.
/// </summary>
/// <param name="Width">Stored pixels across, before orientation.</param>
/// <param name="Height">Stored pixels down, before orientation.</param>
/// <param name="Mirrored">Whether the stored pixels are flipped left to right before the turn.</param>
/// <param name="TurnDegrees">The clockwise turn after the mirror: 0, 90, 180 or 270.</param>
public readonly record struct PictureFrame(int Width, int Height, bool Mirrored, int TurnDegrees)
{
    /// <summary>
    /// The frame of a stored file: its size, and the orientation code with the quarter turns
    /// somebody recorded on it, combined the way the renderer combines them.
    /// </summary>
    public static PictureFrame Of(int width, int height, int? orientationTag, int quarterTurns)
    {
        var (mirrored, turn) = PhotoOrientation.Upright(orientationTag, quarterTurns);
        return new PictureFrame(width, height, mirrored, turn);
    }

    /// <summary>The picture's width as it is drawn.</summary>
    public int UprightWidth => TurnDegrees is 90 or 270 ? Height : Width;

    /// <summary>The picture's height as it is drawn.</summary>
    public int UprightHeight => TurnDegrees is 90 or 270 ? Width : Height;

    /// <summary>Whether a stored-frame pixel falls on the picture at all.</summary>
    public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
}

/// <summary>
/// The one conversion between a phone's pixel on a picture and the fraction of the picture as it
/// is drawn, which is what the browser measures and draws regions in.
/// </summary>
/// <remarks>
/// <para>
/// The two frames differ. A phone's pixel is in the stored frame, before any orientation; a
/// region's fraction is of the upright picture, after the orientation code and any turn recorded in
/// the browser. So the pixel is mirrored, then turned, exactly as the renditions are drawn, and only
/// then divided — a plain <c>x / width</c> would put every pin of a turned or tagged picture in the
/// wrong place.
/// </para>
/// <para>
/// The fraction is taken at the pixel's centre. That makes a mirror exactly <c>f → 1 − f</c>, which
/// matches a mirror's <c>width − 1 − x</c>, and it makes the way back exact for whole pixels: for any
/// picture up to a million pixels across, the product of a fraction and the width lands far inside
/// half a pixel of the centre it was taken at, so the floor returns the pixel it came from.
/// </para>
/// </remarks>
public static class PlacePointFrame
{
    /// <summary>
    /// The fraction of the upright picture at the centre of a stored-frame pixel, or null when the
    /// pixel does not fall on the picture.
    /// </summary>
    public static (double X, double Y)? ToFraction(PictureFrame frame, int x, int y)
    {
        if (!frame.Contains(x, y))
        {
            return null;
        }

        var (u, v) = ToUpright(frame, x, y);
        return ((u + 0.5) / frame.UprightWidth, (v + 0.5) / frame.UprightHeight);
    }

    /// <summary>
    /// The stored-frame pixel under a fraction of the upright picture. A fraction on the far edge
    /// (exactly 1) is taken as the last pixel.
    /// </summary>
    public static (int X, int Y) FromFraction(PictureFrame frame, double fx, double fy)
    {
        var u = Math.Clamp((int)Math.Floor(fx * frame.UprightWidth), 0, frame.UprightWidth - 1);
        var v = Math.Clamp((int)Math.Floor(fy * frame.UprightHeight), 0, frame.UprightHeight - 1);
        return FromUpright(frame, u, v);
    }

    /// <summary>A stored-frame pixel as it lands on the upright picture.</summary>
    public static (int U, int V) ToUpright(PictureFrame frame, int x, int y)
    {
        var (w, h) = (frame.Width, frame.Height);
        var mx = frame.Mirrored ? w - 1 - x : x;
        return frame.TurnDegrees switch
        {
            90 => (h - 1 - y, mx),
            180 => (w - 1 - mx, h - 1 - y),
            270 => (y, w - 1 - mx),
            _ => (mx, y),
        };
    }

    /// <summary>The stored-frame pixel an upright pixel was drawn from.</summary>
    public static (int X, int Y) FromUpright(PictureFrame frame, int u, int v)
    {
        var (w, h) = (frame.Width, frame.Height);
        var (mx, y) = frame.TurnDegrees switch
        {
            90 => (v, h - 1 - u),
            180 => (w - 1 - u, h - 1 - v),
            270 => (w - 1 - v, u),
            _ => (u, v),
        };
        return (frame.Mirrored ? w - 1 - mx : mx, y);
    }
}

/// <summary>
/// A point of a picture, in fractions of the picture as it is drawn, carried through a turn recorded
/// on the picture afterwards — so a point measured before the turn still marks the same spot of the
/// picture after it.
/// </summary>
public static class ImageRegionTurn
{
    /// <summary>The point after the picture is turned clockwise by <paramref name="quarterTurns"/>.</summary>
    public static (double X, double Y) TurnPoint(double x, double y, int quarterTurns) =>
        PhotoOrientation.Normalize(quarterTurns) switch
        {
            1 => (1 - y, x),
            2 => (1 - x, 1 - y),
            3 => (y, 1 - x),
            _ => (x, y),
        };
}

/// <summary>
/// The anchor payload of a place pinned on a phone's map: a point region carrying, beside the
/// fraction it is drawn at, the stored-frame pixel the phone measured.
/// </summary>
/// <remarks>
/// The pixel is the authority — the phone reads it back verbatim — and the fraction is a copy for
/// drawing, rewritten whenever the pixel, the file it is measured on or that file's recorded turn
/// changes. A pixel that does not fall on the picture as drawn, or one on a picture whose size is
/// not known, carries no fraction at all, and every reader that draws regions skips a point without
/// one rather than guessing where it goes.
/// </remarks>
public static class PlacePointAnchor
{
    /// <summary>The field holding the stored-frame pixel across.</summary>
    public const string PixelX = "px";

    /// <summary>The field holding the stored-frame pixel down.</summary>
    public const string PixelY = "py";

    /// <summary>The largest pixel either axis may carry — the bound the phone's upload has.</summary>
    public const int PixelMax = 1_000_000;

    /// <summary>A parsed place-pin anchor.</summary>
    public readonly record struct Parsed(int Px, int Py, double? X, double? Y);

    /// <summary>The payload for a pixel, drawn at <paramref name="fraction"/> when it has one.</summary>
    public static string Write(int px, int py, (double X, double Y)? fraction)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("shape", "point");
            if (fraction is { } f)
            {
                json.WriteNumber("x", f.X);
                json.WriteNumber("y", f.Y);
            }

            json.WriteNumber(PixelX, px);
            json.WriteNumber(PixelY, py);
            json.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>The payload for a pixel on a picture of a known frame, or of none.</summary>
    public static string Write(int px, int py, PictureFrame? frame) =>
        Write(px, py, frame is { } known ? PlacePointFrame.ToFraction(known, px, py) : null);

    /// <summary>A place-pin payload read back, or null when it is not one.</summary>
    public static Parsed? Read(string? anchor)
    {
        if (string.IsNullOrEmpty(anchor))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(anchor);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty(PixelX, out var px) || !px.TryGetInt32(out var x)
                || !root.TryGetProperty(PixelY, out var py) || !py.TryGetInt32(out var y))
            {
                return null;
            }

            double? fx = root.TryGetProperty("x", out var ex) && ex.ValueKind == JsonValueKind.Number ? ex.GetDouble() : null;
            double? fy = root.TryGetProperty("y", out var ey) && ey.ValueKind == JsonValueKind.Number ? ey.GetDouble() : null;
            return new Parsed(x, y, fx, fy);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether a payload asserts a stored-frame pixel — which only the place-pin writer, knowing
    /// the frame, may do. Text, so it can be asked of a history row's recorded value as well.
    /// </summary>
    public static bool CarriesStoredPixel(string? anchor)
    {
        if (string.IsNullOrEmpty(anchor))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(anchor);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && (document.RootElement.TryGetProperty(PixelX, out _) || document.RootElement.TryGetProperty(PixelY, out _));
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
