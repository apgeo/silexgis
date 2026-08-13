// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Infrastructure.Documents;

/// <summary>What a block of a written document is.</summary>
public enum DocumentBlockKind
{
    /// <summary>The document's own title, once, at the top.</summary>
    Title = 0,

    /// <summary>A part heading.</summary>
    Heading = 1,

    /// <summary>Ordinary prose. Line breaks inside it are kept.</summary>
    Paragraph = 2,

    /// <summary>A labelled value: a name and what it says, on one line.</summary>
    Field = 3,

    /// <summary>One entry of a list.</summary>
    Bullet = 4,

    /// <summary>An aside — smaller and quieter than prose, for what the document says about itself.</summary>
    Note = 5,

    /// <summary>A picture, with an optional line under it.</summary>
    Picture = 6,
}

/// <summary>
/// One part of a document, in the vocabulary a surface describes its output in.
/// </summary>
/// <remarks>
/// Deliberately small. A document a club secretary edits after downloading it is worth more than
/// one laid out precisely, and every element here has an obvious counterpart in a word processor's
/// own toolbar, so the result can be taken apart and rebuilt by somebody who has never seen this
/// application's source.
/// </remarks>
/// <param name="Kind">What this block is.</param>
/// <param name="Text">The words — for a picture, the line written under it.</param>
/// <param name="Label">The name of a labelled value; null for every other kind.</param>
/// <param name="Image">The bytes of a picture; null for every other kind.</param>
public sealed record DocumentBlock(
    DocumentBlockKind Kind,
    string Text,
    string? Label = null,
    byte[]? Image = null)
{
    public static DocumentBlock Title(string text) => new(DocumentBlockKind.Title, text);

    public static DocumentBlock Heading(string text) => new(DocumentBlockKind.Heading, text);

    public static DocumentBlock Paragraph(string text) => new(DocumentBlockKind.Paragraph, text);

    public static DocumentBlock Field(string label, string value) =>
        new(DocumentBlockKind.Field, value, label);

    public static DocumentBlock Bullet(string text) => new(DocumentBlockKind.Bullet, text);

    public static DocumentBlock Note(string text) => new(DocumentBlockKind.Note, text);

    public static DocumentBlock Picture(byte[] image, string? caption) =>
        new(DocumentBlockKind.Picture, caption ?? string.Empty, null, image);
}

/// <summary>
/// Writes a word-processor document from blocks.
/// </summary>
/// <remarks>
/// The format engine sits behind this so a surface that wants a document describes its parts and
/// never learns how one is built — the same seam the spreadsheet writer sits behind, for the same
/// reason.
/// </remarks>
public interface IDocumentWriter
{
    /// <summary>The media type the bytes should be served as.</summary>
    string ContentType { get; }

    /// <summary>The file extension, without a leading dot.</summary>
    string Extension { get; }

    /// <summary>Builds the document, whole, in memory.</summary>
    byte[] Write(IReadOnlyList<DocumentBlock> blocks);
}
