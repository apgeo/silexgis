// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using System.Text.Json;
using Shouldly;
using SilexGis.Domain.Export;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The interchange document says what it is allowed to say, in terms somebody else
/// published, and admits what it withheld.
/// </summary>
public class KarstLinkDocumentTests
{
    private static readonly Guid CaveId = Guid.Parse("0199a000-0000-7000-8000-000000000001");
    private static readonly Guid ExportId = Guid.Parse("0199a000-0000-7000-8000-0000000000ff");
    private static readonly DateTimeOffset Created = new(2026, 9, 9, 10, 30, 0, TimeSpan.Zero);

    /// <summary>
    /// Every vocabulary a term in this document may come from. Nothing else may appear as an
    /// IRI anywhere in the file.
    /// </summary>
    private static readonly string[] AllowedNamespaces =
    [
        "https://ontology.uis-speleo.org/ontology/#",
        "http://www.w3.org/2000/01/rdf-schema#",
        "http://purl.org/dc/terms/",
        "http://rs.tdwg.org/dwc/terms/",
        "http://www.w3.org/2003/01/geo/wgs84_pos#",
        "http://www.geonames.org/ontology#",
        "http://schema.org/",
        "http://www.w3.org/2001/XMLSchema#",
        "urn:silexgis:cave-export#",
    ];

    /// <summary>
    /// The terms of the published cave and karst ontology this writer uses, spelled out. Any
    /// other name under that namespace appearing in a document would be one invented here and
    /// dressed up as a published one — which is worse than an absent term, because a consumer
    /// binds to it and nothing else in the world agrees.
    /// </summary>
    private static readonly string[] PublishedCaveTerms =
    [
        "https://ontology.uis-speleo.org/ontology/#UndergroundCavity",
        "https://ontology.uis-speleo.org/ontology/#length",
        "https://ontology.uis-speleo.org/ontology/#extentBelowEntrance",
    ];

    private static JsonElement Parse(byte[] bytes) =>
        JsonDocument.Parse(Encoding.UTF8.GetString(bytes)).RootElement.Clone();

    private static KarstLinkCave Cave(
        double? lat = null,
        double? lon = null,
        double? precision = null,
        ProtectedPositionTreatment? treatment = null,
        string? sameAs = null) =>
        new(
            CaveId,
            "Sample Cave",
            "SAMPLE-1",
            "Sample Hole",
            "Sample Region",
            lat,
            lon,
            720,
            1234.5m,
            88.5m,
            precision,
            treatment,
            sameAs);

    [Fact]
    public void Context_is_exactly_the_published_bindings_and_nothing_else()
    {
        var document = Parse(KarstLinkDocument.Write([Cave(45.0, 25.0)], 0, 5000, Created, ExportId));
        var context = document.GetProperty("@context");

        // Golden: the whole vocabulary a recipient has to resolve, term by term. A term added
        // to the writer without being added here is a term nobody agreed to.
        var expected = new Dictionary<string, string>
        {
            ["karstlink"] = "https://ontology.uis-speleo.org/ontology/#",
            ["rdfs"] = "http://www.w3.org/2000/01/rdf-schema#",
            ["dct"] = "http://purl.org/dc/terms/",
            ["dwc"] = "http://rs.tdwg.org/dwc/terms/",
            ["geo"] = "http://www.w3.org/2003/01/geo/wgs84_pos#",
            ["gn"] = "http://www.geonames.org/ontology#",
            ["schema"] = "http://schema.org/",
            ["xsd"] = "http://www.w3.org/2001/XMLSchema#",
            ["silexgis"] = "urn:silexgis:cave-export#",
            ["UndergroundCavity"] = "karstlink:UndergroundCavity",
            ["name"] = "rdfs:label",
            ["identifier"] = "dct:identifier",
            ["alternateName"] = "gn:alternateName",
            ["region"] = "schema:addressRegion",
            ["latitude"] = "geo:lat",
            ["longitude"] = "geo:long",
            ["altitude"] = "geo:alt",
            ["length"] = "karstlink:length",
            ["depth"] = "karstlink:extentBelowEntrance",
            ["sameAs"] = "schema:sameAs",
            ["description"] = "dct:description",
            ["provenance"] = "dct:provenance",
            ["CaveExport"] = "silexgis:CaveExport",
            ["positionTreatment"] = "silexgis:positionTreatment",
            ["caveCount"] = "silexgis:caveCount",
            ["omittedCaveCount"] = "silexgis:omittedCaveCount",
            ["locationProtectionGridMeters"] = "silexgis:locationProtectionGridMeters",
        };

        var simple = context.EnumerateObject()
            .Where(p => p.Value.ValueKind == JsonValueKind.String)
            .ToDictionary(p => p.Name, p => p.Value.GetString()!);
        simple.ShouldBe(expected, ignoreOrder: true);

        // The two terms whose literal type is stated rather than inferred.
        context.GetProperty("created").GetProperty("@id").GetString().ShouldBe("dct:created");
        context.GetProperty("coordinatePrecision").GetProperty("@id").GetString()
            .ShouldBe("dwc:coordinatePrecision");

        // No default vocabulary: an unmapped key means nothing rather than silently becoming a
        // term. That is what makes "this file coins no vocabulary" a checkable property.
        context.TryGetProperty("@vocab", out _).ShouldBeFalse();
    }

    /// <summary>
    /// The same cave in somebody else's register, written under a term the whole web already
    /// means the same thing by — the only way two files about the same caves can be joined,
    /// since a name is not stable and this file's own identifiers mean nothing elsewhere.
    /// </summary>
    [Fact]
    public void A_cave_carries_its_identity_in_another_register_when_one_is_known()
    {
        var linked = Parse(KarstLinkDocument.Write(
            [Cave(45.0, 25.0, sameAs: "https://www.grottocenter.org/ui/entry/77")],
            0,
            5000,
            Created,
            ExportId));

        linked.GetProperty("@graph").EnumerateArray().Single()
            .GetProperty("sameAs").GetString().ShouldBe("https://www.grottocenter.org/ui/entry/77");

        // And nothing is written where nothing is known, rather than an empty string a consumer
        // would try to resolve.
        var unlinked = Parse(KarstLinkDocument.Write([Cave(45.0, 25.0)], 0, 5000, Created, ExportId));
        unlinked.GetProperty("@graph").EnumerateArray().Single()
            .TryGetProperty("sameAs", out _).ShouldBeFalse();
    }

    [Fact]
    public void No_term_is_invented_locally_and_dressed_as_a_published_one()
    {
        var bytes = KarstLinkDocument.Write(
            [Cave(45.0, 25.0, 5000, ProtectedPositionTreatment.GridPosition)], 2, 5000, Created, ExportId);
        var text = Encoding.UTF8.GetString(bytes);
        var document = Parse(bytes);

        // Everything in the context resolves inside a namespace somebody else publishes, or
        // inside the inert URN this installation uses for statements about its own file.
        foreach (var binding in document.GetProperty("@context").EnumerateObject())
        {
            var iri = binding.Value.ValueKind == JsonValueKind.String
                ? binding.Value.GetString()!
                : binding.Value.GetProperty("@id").GetString()!;

            var resolved = Resolve(iri, document.GetProperty("@context"));

            // A prefix declaration names a namespace rather than a term in one; the term
            // check below is about the names hung off it.
            if (AllowedNamespaces.Contains(resolved))
            {
                continue;
            }

            AllowedNamespaces.ShouldContain(
                ns => resolved.StartsWith(ns, StringComparison.Ordinal),
                $"'{binding.Name}' resolves to '{resolved}', which is in no vocabulary this writer may use");

            if (resolved.StartsWith("https://ontology.uis-speleo.org/ontology/#", StringComparison.Ordinal))
            {
                PublishedCaveTerms.ShouldContain(
                    resolved,
                    $"'{binding.Name}' names '{resolved}' in the published cave and karst ontology, "
                    + "which does not define it");
            }
        }

        // And nothing anywhere else in the bytes reaches into that namespace either — not in a
        // node, not in a value, not in a string somebody concatenated.
        var occurrences = text.Split("https://ontology.uis-speleo.org/").Length - 1;
        occurrences.ShouldBe(1, "the published ontology namespace appears outside the context");
    }

    private static string Resolve(string iri, JsonElement context)
    {
        var colon = iri.IndexOf(':');
        if (colon <= 0)
        {
            return iri;
        }

        var prefix = iri[..colon];
        if (context.TryGetProperty(prefix, out var bound) && bound.ValueKind == JsonValueKind.String)
        {
            var expansion = bound.GetString()!;
            if (expansion != iri)
            {
                return expansion + iri[(colon + 1)..];
            }
        }

        return iri;
    }

    [Fact]
    public void A_file_that_left_caves_out_says_so_in_numbers_and_in_words()
    {
        var document = Parse(KarstLinkDocument.Write([Cave(45.0, 25.0)], 3, 5000, Created, ExportId));

        document.GetProperty("caveCount").GetInt32().ShouldBe(1);
        document.GetProperty("omittedCaveCount").GetInt32().ShouldBe(3);

        var provenance = document.GetProperty("provenance").GetString()!;
        provenance.ShouldContain("3 further cave(s)");
        provenance.ShouldContain("left out");
    }

    [Fact]
    public void A_file_that_left_nothing_out_says_that_too()
    {
        var document = Parse(KarstLinkDocument.Write([Cave(45.0, 25.0)], 0, 5000, Created, ExportId));

        document.GetProperty("omittedCaveCount").GetInt32().ShouldBe(0);
        document.GetProperty("provenance").GetString()!.ShouldContain("No cave was left out");
    }

    [Fact]
    public void A_coarsened_position_carries_how_far_it_may_be_from_the_real_one()
    {
        var document = Parse(KarstLinkDocument.Write(
            [Cave(45.0, 25.0, 5000, ProtectedPositionTreatment.GridPosition)], 0, 5000, Created, ExportId));

        var node = document.GetProperty("@graph")[0];
        node.GetProperty("coordinatePrecision").GetDouble().ShouldBe(5000);
        node.GetProperty("positionTreatment").GetString().ShouldBe("grid_position");
        document.GetProperty("locationProtectionGridMeters").GetDouble().ShouldBe(5000);
        document.GetProperty("provenance").GetString()!.ShouldContain("grid");
    }

    [Fact]
    public void A_cave_with_no_position_carries_no_coordinate_and_says_which_treatment_it_got()
    {
        var document = Parse(KarstLinkDocument.Write(
            [Cave(treatment: ProtectedPositionTreatment.NoPosition)], 0, 5000, Created, ExportId));

        var node = document.GetProperty("@graph")[0];
        node.TryGetProperty("latitude", out _).ShouldBeFalse();
        node.TryGetProperty("longitude", out _).ShouldBeFalse();
        node.TryGetProperty("coordinatePrecision", out _).ShouldBeFalse();
        node.GetProperty("positionTreatment").GetString().ShouldBe("no_position");

        // Everything that is not a position is still there: the record asserts that the cave
        // exists and says what it is, which is the whole point of this treatment.
        node.GetProperty("name").GetString().ShouldBe("Sample Cave");
        node.GetProperty("identifier").GetString().ShouldBe("SAMPLE-1");
        node.GetProperty("length").GetDecimal().ShouldBe(1234.5m);
    }

    [Fact]
    public void A_cave_nothing_was_decided_about_carries_no_treatment_statement()
    {
        var document = Parse(KarstLinkDocument.Write([Cave(45.0, 25.0)], 0, 5000, Created, ExportId));

        var node = document.GetProperty("@graph")[0];
        node.GetProperty("@type").GetString().ShouldBe("UndergroundCavity");
        node.GetProperty("@id").GetString().ShouldBe($"urn:uuid:{CaveId:D}");
        node.TryGetProperty("positionTreatment", out _).ShouldBeFalse();
        node.TryGetProperty("coordinatePrecision", out _).ShouldBeFalse();
        node.GetProperty("latitude").GetDouble().ShouldBe(45.0);
    }

    [Fact]
    public void The_document_carries_the_identity_the_audit_trail_records()
    {
        var document = Parse(KarstLinkDocument.Write([Cave(45.0, 25.0)], 0, 5000, Created, ExportId));

        document.GetProperty("@id").GetString().ShouldBe($"urn:uuid:{ExportId:D}");
        document.GetProperty("@type").GetString().ShouldBe("CaveExport");
    }
}
