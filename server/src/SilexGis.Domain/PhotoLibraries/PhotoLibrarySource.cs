// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.PhotoLibraries;

/// <summary>
/// Which neighbouring photo library a photograph came from. A library is a separate product with
/// its own database, its own storage and its own accounts; this application reads from it and
/// files nothing into it, and nothing moves between one library and another.
/// </summary>
public enum PhotoLibrarySource
{
    /// <summary>A neighbouring Immich instance.</summary>
    Immich = 1,

    /// <summary>A neighbouring PhotoPrism instance.</summary>
    PhotoPrism = 2,
}

/// <summary>
/// Which rendering of a photograph is wanted. Named for what it is for rather than for any one
/// product's vocabulary, because the two products name their renderings differently and neither
/// name would mean anything at the call site.
/// </summary>
public enum LibraryThumbnailSize
{
    /// <summary>A hover card or a list row.</summary>
    Small = 0,

    /// <summary>The picture in a map balloon.</summary>
    Large = 1,
}

/// <summary>
/// What a library says a photograph is. Three-valued in use — see the kind field on a photograph
/// — because "the library did not say" is a real and common answer that must not be read as
/// "image".
/// </summary>
public enum LibraryPhotoKind
{
    Image = 0,
    Video = 1,
}

/// <summary>
/// Who may see the neighbouring photo libraries through this application.
///
/// <para>
/// Two values, because there are two answers worth having and no cheap way to have a third: the
/// products on the other side either bind a credential to exactly one of their own accounts with
/// no scoping at all, or authorise their pictures by the address alone with no roles applying — so
/// nothing this application decides could reflect a per-person answer from over there without
/// holding one credential per person.
/// </para>
/// </summary>
public enum PhotoLibraryAudience
{
    /// <summary>
    /// Full administrators only. The shipped value: the photographs on the other side are governed
    /// by that side's rules and not by this application's, so opening them to every account is a
    /// decision an operator has to make rather than one they inherit.
    /// </summary>
    Administrators = 0,

    /// <summary>Every signed-in account.</summary>
    SignedIn = 1,
}
