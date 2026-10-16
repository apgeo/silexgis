// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;

namespace SilexGis.Domain.Filters;

/// <summary>
/// A filter document as the one opaque token a link and a list request carry.
/// </summary>
/// <remarks>
/// <para>
/// Base64url over the document's JSON, unpadded — the same spelling the browser writes into its
/// address bar, so a list, an export and a pasted link all carry the identical string and a file
/// can be asked for with exactly the filter the screen above it was drawn with. The two sides agree
/// through a literal token pinned in a test on each, not through the generated contract, which
/// sees only a string.
/// </para>
/// <para>
/// A token that cannot be read is answered as unreadable, never as "everything": a request that
/// narrowed to nothing because a chat client truncated its link would otherwise export the whole
/// registry while claiming to export a selection.
/// </para>
/// </remarks>
public static class FilterToken
{
    /// <summary>
    /// The longest token read at all. A document at every validation ceiling is well under this;
    /// past it the string is not a filter somebody built, and decoding it would be work done for an
    /// attacker rather than for a reader.
    /// </summary>
    public const int MaxLength = 32 * 1024;

    public static string Encode(FilterDocument document)
    {
        var bytes = Encoding.UTF8.GetBytes(FilterDocument.Serialize(document));
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>The document a token holds, or null when it holds nothing this version can read.</summary>
    public static FilterDocument? Decode(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > MaxLength)
        {
            return null;
        }

        var base64 = token.Replace('-', '+').Replace('_', '/');
        base64 += (base64.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            _ => string.Empty,
        };

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return null;
        }

        string json;
        try
        {
            json = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }

        return FilterDocument.Deserialize(json);
    }
}
