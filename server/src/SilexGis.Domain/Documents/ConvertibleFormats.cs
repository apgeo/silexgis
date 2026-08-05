// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Documents;

/// <summary>
/// Which recorded formats are worth turning into a portable document, so that a page of them
/// can be drawn.
/// <para>
/// Pages are drawn on the server from a portable document, which is the only format that
/// paginates itself. Everything a word processor, spreadsheet or presentation editor saves is
/// laid out at the moment it is opened, so nothing that merely reads the file can say where a
/// page ends — a converter has to decide a paper size and a font first. That is why these
/// formats have a document page with nothing on it until a conversion produces something that
/// does have pages.
/// </para>
/// <para>
/// This is a property of the format alone, and the answer does not change with what the
/// installation happens to have deployed: a converted copy is either possible in principle or
/// it is not. Whether one can be made here and now is a separate question, asked of the
/// converter, and keeping the two apart is what lets an installation with no converter say
/// "this could be shown, but nothing here can do it" instead of staying silent.
/// </para>
/// </summary>
public static class ConvertibleFormats
{
    /// <summary>What a converted copy is stored as.</summary>
    public const string TargetMediaType = "application/pdf";

    /// <summary>
    /// Formats named exactly. Deliberately narrow: only office-suite documents, whose whole
    /// difficulty is that they carry no pagination of their own. Pictures, sound, geometry and
    /// plain text are all shown as themselves and gain nothing from being turned into a
    /// portable document — converting them would replace something a reader can already use
    /// with a worse copy of it.
    /// </summary>
    private static readonly string[] Named =
    [
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "application/msword",
        "application/vnd.ms-excel",
        "application/vnd.ms-powerpoint",
        "application/rtf",

        // A legacy compound file whose sub-type the upload's name did not settle. It is one of
        // the older three above, and a converter that opens office documents opens it too.
        "application/x-ole-storage",
    ];

    /// <summary>
    /// Whether a converted copy of a file recorded under this format could give it pages.
    /// False for a portable document itself: it already has them, and a copy would be waste.
    /// </summary>
    public static bool CanConvert(string? mimeType)
    {
        if (string.IsNullOrEmpty(mimeType))
        {
            return false;
        }

        return mimeType.StartsWith("application/vnd.oasis.opendocument.", StringComparison.Ordinal)
            || Named.Contains(mimeType, StringComparer.Ordinal);
    }
}
