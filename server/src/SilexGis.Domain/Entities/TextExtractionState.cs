// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// How far reading a stored file's text layer has got. Stored as smallint and append-only:
/// the values are part of the schema contract, so a new member takes the next free number
/// and nothing is ever renumbered or reused.
/// <para>
/// The distinction that earns this column is between "nothing has read this yet" and "this
/// was read and there is nothing in it". Both leave a file with no text, and a reader that
/// cannot tell them apart makes the interface guess: it either promises text that will never
/// arrive, or reports emptiness before anything has looked. A scanned page is the ordinary
/// case of the second — it is a photograph in a paged wrapper, and no amount of waiting will
/// produce words from it.
/// </para>
/// </summary>
public enum TextExtractionState : short
{
    /// <summary>
    /// The format holds no text layer at all — a photograph, a raster, a recording, a mesh.
    /// Nothing was queued and nothing ever will be; this is the resting state, not a delay.
    /// </summary>
    NotApplicable = 0,

    /// <summary>Queued to be read, and not read yet.</summary>
    Pending = 1,

    /// <summary>Read, and text was found. The page rows carry it.</summary>
    Extracted = 2,

    /// <summary>
    /// Read in full, and the file carries no words — an image-only PDF, an empty document.
    /// The pages exist and are numbered; there is simply nothing on them to read.
    /// </summary>
    NoText = 3,

    /// <summary>
    /// Reading was attempted and could not finish: the bytes are damaged, encrypted, or not
    /// what the recorded format says they are. Unlike <see cref="NoText"/> this is worth
    /// trying again, because the answer may change without the file changing.
    /// </summary>
    Failed = 4,

    /// <summary>
    /// The format is one that carries text, but nothing here knows how to read it. Queuing
    /// it was right and finding no reader is the honest outcome — distinct from a failure,
    /// because nothing is wrong with the file.
    /// </summary>
    Unsupported = 5,
}
