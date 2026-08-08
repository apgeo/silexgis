// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.RegularExpressions;

namespace SilexGis.Domain.Import;

/// <summary>
/// What to call the object a photograph becomes, before anybody types anything.
///
/// <para>
/// A file name is a name only sometimes. <c>Pestera Ursilor.jpg</c> is one and should be
/// offered; <c>IMG_2043.JPG</c>, <c>DSC01234.jpg</c> and <c>20260808_114233.jpg</c> are what a
/// camera calls a file, and proposing them would fill the registry with objects named after
/// somebody's shutter count. When in doubt this proposes nothing, because an empty name a
/// reviewer fills in is better than a wrong one they have to notice first.
/// </para>
/// </summary>
public static partial class PhotoNameProposal
{
    /// <summary>
    /// A name for the object these pictures become, or null when none of the file names is one.
    /// The first picture that offers a usable name wins, which — since the pictures arrive in
    /// capture order — is the earliest one somebody bothered to rename.
    /// </summary>
    public static string? From(IEnumerable<string?> fileNames) =>
        fileNames.Select(From).FirstOrDefault(name => name is not null);

    /// <summary>A name from one file name, or null when the camera chose that file name.</summary>
    public static string? From(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var stem = StripExtension(fileName).Trim();

        // A trailing counter is how a phone distinguishes three shots of one thing; the name in
        // front of it is still the name. "Ursilor (2)" and "Ursilor-3" are both "Ursilor".
        stem = TrailingCounter().Replace(stem, string.Empty).Trim(' ', '-', '_');

        if (stem.Length is 0 or > 100 || CameraName().IsMatch(stem) || !stem.Any(char.IsLetter))
        {
            return null;
        }

        // A name that is mostly digits with a letter or two glued on is still a camera's.
        return stem.Count(char.IsLetter) < 2 ? null : stem;
    }

    private static string StripExtension(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        return dot > 0 ? fileName[..dot] : fileName;
    }

    /// <summary>
    /// The shapes cameras and phones actually write: a two-to-four letter prefix and a number,
    /// or a date and a time. Anchored on both ends, so a real name that merely contains digits
    /// is left alone.
    /// </summary>
    [GeneratedRegex(
        @"^(?:[A-Za-z]{1,4}[ _-]?\d{2,}|\d{6,8}[ _-]?\d{0,6}|(?:photo|image|picture|foto|poza)[ _-]?\d*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CameraName();

    [GeneratedRegex(@"[ _-]*\((\d{1,3})\)$|[ _-]\d{1,3}$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingCounter();
}
