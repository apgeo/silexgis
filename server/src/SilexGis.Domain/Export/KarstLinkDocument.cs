// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SilexGis.Domain.Export;

/// <summary>
/// One cave as it will appear in an interchange document, already reduced to what may
/// leave the installation.
/// </summary>
/// <remarks>
/// Nothing here decides anything. A position that arrives in <paramref name="Latitude"/>
/// and <paramref name="Longitude"/> is written out as given, so whatever produced this
/// record is what enforces the protection rule — this type only knows how to say things in
/// the shared vocabulary. Keeping the two apart is what lets the whole translation be
/// tested without a database and without a permission context.
/// </remarks>
/// <param name="Id">Stable identity of the cave, written as a URN.</param>
/// <param name="Name">The cave's name.</param>
/// <param name="IdentificationCode">The registry code this installation knows it by.</param>
/// <param name="AlternateName">Other names the same cave is known by.</param>
/// <param name="Region">The administrative region, as free text.</param>
/// <param name="Latitude">Latitude in WGS 84, or null when no position is disclosed.</param>
/// <param name="Longitude">Longitude in WGS 84, or null when no position is disclosed.</param>
/// <param name="Altitude">Entrance altitude in metres.</param>
/// <param name="LengthMeters">Surveyed development in metres.</param>
/// <param name="DepthMeters">Vertical extent below the entrance, in metres.</param>
/// <param name="CoordinatePrecisionMeters">
/// How far the written position may be from the real one, in metres. Null means the
/// position is the surveyed one; a value means it was deliberately coarsened, and saying so
/// in the file is the difference between an approximation and a lie.
/// </param>
/// <param name="Treatment">
/// What was decided about this cave's protected position, when something was. Null when
/// nothing had to be decided.
/// </param>
/// <param name="SameAs">
/// The same cave in a register outside this installation, as an address a consumer can
/// resolve. It is what makes two files about the same caves joinable at all — a name is not
/// stable and this file's own identifier means nothing outside the installation that wrote
/// it. Null unless the cave is one whose position is not protected: an entry in somebody
/// else's register publishes coordinates, so a link to it would hand over by reference the
/// position the treatment was chosen to withhold.
/// </param>
public sealed record KarstLinkCave(
    Guid Id,
    string? Name,
    string? IdentificationCode = null,
    string? AlternateName = null,
    string? Region = null,
    double? Latitude = null,
    double? Longitude = null,
    double? Altitude = null,
    decimal? LengthMeters = null,
    decimal? DepthMeters = null,
    double? CoordinatePrecisionMeters = null,
    ProtectedPositionTreatment? Treatment = null,
    string? SameAs = null);

/// <summary>
/// Writes caves as JSON-LD in the published cave and karst vocabulary of the international
/// speleological union, plus the small amount this installation has to say about its own
/// file.
/// </summary>
/// <remarks>
/// <para>
/// <b>Published terms only, and the file admits which are not.</b> Every term describing a
/// cave comes from a vocabulary somebody else publishes and a consumer can already resolve:
/// the cave and karst ontology itself, WGS 84 geo positioning, Dublin Core, Darwin Core,
/// GeoNames and schema.org. A term invented here that merely looked like one of those would
/// be worse than an absent term, because a consumer would bind to it and only find out later
/// that nothing else in the world means the same thing by it. Where that vocabulary has no
/// term — and it has none for cave type, and none yet for springs, ponors or water tracing —
/// nothing is written rather than something approximate.
/// </para>
/// <para>
/// <b>What this installation has to say about its own file is namespaced apart on purpose.</b>
/// Which caves were left out, and what was done to a position that is protected, are
/// statements about this export and not facts about karst; they are written under a URN
/// namespace that resolves to nothing and cannot be mistaken for a published term.
/// </para>
/// <para>
/// <b>The file states what was withheld.</b> A recipient who cannot tell that caves were left
/// out has been misled by a document containing only true statements: a count taken from it
/// disagrees with the registry and nothing in the file explains why. So the header carries the
/// counts and a sentence saying it, and every coarsened position carries how far it may be
/// from the real one.
/// </para>
/// </remarks>
public static class KarstLinkDocument
{
    /// <summary>The published cave and karst ontology.</summary>
    public const string KarstLinkNamespace = "https://ontology.uis-speleo.org/ontology/#";

    /// <summary>
    /// Where this document says things about itself. A URN, so that it is inert: it resolves
    /// to nothing, it is not somebody else's vocabulary, and no consumer can mistake it for
    /// one that has been agreed.
    /// </summary>
    public const string LocalNamespace = "urn:silexgis:cave-export#";

    /// <summary>Media type of the bytes this writes.</summary>
    public const string ContentType = "application/ld+json";

    /// <summary>Extension of the file this writes.</summary>
    public const string FileExtension = "jsonld";

    /// <summary>The class every exported cave is an instance of.</summary>
    public const string CavityType = "UndergroundCavity";

    /// <summary>
    /// The term bindings written into every document, and the whole of what a consumer has to
    /// resolve. Deliberately closed: no <c>@vocab</c> is declared, so a key that is not in
    /// here means nothing at all to a processor rather than quietly becoming a term in some
    /// default namespace. That is what makes "this file coins no vocabulary" checkable.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Prefixes = new Dictionary<string, string>
    {
        ["karstlink"] = KarstLinkNamespace,
        ["rdfs"] = "http://www.w3.org/2000/01/rdf-schema#",
        ["dct"] = "http://purl.org/dc/terms/",
        ["dwc"] = "http://rs.tdwg.org/dwc/terms/",
        ["geo"] = "http://www.w3.org/2003/01/geo/wgs84_pos#",
        ["gn"] = "http://www.geonames.org/ontology#",
        ["schema"] = "http://schema.org/",
        ["xsd"] = "http://www.w3.org/2001/XMLSchema#",
        ["silexgis"] = LocalNamespace,
    };

    /// <summary>
    /// Every term this writer may emit, mapped to the prefixed name it stands for. A term
    /// outside this table cannot be written, and the table is asserted against the published
    /// vocabularies rather than trusted.
    /// </summary>
    /// <remarks>
    /// The bindings for a cave follow the context the reference implementation of this
    /// vocabulary publishes for its own data, so that a document from here and a document
    /// from there say the same thing with the same term rather than two defensible ones.
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, string> Terms = new Dictionary<string, string>
    {
        [CavityType] = "karstlink:UndergroundCavity",
        ["name"] = "rdfs:label",
        ["identifier"] = "dct:identifier",
        ["alternateName"] = "gn:alternateName",
        ["region"] = "schema:addressRegion",
        ["latitude"] = "geo:lat",
        ["longitude"] = "geo:long",
        ["altitude"] = "geo:alt",
        ["length"] = "karstlink:length",
        // The vertical extent below the entrance is what the reference implementation binds
        // its "depth" to, and a depth held here is the same measurement under another name.
        ["depth"] = "karstlink:extentBelowEntrance",
        ["sameAs"] = "schema:sameAs",
        ["description"] = "dct:description",
        ["provenance"] = "dct:provenance",
    };

    /// <summary>The document's own statements about itself, none of them a published term.</summary>
    public static readonly IReadOnlyDictionary<string, string> LocalTerms = new Dictionary<string, string>
    {
        ["CaveExport"] = "silexgis:CaveExport",
        ["positionTreatment"] = "silexgis:positionTreatment",
        ["caveCount"] = "silexgis:caveCount",
        ["omittedCaveCount"] = "silexgis:omittedCaveCount",
        ["locationProtectionGridMeters"] = "silexgis:locationProtectionGridMeters",
    };

    /// <summary>
    /// Builds the document's <c>@context</c>. One place, so the golden set a test pins and
    /// the bytes a recipient gets cannot come apart.
    /// </summary>
    public static JsonObject Context()
    {
        var context = new JsonObject();
        foreach (var (prefix, iri) in Prefixes)
        {
            context[prefix] = iri;
        }

        foreach (var (term, prefixed) in Terms)
        {
            context[term] = prefixed;
        }

        foreach (var (term, prefixed) in LocalTerms)
        {
            context[term] = prefixed;
        }

        // The two terms whose literal type is worth stating rather than inferring from how
        // the number or string happens to be written.
        context["created"] = new JsonObject
        {
            ["@id"] = "dct:created",
            ["@type"] = "xsd:dateTime",
        };
        context["coordinatePrecision"] = new JsonObject
        {
            ["@id"] = "dwc:coordinatePrecision",
            ["@type"] = "xsd:decimal",
        };
        return context;
    }

    /// <summary>
    /// Writes one export.
    /// </summary>
    /// <param name="caves">The caves that are in the file, already reduced.</param>
    /// <param name="omittedCount">
    /// How many caves the requested set held that this file does not. Stated even when zero:
    /// "none were left out" is a fact the recipient needs as much as the other one, and a
    /// missing statement would leave them guessing which document they have.
    /// </param>
    /// <param name="gridMeters">
    /// The grid a coarsened position was moved onto, in metres. Written only when at least
    /// one position was coarsened.
    /// </param>
    /// <param name="createdUtc">When the file was produced.</param>
    /// <param name="exportId">
    /// Identity of this export. The same value is what the audit trail records, so a file
    /// somebody is holding can be matched against the record of who produced it and what they
    /// chose — which is the question asked afterwards, when the file has travelled.
    /// </param>
    public static byte[] Write(
        IReadOnlyList<KarstLinkCave> caves,
        int omittedCount,
        double gridMeters,
        DateTimeOffset createdUtc,
        Guid exportId)
    {
        var graph = new JsonArray();
        var coarsened = 0;
        foreach (var cave in caves)
        {
            graph.Add(Node(cave));
            if (cave.CoordinatePrecisionMeters is not null)
            {
                coarsened++;
            }
        }

        var document = new JsonObject
        {
            ["@context"] = Context(),
            ["@id"] = Urn(exportId),
            ["@type"] = "CaveExport",
            ["created"] = createdUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["caveCount"] = caves.Count,
            ["omittedCaveCount"] = omittedCount,
            ["provenance"] = Provenance(caves.Count, omittedCount, coarsened, gridMeters),
        };

        if (coarsened > 0)
        {
            document["locationProtectionGridMeters"] = gridMeters;
        }

        document["@graph"] = graph;

        return Encoding.UTF8.GetBytes(document.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
        }));
    }

    /// <summary>
    /// The sentence a person reads. It says the two things a machine-readable count cannot be
    /// relied on to be looked at: that caves are missing, and that some positions are not the
    /// real ones.
    /// </summary>
    private static string Provenance(int caveCount, int omittedCount, int coarsened, double gridMeters)
    {
        var parts = new List<string>
        {
            $"{caveCount} cave(s) exported from a SilexGIS installation, "
            + "restricted to what the exporting account was allowed to read.",
        };

        parts.Add(omittedCount > 0
            ? $"{omittedCount} further cave(s) matched the request and were deliberately left out "
              + "because this installation protects their location; counts taken from this file "
              + "will therefore be lower than the registry's."
            : "No cave was left out of this file.");

        if (coarsened > 0)
        {
            parts.Add(
                $"{coarsened} cave(s) are placed on a {gridMeters:0.###} m grid rather than at their "
                + "surveyed position, and each says so; no position in this file is more precise "
                + "than the coordinate precision stated beside it.");
        }

        return string.Join(" ", parts);
    }

    private static JsonObject Node(KarstLinkCave cave)
    {
        var node = new JsonObject
        {
            ["@id"] = Urn(cave.Id),
            ["@type"] = CavityType,
        };

        Put(node, "name", cave.Name);
        Put(node, "identifier", cave.IdentificationCode);
        Put(node, "alternateName", cave.AlternateName);
        Put(node, "region", cave.Region);
        Put(node, "sameAs", cave.SameAs);

        if (cave.Latitude is not null && cave.Longitude is not null)
        {
            node["latitude"] = cave.Latitude.Value;
            node["longitude"] = cave.Longitude.Value;
        }

        if (cave.Altitude is not null)
        {
            node["altitude"] = cave.Altitude.Value;
        }

        if (cave.LengthMeters is not null)
        {
            node["length"] = cave.LengthMeters.Value;
        }

        if (cave.DepthMeters is not null)
        {
            node["depth"] = cave.DepthMeters.Value;
        }

        if (cave.CoordinatePrecisionMeters is not null)
        {
            node["coordinatePrecision"] = cave.CoordinatePrecisionMeters.Value;
        }

        if (cave.Treatment is not null)
        {
            // What was decided about this cave, on the cave, so a recipient reading one record
            // out of the file still learns that its position was treated rather than surveyed.
            node["positionTreatment"] = ProtectedPositionTreatments.Code(cave.Treatment.Value);
        }

        return node;
    }

    private static void Put(JsonObject node, string term, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            node[term] = value;
        }
    }

    /// <summary>
    /// A cave's identity in the file. A URN rather than a URL: this installation is not on the
    /// public web and a link into it would resolve to nothing for whoever ends up holding the
    /// file, while a URN is honest about being an identifier and nothing more.
    /// </summary>
    private static string Urn(Guid id) => $"urn:uuid:{id:D}";
}
