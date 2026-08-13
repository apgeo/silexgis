// SPDX-License-Identifier: AGPL-3.0-or-later
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ImageMagick;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace SilexGis.Infrastructure.Documents;

/// <summary>
/// The word-processor writer, over the package this project already carries for reading the same
/// format back. Everything is built in memory: a write-up is small, and a temporary file would be
/// one more thing to clean up on two operating systems.
/// </summary>
/// <remarks>
/// Every measurement here is a constant. Fitting text to its own width means measuring it, which
/// means loading a font, and the runtime container this application ships in has no fonts
/// installed at all — the call that measures throws there while succeeding on every developer
/// machine, so the failure would appear only after deployment and only on the route that writes a
/// document. Nothing below asks the platform about a font, a glyph or a line height; the page is
/// laid out by the word processor that opens it, which is the one place that knows.
/// </remarks>
public sealed class DocxDocumentWriter : IDocumentWriter
{
    /// <summary>A4 portrait, in twentieths of a point.</summary>
    private const uint PageWidthTwips = 11906;

    /// <summary>A4 portrait, in twentieths of a point.</summary>
    private const uint PageHeightTwips = 16838;

    /// <summary>Two centimetres of margin, in twentieths of a point.</summary>
    private const uint MarginTwips = 1134;

    /// <summary>Type sizes, in half-points, so a document reads as a document and not a form.</summary>
    private const string TitleHalfPoints = "32";

    private const string HeadingHalfPoints = "26";
    private const string BodyHalfPoints = "22";
    private const string NoteHalfPoints = "18";

    /// <summary>The quieter grey an aside is written in.</summary>
    private const string NoteColour = "595959";

    /// <summary>
    /// How wide a picture is placed, in English metric units (914400 to the inch).
    /// </summary>
    /// <remarks>
    /// Six inches: an A4 page with two-centimetre margins has about 6.7 inches of text column, so
    /// this leaves a picture comfortably inside it whatever the reader's own margins turn out to
    /// be. The height follows from the picture's own proportions, never from a measurement of the
    /// page.
    /// </remarks>
    private const long PlateWidthEmu = 5486400;

    /// <summary>
    /// How much of a page a single picture may take, in English metric units.
    /// </summary>
    /// <remarks>
    /// A tall picture placed at the full column width would run off the bottom of the page and be
    /// scaled down by the word processor, or worse, sit alone on a page of its own. Seven inches
    /// keeps the tallest portrait plate and its caption on one page.
    /// </remarks>
    private const long PlateMaxHeightEmu = 6400800;

    /// <summary>What a picture is re-encoded to before it is placed.</summary>
    /// <remarks>
    /// A document that leaves this application is opened by word processors of every vintage, and
    /// the newer web formats are not read by all of them. Re-encoding also strips the picture's
    /// metadata a second time, on the way in: a photograph's own metadata carries where it was
    /// taken, and a document is forwarded far past the audience the picture was released to.
    /// </remarks>
    private const MagickFormat PlateFormat = MagickFormat.Jpeg;

    public string ContentType =>
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    public string Extension => "docx";

    public byte[] Write(IReadOnlyList<DocumentBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        using var buffer = new MemoryStream();
        using (var document = WordprocessingDocument.Create(
            buffer, WordprocessingDocumentType.Document, autoSave: true))
        {
            var main = document.AddMainDocumentPart();
            var body = new W.Body();

            // Pictures are numbered across the document: a drawing's identifier has to be unique
            // within it, and a repeated one makes the file unreadable rather than merely odd.
            var plate = 0u;
            foreach (var block in blocks)
            {
                if (block.Kind == DocumentBlockKind.Picture)
                {
                    if (block.Image is { Length: > 0 } bytes)
                    {
                        body.AppendChild(PlateParagraph(main, bytes, ++plate));
                        if (!string.IsNullOrWhiteSpace(block.Text))
                        {
                            body.AppendChild(TextParagraph(DocumentBlockKind.Note, block.Text, null));
                        }
                    }

                    continue;
                }

                body.AppendChild(TextParagraph(block.Kind, block.Text, block.Label));
            }

            body.AppendChild(new W.SectionProperties(
                new W.PageSize { Width = PageWidthTwips, Height = PageHeightTwips },
                new W.PageMargin
                {
                    Top = (int)MarginTwips,
                    Bottom = (int)MarginTwips,
                    Left = MarginTwips,
                    Right = MarginTwips,
                    Header = 0,
                    Footer = 0,
                    Gutter = 0,
                }));

            main.Document = new W.Document(body);
        }

        return buffer.ToArray();
    }

    /// <summary>One paragraph of words, in the shape its kind is written in.</summary>
    private static W.Paragraph TextParagraph(DocumentBlockKind kind, string text, string? label)
    {
        var paragraph = new W.Paragraph();
        paragraph.AppendChild(Spacing(kind));

        if (label is not null)
        {
            paragraph.AppendChild(Run($"{label}: ", BodyHalfPoints, bold: true, italic: false, colour: null));
        }

        var body = kind switch
        {
            DocumentBlockKind.Title => Run(text, TitleHalfPoints, bold: true, italic: false, colour: null),
            DocumentBlockKind.Heading => Run(text, HeadingHalfPoints, bold: true, italic: false, colour: null),
            DocumentBlockKind.Note => Run(text, NoteHalfPoints, bold: false, italic: true, colour: NoteColour),
            // A literal mark rather than a numbering definition: a bulleted list in this format is
            // a separate part of the package with its own identifiers, and a document somebody
            // edits afterwards gains nothing from carrying one.
            DocumentBlockKind.Bullet => Run($"• {text}", BodyHalfPoints, bold: false, italic: false, colour: null),
            _ => Run(text, BodyHalfPoints, bold: false, italic: false, colour: null),
        };

        paragraph.AppendChild(body);
        return paragraph;
    }

    /// <summary>How much air a kind of block is given above and below it.</summary>
    private static W.ParagraphProperties Spacing(DocumentBlockKind kind)
    {
        var (before, after) = kind switch
        {
            DocumentBlockKind.Title => (0, 240),
            DocumentBlockKind.Heading => (240, 120),
            DocumentBlockKind.Bullet => (0, 0),
            _ => (0, 120),
        };

        return new W.ParagraphProperties(new W.SpacingBetweenLines
        {
            Before = before.ToString(System.Globalization.CultureInfo.InvariantCulture),
            After = after.ToString(System.Globalization.CultureInfo.InvariantCulture),
        });
    }

    /// <summary>
    /// A run of text. Line breaks inside it are kept as breaks: prose typed into a form arrives
    /// with the shape its author gave it, and a report that ran it all together would be a
    /// different document from the one on screen.
    /// </summary>
    private static W.Run Run(string text, string halfPoints, bool bold, bool italic, string? colour)
    {
        // Built in the order the format defines for these elements — weight, slope, colour, size.
        // A word processor reads a run whose properties are out of order as a damaged document
        // rather than as an unusual one, so the order here is a rule and not a style.
        var properties = new W.RunProperties();
        if (bold)
        {
            properties.AppendChild(new W.Bold());
        }

        if (italic)
        {
            properties.AppendChild(new W.Italic());
        }

        if (colour is not null)
        {
            properties.AppendChild(new W.Color { Val = colour });
        }

        properties.AppendChild(new W.FontSize { Val = halfPoints });

        var run = new W.Run(properties);
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                run.AppendChild(new W.Break());
            }

            run.AppendChild(new W.Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });
        }

        return run;
    }

    /// <summary>
    /// One picture, re-encoded and placed at a fixed width with its own proportions kept.
    /// </summary>
    private static W.Paragraph PlateParagraph(MainDocumentPart main, byte[] source, uint number)
    {
        using var image = new MagickImage(source);

        // Stripped again here rather than trusted to have been stripped already. This is the last
        // point before the bytes leave the application inside a file that is forwarded, and a
        // photograph's profile holds the position it was taken at.
        image.Strip();
        var encoded = image.ToByteArray(PlateFormat);

        var width = PlateWidthEmu;
        var height = image.Width == 0
            ? PlateWidthEmu
            : (long)(PlateWidthEmu * (double)image.Height / image.Width);
        if (height > PlateMaxHeightEmu)
        {
            width = (long)(PlateWidthEmu * (double)PlateMaxHeightEmu / height);
            height = PlateMaxHeightEmu;
        }

        var part = main.AddImagePart(ImagePartType.Jpeg);
        using (var bytes = new MemoryStream(encoded))
        {
            part.FeedData(bytes);
        }

        var name = $"Picture {number}";
        var drawing = new W.Drawing(
            new DW.Inline(
                new DW.Extent { Cx = width, Cy = height },
                new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                new DW.DocProperties { Id = number, Name = name },
                new DW.NonVisualGraphicFrameDrawingProperties(
                    new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties { Id = 0U, Name = name },
                                new PIC.NonVisualPictureDrawingProperties()),
                            new PIC.BlipFill(
                                new A.Blip { Embed = main.GetIdOfPart(part) },
                                new A.Stretch(new A.FillRectangle())),
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset { X = 0L, Y = 0L },
                                    new A.Extents { Cx = width, Cy = height }),
                                new A.PresetGeometry(new A.AdjustValueList())
                                {
                                    Preset = A.ShapeTypeValues.Rectangle,
                                })))
                    {
                        Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture",
                    }))
            {
                DistanceFromTop = 0U,
                DistanceFromBottom = 0U,
                DistanceFromLeft = 0U,
                DistanceFromRight = 0U,
            });

        var paragraph = new W.Paragraph(Spacing(DocumentBlockKind.Paragraph));
        paragraph.AppendChild(new W.Run(drawing));
        return paragraph;
    }
}
