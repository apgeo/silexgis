// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SilexGis.Domain.Documents;

/// <summary>
/// What a photograph says about how it was taken: the camera, the lens, and the exposure.
///
/// <para>
/// This is the panel shown beside a picture and nothing else — it holds no position. That is
/// deliberate and it is the whole reason this record is separate from the position columns
/// beside it: where a photograph was taken is protected, and how it was taken is not, so a
/// response that may show one and not the other must be able to carry them apart.
/// </para>
/// <para>
/// Stored in the file's metadata document rather than in columns because nothing queries it.
/// Every member is optional: a picture from a scanner states none of this, and one that states
/// nothing is not an error.
/// </para>
/// </summary>
public sealed record PhotoExif(
    string? CameraMake = null,
    string? CameraModel = null,
    string? Lens = null,

    /// <summary>The EXIF orientation code, 1–8: which way up the picture was recorded.</summary>
    int? Orientation = null,

    /// <summary>Shutter time as the file states it, in seconds.</summary>
    double? ExposureSeconds = null,

    /// <summary>Aperture as an f-number.</summary>
    double? FNumber = null,

    int? Iso = null,

    /// <summary>Focal length in millimetres, as recorded rather than as an equivalent.</summary>
    double? FocalLengthMm = null,

    int? WidthPixels = null,

    int? HeightPixels = null)
{
    /// <summary>A picture that stated nothing about itself, or one nothing could read.</summary>
    public static readonly PhotoExif None = new();

    /// <summary>
    /// Whether the picture stated nothing about itself.
    /// </summary>
    /// <remarks>
    /// Never written down. It is derived from the other fields, and this record is serialised into
    /// the stored metadata — so writing it would put a value in storage that can disagree with what
    /// it was derived from the moment either changes. Rows written before this carry the key; it is
    /// read-only, so it is ignored on the way back in.
    /// </remarks>
    [JsonIgnore]
    public bool IsEmpty => this == None;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// The record as the file's metadata document holds it, under its own key. A key rather
    /// than the whole document because the bag is shared: a raster's layer information and a
    /// picture's camera live in the same column and must not overwrite one another.
    /// </summary>
    public const string MetadataKey = "photo";

    /// <summary>
    /// This record merged into an existing metadata document. Everything already in the
    /// document under another key is kept — the bag belongs to the file, not to this reader.
    /// </summary>
    public string IntoMetadata(string? existingJson)
    {
        var document = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(existingJson))
        {
            try
            {
                using var parsed = JsonDocument.Parse(existingJson);
                if (parsed.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in parsed.RootElement.EnumerateObject())
                    {
                        document[property.Name] = property.Value.Clone();
                    }
                }
            }
            catch (JsonException)
            {
                // A bag nothing can read is replaced rather than allowed to refuse the write:
                // this is descriptive metadata, and losing an unreadable copy of it costs less
                // than failing an upload over it.
            }
        }

        if (IsEmpty)
        {
            document.Remove(MetadataKey);
        }
        else
        {
            document[MetadataKey] = JsonSerializer.SerializeToElement(this, SerializerOptions);
        }

        return JsonSerializer.Serialize(document, SerializerOptions);
    }

    /// <summary>What a file's metadata document says about the camera, or nothing.</summary>
    public static PhotoExif FromMetadata(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return None;
        }

        try
        {
            using var parsed = JsonDocument.Parse(json);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object
                || !parsed.RootElement.TryGetProperty(MetadataKey, out var photo))
            {
                return None;
            }

            return photo.Deserialize<PhotoExif>(SerializerOptions) ?? None;
        }
        catch (JsonException)
        {
            return None;
        }
    }
}
