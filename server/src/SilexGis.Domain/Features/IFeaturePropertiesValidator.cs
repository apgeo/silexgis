// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Features;

/// <summary>
/// Validates a feature's jsonb properties document against its kind's JSON Schema.
/// Implemented in Infrastructure (the schema engine is a dependency Domain must not
/// carry); consumed by the feature write path, which stamps the kind's current
/// properties-schema version on success. A kind without a schema accepts any JSON
/// object.
/// </summary>
public interface IFeaturePropertiesValidator
{
    /// <summary>
    /// Returns validation errors ("<c>/path: message</c>"), empty when valid. Throws
    /// nothing on malformed schema — a broken stored schema reports as an error entry
    /// so writes fail closed with a diagnosable message.
    /// </summary>
    IReadOnlyList<string> Validate(string schemaJson, string propertiesJson);
}
