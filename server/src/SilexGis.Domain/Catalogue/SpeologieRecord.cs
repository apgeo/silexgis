// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Catalogue;

/// <summary>
/// One cave as the Romanian community catalogue describes it — every field that catalogue's
/// interface exposes, under the names it uses, unaltered.
///
/// <para>
/// The Romanian names are kept deliberately. This record is the boundary where a foreign
/// vocabulary is still foreign: translating <c>denNegativa</c> into a local name here would
/// mean two places had an opinion about what it means, and the one further from the source
/// would be the one people read. The translating is done once, visibly, in
/// <see cref="SpeologieMapping"/>.
/// </para>
/// <para>
/// Every field but <see cref="Id"/> and <see cref="Title"/> is nullable because the catalogue
/// declares them so, and the measured fill rates bear it out: a county is present on about two
/// thirds of rows and a protection class on about one in eight. Absence is the normal case, not
/// an error to report.
/// </para>
/// </summary>
/// <param name="Id">The catalogue's own numeric identifier. Stable, and the key an import recognises a cave by.</param>
/// <param name="Title">The cave's name as the catalogue writes it, diacritics and all — or without them, since the catalogue is not consistent about that.</param>
/// <param name="Slug">The catalogue's URL segment, which is also how a person reaches the public page. Occasionally a cave code (<c>V5</c>) rather than a slugged name.</param>
/// <param name="Descriere">A free-text description, as HTML. Usually short; occasionally enormous. Never trusted, never stored as markup.</param>
/// <param name="Judet">Two-letter Romanian county code (<c>BH</c>, <c>GJ</c>). Not a county name — the catalogue's own documentation is wrong about this.</param>
/// <param name="Localitate">Nearest locality, free text.</param>
/// <param name="Munte">Mountain range, as a lowercase slug (<c>craiului</c>, <c>godeanu</c>).</param>
/// <param name="Lungime">Surveyed length in metres.</param>
/// <param name="Denivelare">Total vertical range in metres.</param>
/// <param name="DenNegativa">The downward part of the vertical range, in metres. The catalogue's sign convention is not consistent.</param>
/// <param name="Altitudine">Entrance altitude in metres.</param>
/// <param name="NrHidro">Hydrologic number within the basin, as text.</param>
/// <param name="BazinHidroId">Identifier of the hydrographic basin in a table the interface does not expose.</param>
/// <param name="Roca">Rock code (<c>00</c>, <c>05</c>, <c>06</c>). The catalogue publishes no legend for it.</param>
/// <param name="Scufundabila">Whether the cave holds a sump, carried as the string <c>"0"</c> or <c>"1"</c>.</param>
/// <param name="Clasificare">Statutory protection class, as <c>clasaA</c> / <c>clasaB</c> / <c>clasaC</c>.</param>
/// <param name="Stiinta">Scientific interest, as a comma-separated list with inconsistent spacing.</param>
/// <param name="Disparuta">Whether the cave is recorded as destroyed or lost.</param>
/// <param name="CodAp">Protected-area code, e.g. <c>2.613</c>.</param>
public sealed record SpeologieRecord(
    int Id,
    string Title,
    string? Slug = null,
    string? Descriere = null,
    string? Judet = null,
    string? Localitate = null,
    string? Munte = null,
    double? Lungime = null,
    double? Denivelare = null,
    double? DenNegativa = null,
    double? Altitudine = null,
    string? NrHidro = null,
    int? BazinHidroId = null,
    string? Roca = null,
    string? Scufundabila = null,
    string? Clasificare = null,
    string? Stiinta = null,
    bool? Disparuta = null,
    string? CodAp = null)
{
    /// <summary>
    /// The public page for this cave. The catalogue serves cave pages from the site root, so the
    /// slug is the whole path; a record with no slug has no public page and gets no link rather
    /// than a guessed one.
    /// </summary>
    public string? PublicUrl => string.IsNullOrWhiteSpace(Slug) ? null : $"https://www.speologie.org/{Slug}";
}
