// SPDX-License-Identifier: AGPL-3.0-or-later
using NPOI.POIFS.FileSystem;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// The pre-2007 Office container, built here rather than committed as a binary fixture.
/// <para>
/// What these samples can honestly prove is which stream a container holds and what the head of
/// that stream declares about itself — which is exactly what the reader dispatches and refuses
/// on. They are deliberately not documents: nothing follows the header but padding, so a sample
/// that reaches the converter fails there, and a test wanting the converter's own answer to a
/// real report needs a real report and cannot be written from here.
/// </para>
/// </summary>
internal static class CompoundFileSamples
{
    /// <summary>The format version a Word 6 or Word 95 document declares — older than any
    /// converter here reaches.</summary>
    public const ushort Word6Version = 0x0065;

    /// <summary>The format version a Word 97 document declares.</summary>
    public const ushort Word97Version = 0x00C1;

    /// <summary>The name of a word-processor document's body stream.</summary>
    public const string WordStream = "WordDocument";

    /// <summary>The name of a presentation's body stream.</summary>
    public const string PresentationStream = "PowerPoint Document";

    /// <summary>The name of the stream a presentation records its current revision in.</summary>
    public const string CurrentUserStream = "Current User";

    /// <summary>A compound file holding exactly the named streams at its root.</summary>
    public static byte[] CompoundFile(params (string Name, byte[] Content)[] streams)
    {
        ArgumentNullException.ThrowIfNull(streams);

        var compound = new POIFSFileSystem();
        foreach (var (name, content) in streams)
        {
            using var payload = new MemoryStream(content);
            compound.Root.CreateDocument(name, payload);
        }

        var buffer = new MemoryStream();
        compound.WriteFileSystem(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// The opening bytes of a word-processor document's body stream: the identifier every such
    /// document starts with, the format version, and the sixteen-bit flag field at offset ten
    /// whose 0x0100 bit says the body is locked.
    /// </summary>
    public static byte[] WordDocumentStream(ushort version, bool encrypted)
    {
        const ushort wordIdentifier = 0xA5EC;
        const ushort encryptedFlag = 0x0100;

        // Generously longer than the header itself: the version is checked only after the whole
        // header has been read, so a stream that stops short of it fails as a truncated file
        // before the question this builds the sample to ask is ever reached.
        var header = new byte[8192];
        BitConverter.TryWriteBytes(header.AsSpan(0), wordIdentifier);
        BitConverter.TryWriteBytes(header.AsSpan(2), version);
        BitConverter.TryWriteBytes(header.AsSpan(10), (ushort)(encrypted ? encryptedFlag : 0));
        return header;
    }

    /// <summary>
    /// The record a presentation keeps its current revision in. Its declared size is fixed by the
    /// format, and the token after it is one of exactly two documented values — one saying the
    /// file is encrypted and one saying it is not.
    /// </summary>
    public static byte[] CurrentUser(bool encrypted)
    {
        const uint declaredSize = 0x00000014;
        const uint encryptedToken = 0xF3D1C4DF;
        const uint plainToken = 0xE391C05F;

        var record = new byte[32];
        BitConverter.TryWriteBytes(record.AsSpan(8), declaredSize);
        BitConverter.TryWriteBytes(record.AsSpan(12), encrypted ? encryptedToken : plainToken);
        return record;
    }
}
