// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Infrastructure.Metadata;

namespace SilexGis.Api.Tests;

/// <summary>
/// The one validator behind every typed-property mechanism — feature properties, document details,
/// trip sections — answers an unusable schema with errors and never with an exception, so a write
/// that carries one is refused with a reason rather than failing as a server error.
/// </summary>
public sealed class JsonSchemaPropertiesValidatorTests
{
    private readonly JsonSchemaPropertiesValidator validator = new();

    [Theory]
    [InlineData("""{"type":"nonsense"}""")]
    [InlineData("""{"type":"object","properties":{"n":{"type":"integer","minimum":"high"}}}""")]
    [InlineData("""not json""")]
    public void An_unusable_schema_is_reported_not_thrown(string schema)
    {
        validator.ValidateSchema(schema).ShouldNotBeEmpty();
        validator.Validate(schema, "{}").ShouldNotBeEmpty();
    }

    [Fact]
    public void A_usable_schema_passes_and_still_judges_what_it_is_given()
    {
        const string schema = """{"type":"object","properties":{"n":{"type":"integer"}}}""";

        validator.ValidateSchema(schema).ShouldBeEmpty();
        validator.Validate(schema, """{"n":3}""").ShouldBeEmpty();
        validator.Validate(schema, """{"n":"three"}""").ShouldNotBeEmpty();
    }
}
