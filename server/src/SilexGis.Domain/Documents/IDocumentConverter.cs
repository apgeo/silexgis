// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Documents;

/// <summary>
/// Turns an office-suite document into a portable one, so that its pages can be drawn.
/// <para>
/// Optional by design. Laying out a word-processor document needs a whole office suite, which
/// is far too much to put inside an application that mostly stores caves — so it lives in a
/// service of its own that an installation runs only if it wants this, and a small installation
/// is expected not to. That is why <see cref="IsConfigured"/> exists: every caller has to be
/// able to tell "there is no converter here" apart from "the converter failed", because those
/// are different sentences to show a person.
/// </para>
/// </summary>
public interface IDocumentConverter
{
    /// <summary>
    /// Whether this installation has a converter to ask at all. False is the ordinary state,
    /// not an error, and nothing is ever queued for conversion while it is false.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Converts the given bytes to a portable document, writing the result to
    /// <paramref name="destination"/>. The source stream is never modified — a conversion
    /// produces a copy beside the original and the stored upload stays exactly as it arrived.
    /// </summary>
    /// <param name="originalName">
    /// The uploaded file's name. Converters decide which reader to use from the extension, so
    /// this is content, not decoration.
    /// </param>
    /// <exception cref="DocumentConversionException">
    /// The converter answered and could not produce a copy of these bytes.
    /// </exception>
    /// <exception cref="InvalidOperationException">No converter is configured.</exception>
    Task ConvertToPortableAsync(
        Stream source, string originalName, Stream destination, CancellationToken ct = default);
}

/// <summary>
/// The converter was reached and refused these bytes. Distinct from an unreachable or absent
/// converter, which is not a fact about the document and must not be recorded as one.
/// </summary>
public sealed class DocumentConversionException(string message, Exception? inner = null)
    : Exception(message, inner);
