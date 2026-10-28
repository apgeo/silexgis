// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Documents;

/// <summary>
/// Turning a photograph the right way up, without touching what was uploaded.
///
/// <para>
/// The upload is immutable: its hash is what duplicate detection compares and what the archive
/// means when it says these are the bytes it was given. So a turn is a number stored beside the
/// file and applied wherever the picture is drawn — thumbnails, the gallery, the lightbox — and
/// the download still hands over exactly what arrived. Turning a picture four times leaves the
/// row where it started rather than four copies on a disk.
/// </para>
/// </summary>
public static class PhotoOrientation
{
    /// <summary>Quarter-turns in a full turn — the modulus everything here works in.</summary>
    public const int Turns = 4;

    /// <summary>
    /// A stored quarter-turn count, normalised to 0–3.
    /// </summary>
    /// <remarks>
    /// Negative input is normalised rather than refused, because "turn it the other way" is the
    /// natural thing for a caller to send and −1 and 3 are the same picture. C#'s remainder
    /// keeps the sign of the left operand, so the second modulus is what turns −1 into 3 instead
    /// of leaving it negative.
    /// </remarks>
    public static int Normalize(int quarterTurns) => ((quarterTurns % Turns) + Turns) % Turns;

    /// <summary>The stored value after turning a picture by a further amount.</summary>
    public static int Rotate(int current, int byQuarterTurns) =>
        Normalize(current + byQuarterTurns);

    /// <summary>How many degrees clockwise a renderer should turn the picture.</summary>
    public static int Degrees(int quarterTurns) => Normalize(quarterTurns) * 90;

    /// <summary>
    /// Whether the turn swaps the picture's width and height — true for the quarter and
    /// three-quarter turns.
    /// </summary>
    /// <remarks>
    /// This is what a gallery needs to lay a grid out before it has the bytes: a portrait
    /// picture turned once is landscape, and a tile sized from the stored dimensions alone would
    /// be the wrong shape for every rotated photograph in the archive.
    /// </remarks>
    public static bool SwapsAxes(int quarterTurns) => Normalize(quarterTurns) % 2 == 1;

    /// <summary>The dimensions a rendering of this picture will actually have.</summary>
    public static (int Width, int Height) Apply(int width, int height, int quarterTurns) =>
        SwapsAxes(quarterTurns) ? (height, width) : (width, height);

    /// <summary>
    /// How a rendering is drawn from the picture as stored: flipped left to right first when
    /// <c>Mirrored</c>, then turned clockwise by <c>TurnDegrees</c> (0, 90, 180 or 270).
    /// </summary>
    /// <param name="orientationTag">
    /// The orientation code the renderer obeys, 1–8 as EXIF and TIFF number them. Null, or a value
    /// outside that range, is a picture drawn as stored — which is what the renderer does with a
    /// tag it cannot read.
    /// </param>
    /// <param name="quarterTurns">The turn somebody recorded, applied after the tag.</param>
    /// <remarks>
    /// <para>
    /// Every one of the eight codes is one of the four turns, with or without a flip first. The
    /// four that mirror (2, 4, 5, 7) are the ones a camera never writes and a scanner or an editor
    /// sometimes does; a reader who assumed renderings never mirror would put every pin of such a
    /// picture on the wrong side of it. Writing each as "flip, then turn" rather than "turn, then
    /// flip" is a choice, and the only one that matters is that it is fixed: it is what lets a
    /// recorded turn simply add to the tag's own, because it is applied after both.
    /// </para>
    /// <para>
    /// Code 5 is the picture reflected in its main diagonal — flipped and turned three quarters —
    /// and code 7 in its other diagonal, flipped and turned one quarter.
    /// </para>
    /// </remarks>
    public static (bool Mirrored, int TurnDegrees) Upright(int? orientationTag, int quarterTurns)
    {
        var (mirrored, tagTurns) = orientationTag switch
        {
            2 => (true, 0),
            3 => (false, 2),
            4 => (true, 2),
            5 => (true, 3),
            6 => (false, 1),
            7 => (true, 1),
            8 => (false, 3),
            _ => (false, 0),
        };

        return (mirrored, Degrees(tagTurns + quarterTurns));
    }

    /// <summary>
    /// The dimensions a rendering of this picture will actually have, the orientation code and the
    /// recorded turn both applied.
    /// </summary>
    public static (int Width, int Height) UprightSize(int width, int height, int? orientationTag, int quarterTurns) =>
        Apply(width, height, Upright(orientationTag, quarterTurns).TurnDegrees / 90);
}
