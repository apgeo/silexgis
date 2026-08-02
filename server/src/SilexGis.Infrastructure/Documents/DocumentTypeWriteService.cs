// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Metadata;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Documents;

/// <summary>What a caller asks a document type to become.</summary>
public sealed record DocumentTypeInput(
    string Code, string Name, string? Description, int SortOrder, string? MetadataSchema);

/// <summary>
/// The single mutator of a document type's metadata schema. A schema is not a plain column:
/// changing it has to move a version number and record what the previous text was, because
/// documents stamp the version they were validated against and are re-checked against that
/// version rather than against whatever the schema became afterwards. Doing that in one
/// place is what keeps the stamp meaningful.
/// </summary>
public sealed class DocumentTypeWriteService(SilexGisDbContext db, ITypedPropertiesValidator validator)
{
    public const string InvalidSchemaCode = "document_type.schema_invalid";
    public const string CodeTakenCode = "document_type.code_taken";
    public const string NotFoundCode = "document_type.not_found";

    /// <summary>
    /// Adds a kind of document, together with its first published schema. Owns its
    /// transaction: the type's identity is assigned by the database, so the schema row that
    /// references it cannot be written in the same statement, and a type whose schema row
    /// never landed would carry a version stamp nothing can resolve.
    /// </summary>
    /// <exception cref="DocumentWriteException">
    /// <c>document_type.code_taken</c> when the code is already used;
    /// <c>document_type.schema_invalid</c> when the supplied schema is not usable.
    /// </exception>
    public async Task<DocumentType> CreateAsync(DocumentTypeInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (await db.DocumentTypes.AsNoTracking().AnyAsync(t => t.Code == input.Code, ct))
        {
            throw new DocumentWriteException(CodeTakenCode, "Another document type already uses this code.");
        }

        var schema = Normalize(input.MetadataSchema);
        RejectUnusableSchema(schema);

        var type = new DocumentType
        {
            Code = input.Code,
            Name = input.Name,
            Description = input.Description,
            SortOrder = input.SortOrder,
            MetadataSchema = schema,
            MetadataSchemaVersion = DocumentType.FirstSchemaVersion,
        };

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.DocumentTypes.Add(type);
        await db.SaveChangesAsync(ct);
        Publish(type, schema);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return type;
    }

    /// <summary>
    /// Applies <paramref name="input"/> to an existing kind. The schema version moves only
    /// when the schema's *meaning* changes — reformatting or reordering keys is not a new
    /// version, because every document stamped with the old one would then be marked stale
    /// for nothing.
    /// </summary>
    /// <exception cref="DocumentWriteException">
    /// <c>document_type.not_found</c>, <c>document_type.code_taken</c>,
    /// <c>document_type.schema_invalid</c>.
    /// </exception>
    public async Task<DocumentType> UpdateAsync(long id, DocumentTypeInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var type = await db.DocumentTypes.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new DocumentWriteException(NotFoundCode, "The document type no longer exists.");

        if (await db.DocumentTypes.AsNoTracking().AnyAsync(t => t.Code == input.Code && t.Id != id, ct))
        {
            throw new DocumentWriteException(CodeTakenCode, "Another document type already uses this code.");
        }

        var schema = Normalize(input.MetadataSchema);
        RejectUnusableSchema(schema);

        type.Code = input.Code;
        type.Name = input.Name;
        type.Description = input.Description;
        type.SortOrder = input.SortOrder;

        if (JsonCanonical.Canonicalize(type.MetadataSchema) != JsonCanonical.Canonicalize(schema))
        {
            type.MetadataSchema = schema;
            type.MetadataSchemaVersion++;
            Publish(type, schema);
        }

        await db.SaveChangesAsync(ct);
        return type;
    }

    /// <summary>
    /// Records the current schema as a published version. A kind with no schema publishes
    /// nothing: there is no text for a document to have been validated against, and the
    /// stamp such a document carries is null.
    /// </summary>
    internal void Publish(DocumentType type, string? schema)
    {
        if (schema is null)
        {
            return;
        }

        db.DocumentTypeSchemas.Add(new DocumentTypeSchema
        {
            DocumentTypeId = type.Id,
            Version = type.MetadataSchemaVersion,
            Schema = schema,
        });
    }

    private void RejectUnusableSchema(string? schema)
    {
        if (schema is null)
        {
            return;
        }

        var errors = validator.ValidateSchema(schema);
        if (errors.Count > 0)
        {
            throw new DocumentWriteException(InvalidSchemaCode, string.Join(" ", errors));
        }
    }

    /// <summary>Blank is the same as absent: a kind either has a schema or has none.</summary>
    private static string? Normalize(string? schema) =>
        string.IsNullOrWhiteSpace(schema) ? null : schema.Trim();
}
