// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Infrastructure.Documents.Conversion;

/// <summary>
/// The optional document-conversion service. Off by default, and an installation that leaves it
/// off is a supported installation rather than a broken one — laying out an office document
/// needs an office suite, which is a service of its own and a great deal more machinery than a
/// small site should have to run.
/// </summary>
public sealed class ConversionOptions
{
    public const string SectionName = "Conversion";

    /// <summary>
    /// Whether a converter is deployed. Off unless the operator says otherwise, so a stock
    /// installation never queues work for a service that is not there.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Base address of the conversion service, reached over the internal network. Empty is the
    /// same as disabled: half a configuration is not a converter.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// How long one conversion may take. Laying out a long document is slow, and a request cut
    /// short would be recorded as a failure of the document, so this is generous rather than
    /// tight. Clamped when used, so a nonsensical value cannot disable the timeout.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 120;
}
