// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text;

namespace SilexGis.Infrastructure.Persistence;

/// <summary>
/// Builds the demo dataset's survey report: a small, genuinely valid PDF with genuinely
/// selectable text on two pages.
/// </summary>
/// <remarks>
/// It is written by hand rather than produced by a library because the demo needs exactly one
/// thing from it — words that a reader can extract and a browser can select — and adding a PDF
/// writer to the server to obtain two pages of Latin text would be a dependency carried forever
/// for a fixture. The structure below is the minimum a PDF may have: a catalogue, a page tree,
/// one content stream per page and one of the fourteen fonts every reader is required to know,
/// so no font data is embedded and nothing here has to rasterise anything.
/// <para>
/// The cross-reference table records where each object begins, so the offsets are collected
/// while the bytes are written rather than computed afterwards. Readers rebuild a broken table
/// silently, which is precisely why it is worth getting right: a fixture that only works
/// because every reader repairs it proves nothing about the readers.
/// </para>
/// </remarks>
public static class DemoPdf
{
    /// <summary>
    /// The words on each page, in reading order. Public because the demo's resource link
    /// anchors a text selection at one of them, and a fixture whose quote and whose text come
    /// from two places will disagree the first time either is edited.
    /// </summary>
    public static readonly IReadOnlyList<IReadOnlyList<string>> Pages =
    [
        [
            "Pestera Demo Mare - survey report",
            "",
            "The 1987 expedition surveyed 1,234 metres of passage between the",
            "main entrance and the second sump. Beyond the sump the passage",
            "widens into a chamber roughly forty metres across, floored with",
            "breakdown and drained by a small stream sink at its eastern end.",
            "",
            "Air movement was noted at the upper entrance throughout the week,",
            "which is the reason the connection was looked for at all.",
        ],
        [
            "Pestera Demo Mare - survey report (page 2)",
            "",
            "Equipment left in place: two bolts at the head of the first pitch,",
            "removed the following season. The upper entrance is a natural shaft",
            "and takes water in spate; the main entrance does not.",
            "",
            "This document is part of the demonstration dataset. Nothing in it",
            "describes a real cave.",
        ],
    ];

    /// <summary>The passage the demo's resource link points at, quoted exactly.</summary>
    public const string LinkedQuote = "widens into a chamber roughly forty metres across";

    private const int PageWidth = 595;
    private const int PageHeight = 842;
    private const int FontSize = 12;
    private const int LineHeight = 18;
    private const int MarginLeft = 56;
    private const int MarginTop = 72;

    public static byte[] Build()
    {
        var contents = Pages.Select(ContentStream).ToArray();
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [{string.Join(' ', Pages.Select((_, i) => $"{3 + i * 2} 0 R"))}] /Count {Pages.Count} >>",
        };

        for (var i = 0; i < Pages.Count; i += 1)
        {
            objects.Add(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth} {PageHeight}] "
                + $"/Resources << /Font << /F1 {3 + Pages.Count * 2} 0 R >> >> /Contents {4 + i * 2} 0 R >>");
            objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(contents[i])} >>\nstream\n{contents[i]}\nendstream");
        }

        // Helvetica is one of the fourteen a reader must provide, so nothing is embedded and
        // the text stays real text rather than curves.
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");

        var bytes = new List<byte>();
        void Write(string text) => bytes.AddRange(Encoding.ASCII.GetBytes(text));

        Write("%PDF-1.4\n");
        // A comment of high bytes, which is what tells anything transferring the file that it
        // is binary and must not have its line endings rewritten.
        bytes.AddRange([(byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n']);

        var offsets = new int[objects.Count];
        for (var i = 0; i < objects.Count; i += 1)
        {
            offsets[i] = bytes.Count;
            Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xrefOffset = bytes.Count;
        Write($"xref\n0 {objects.Count + 1}\n");
        Write("0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            Write($"{offset.ToString("D10", CultureInfo.InvariantCulture)} 00000 n \n");
        }

        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        return [.. bytes];
    }

    private static string ContentStream(IReadOnlyList<string> lines)
    {
        var text = new StringBuilder();
        text.Append("BT\n");
        text.Append($"/F1 {FontSize} Tf\n");
        text.Append($"{LineHeight} TL\n");
        text.Append($"{MarginLeft} {PageHeight - MarginTop} Td\n");
        foreach (var line in lines)
        {
            // A blank line still moves the cursor: the spacing is part of what makes the
            // extracted text read the way the page does.
            text.Append($"({Escape(line)}) Tj\nT*\n");
        }
        text.Append("ET");
        return text.ToString();
    }

    /// <summary>The three characters a PDF string may not carry raw.</summary>
    private static string Escape(string line) =>
        line.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
}
