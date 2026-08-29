// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Documents;

/// <summary>
/// Which recorded formats are worth reading text out of.
/// <para>
/// The answer is a property of the format alone, so it lives beside the format table rather
/// than inside the readers: the decision "should this upload be read at all" is taken where a
/// file is stored, long before anything opens it, and taking it from a list of readers would
/// mean the storage path knowing which readers exist.
/// </para>
/// <para>
/// Nothing here promises text will be found. A format carries a text layer or it does not; a
/// scanned page photographed into a PDF is a picture in a paged wrapper and yields nothing,
/// which is a fact about that file rather than about its format, and is reported as an empty
/// result rather than as a failure. Formats that are only ever pictures, sound or geometry are
/// absent from this list entirely, so nothing is queued to read them.
/// </para>
/// </summary>
public static class TextExtractionFormats
{
    /// <summary>Formats named exactly, because their media type identifies one file layout.</summary>
    private static readonly string[] Named =
    [
        "application/pdf",
        "application/rtf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "application/msword",
        "application/vnd.ms-excel",
        "application/vnd.ms-powerpoint",

        // A legacy compound file whose sub-type the upload's name did not settle. It is one of
        // the three above; which one is a question for whatever opens it, not for this list.
        "application/x-ole-storage",

        "application/json",
        "application/xml",
        "application/x-yaml",
        "application/yaml",
        "application/gpx+xml",
        "application/vnd.google-earth.kml+xml",
        "application/geo+json",

        // The link-annotated text format. Read like any other document, deliberately: its
        // whole point is prose somebody wrote, and prose nobody can find is prose nobody
        // reads. The reader that handles it produces the words themselves, never the JSON
        // structure they are stored in.
        AnnotatedText.MediaType,
    ];

    /// <summary>Whether text is worth trying to read out of a file recorded under this format.</summary>
    public static bool CarriesText(string? mimeType)
    {
        if (string.IsNullOrEmpty(mimeType))
        {
            return false;
        }

        // Every "text/..." media type is by definition characters, whatever the sub-type says.
        return mimeType.StartsWith("text/", StringComparison.Ordinal)
            || mimeType.StartsWith("application/vnd.oasis.opendocument.", StringComparison.Ordinal)
            || Named.Contains(mimeType, StringComparer.Ordinal);
    }
}
