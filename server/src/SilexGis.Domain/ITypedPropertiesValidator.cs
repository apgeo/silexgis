// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain;

/// <summary>
/// Validates a jsonb property bag against the JSON Schema its kind publishes. One seam for
/// every typed-property mechanism in the system — feature properties keyed by feature type,
/// document metadata keyed by document type — because they are the same knowledge and a
/// second implementation would drift from the first.
/// <para>
/// Implemented in Infrastructure: the schema engine is a dependency Domain must not carry.
/// </para>
/// </summary>
public interface ITypedPropertiesValidator
{
    /// <summary>
    /// Returns validation errors ("<c>/path: message</c>"), empty when valid. Throws
    /// nothing on malformed schema — a broken stored schema reports as an error entry
    /// so writes fail closed with a diagnosable message.
    /// </summary>
    IReadOnlyList<string> Validate(string schemaJson, string propertiesJson);

    /// <summary>
    /// Returns errors describing why <paramref name="schemaJson"/> is not usable as a
    /// schema, empty when it is. Callers that accept a schema from a user check it here
    /// before storing it, so a schema that would reject every write is refused at the
    /// point it is written rather than discovered by the next person to save a document.
    /// </summary>
    IReadOnlyList<string> ValidateSchema(string schemaJson);
}
