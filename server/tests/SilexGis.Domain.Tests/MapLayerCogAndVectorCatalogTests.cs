// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Map;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The catalogue reader on its two file-and-shape kinds: a cloud-optimised GeoTIFF and vector data
/// drawn in a style — what each entry is read into, and what each refuses, by its stable code.
/// </summary>
/// <remarks>
/// The projection cases are the ones that matter most here. A GeoTIFF in a coordinate system a
/// view does not know is not refused by anything on screen: it is drawn, somewhere else. So an
/// entry whose system has no definition has to be refused where the operator reads why, and one
/// that has a definition has to carry it to the views.
/// </remarks>
public class MapLayerCogAndVectorCatalogTests
{
    /// <summary>A stand-in for the installation's coordinate-system registry: it knows Stereo 70 and nothing else.</summary>
    private const string Stereo70Definition = "+proj=sterea +lat_0=46 +lon_0=25 +k=0.99975 +units=m +no_defs";

    private static string? Registry(int code) => code == 3844 ? $"  {Stereo70Definition}  " : null;

    private static string Doc(string inner) =>
        $"<silexgis-map-layers version=\"1\">{inner}</silexgis-map-layers>";

    private static MapLayerCatalogResult Read(string inner) => MapLayerCatalog.Read(Doc(inner), Registry);

    // ---- cloud-optimised GeoTIFF

    [Fact]
    public void Reads_a_cog_entry_with_its_file_options_and_their_defaults()
    {
        var result = Read(
            """
            <layer name="Ortho" kind="cog" url="https://data.example.gov/ortho.tif" projection="EPSG:3857" attribution="© Agency" />
            <layer name="Hillshade" kind="COG" base="false" url="https://data.example.gov/lidar.tif"
                   projection="epsg:3844" bands=" 1 " nodata="-9999" min="0" max="2500.5" />
            <layer name="Geology" kind="cog" url="https://data.example.gov/geo.tif?key={apikey}" apiKey="Agency"
                   projection="EPSG:4326" bands="3,2,1" />
            """);

        Assert.Empty(result.Problems);

        var ortho = result.Layers.Single(l => l.Name == "Ortho");
        Assert.Equal(MapLayerKind.Cog, ortho.Kind);
        Assert.Null(ortho.Wms);
        Assert.Null(ortho.Wmts);
        Assert.Null(ortho.Vector);
        // Read as the file stores it, scaled by its own statistics, placed in the map's own
        // system — which every view knows, so no definition travels with it.
        Assert.Equal(new MapLayerCogOptions(null, null, null, null, "EPSG:3857", null), ortho.Cog);
        Assert.Equal(19, ortho.MaxZoom);

        var hillshade = result.Layers.Single(l => l.Name == "Hillshade").Cog!;
        Assert.Equal([1], hillshade.Bands!);
        Assert.Equal(-9999.0, hillshade.Nodata);
        Assert.Equal(0.0, hillshade.Min);
        Assert.Equal(2500.5, hillshade.Max);
        // One spelling, and the registry's definition with it, trimmed.
        Assert.Equal("EPSG:3844", hillshade.Projection);
        Assert.Equal(Stereo70Definition, hillshade.ProjectionDefinition);

        var geology = result.Layers.Single(l => l.Name == "Geology").Cog!;
        Assert.Equal([3, 2, 1], geology.Bands!);
        Assert.Equal("EPSG:4326", geology.Projection);
        Assert.Null(geology.ProjectionDefinition);
    }

    [Fact]
    public void A_cog_in_a_system_the_installation_cannot_define_is_refused_where_the_operator_reads_why()
    {
        var unknown = Read(
            """<layer name="Elsewhere" kind="cog" url="https://data.example.gov/a.tif" projection="EPSG:900914" />""");
        Assert.Empty(unknown.Layers);
        var problem = Assert.Single(unknown.Problems);
        Assert.Equal(MapLayerCatalogCodes.CogProjectionUnknown, problem.Code);
        Assert.Contains("EPSG:900914", problem.Message, StringComparison.Ordinal);
        Assert.Equal(["Elsewhere"], unknown.RefusedNames);

        // Without a registry only the two systems every view knows can be read: a reader that
        // cannot look a definition up must not hand a view a file it would misplace.
        const string stereo = """<layer name="Stereo" kind="cog" url="https://data.example.gov/a.tif" projection="EPSG:3844" />""";
        Assert.Equal(MapLayerCatalogCodes.CogProjectionUnknown, Assert.Single(MapLayerCatalog.Read(Doc(stereo)).Problems).Code);
        Assert.Single(MapLayerCatalog.Read(Doc(
            """<layer name="Mercator" kind="cog" url="https://data.example.gov/a.tif" projection="EPSG:3857" />""")).Layers);
    }

    [Theory]
    [InlineData("""<layer name="Bad" kind="cog" url="https://d.example/a.tif" />""", MapLayerCatalogCodes.CogProjectionMissing)]
    [InlineData("""<layer name="Bad" kind="cog" url="https://d.example/a.tif" projection="3844" />""", MapLayerCatalogCodes.CogProjectionUnknown)]
    [InlineData("""<layer name="Bad" kind="cog" url="https://d.example/a.tif" projection="ESRI:102008" />""", MapLayerCatalogCodes.CogProjectionUnknown)]
    [InlineData("""<layer name="Bad" kind="cog" url="https://d.example/a.tif" projection="EPSG:-3844" />""", MapLayerCatalogCodes.CogProjectionUnknown)]
    [InlineData("""<layer name="Bad" kind="cog" url="https://d.example/a.tif" projection="EPSG:0" />""", MapLayerCatalogCodes.CogProjectionUnknown)]
    [InlineData("""<layer name="Bad" kind="cog" url="https://d.example/a.tif" projection="EPSG:3857" bands="0" />""", MapLayerCatalogCodes.CogBandsInvalid)]
    [InlineData("""<layer name="Bad" kind="cog" url="https://d.example/a.tif" projection="EPSG:3857" bands="1,2,3,4,5" />""", MapLayerCatalogCodes.CogBandsInvalid)]
    [InlineData("""<layer name="Bad" kind="cog" url="https://d.example/a.tif" projection="EPSG:3857" bands="1,,2" />""", MapLayerCatalogCodes.CogBandsInvalid)]
    [InlineData("""<layer name="Bad" kind="cog" url="https://d.example/a.tif" projection="EPSG:3857" bands="red" />""", MapLayerCatalogCodes.CogBandsInvalid)]
    [InlineData("""<layer name="Bad" kind="cog" url="https://d.example/a.tif" projection="EPSG:3857" nodata="none" />""", MapLayerCatalogCodes.CogNumberInvalid)]
    [InlineData("""<layer name="Bad" kind="cog" url="https://d.example/a.tif" projection="EPSG:3857" min="NaN" max="1" />""", MapLayerCatalogCodes.CogNumberInvalid)]
    [InlineData("""<layer name="Bad" kind="cog" url="https://d.example/a.tif" projection="EPSG:3857" min="0" max="Infinity" />""", MapLayerCatalogCodes.CogNumberInvalid)]
    [InlineData("""<layer name="Bad" kind="cog" url="https://d.example/a.tif" projection="EPSG:3857" min="0" />""", MapLayerCatalogCodes.CogRangeInvalid)]
    [InlineData("""<layer name="Bad" kind="cog" url="https://d.example/a.tif" projection="EPSG:3857" max="10" />""", MapLayerCatalogCodes.CogRangeInvalid)]
    [InlineData("""<layer name="Bad" kind="cog" url="https://d.example/a.tif" projection="EPSG:3857" min="10" max="10" />""", MapLayerCatalogCodes.CogRangeInvalid)]
    [InlineData("""<layer name="Bad" kind="cog" url="https://d.example/a.tif" projection="EPSG:3857" min="10" max="2" />""", MapLayerCatalogCodes.CogRangeInvalid)]
    [InlineData("""<layer name="Bad" kind="cog" url="https://d.example/{z}/a.tif" projection="EPSG:3857" />""", MapLayerCatalogCodes.StrayPlaceholder)]
    public void A_cog_entry_that_cannot_be_drawn_is_skipped_with_its_code(string entry, string code)
    {
        var result = Read(entry);

        Assert.Empty(result.Layers);
        var problem = Assert.Single(result.Problems);
        Assert.Equal(code, problem.Code);
        Assert.Contains("'Bad'", problem.Message, StringComparison.Ordinal);
        Assert.EndsWith("skipped", problem.Message, StringComparison.Ordinal);
        Assert.Equal(["Bad"], result.RefusedNames);
    }

    // ---- vector

    [Fact]
    public void Reads_a_vector_entry_in_each_of_its_formats()
    {
        var result = Read(
            """
            <layer name="Raster" url="https://t.example/{z}/{x}/{y}.png" />
            <layer name="Liberty" kind="vector" format="mvt" url="https://tiles.example.org/planet"
                   style="https://tiles.example.org/styles/liberty" styleSource="openmaptiles"
                   labelField="name:{lang}" scene3d="Raster" attribution="© OpenMapTiles © OpenStreetMap contributors" />
            <layer name="Agency tiles" kind="vector" format="MVT" base="false" url="https://v.example.gov/{z}/{x}/{y}.pbf" maxZoom="12" />
            <layer name="Archive" kind="vector" format="pmtiles" url="https://files.example.org/romania.pmtiles"
                   style="https://files.example.org/light.json" maxZoom="15" />
            <layer name="Boundary" kind="vector" format="geojson" base="false" url="https://files.example.org/natura.geojson" />
            """);

        Assert.Empty(result.Problems);

        var liberty = result.Layers.Single(l => l.Name == "Liberty");
        Assert.Equal(MapLayerKind.Vector, liberty.Kind);
        Assert.Null(liberty.Cog);
        Assert.Equal(
            new MapLayerVectorOptions(
                MapLayerVectorFormat.Mvt,
                "https://tiles.example.org/styles/liberty",
                "openmaptiles",
                "name:{lang}",
                "Raster"),
            liberty.Vector);
        // Where OpenMapTiles-schema tiles stop, unless the entry says otherwise: one level too few
        // is drawn sharp by enlarging the last, one too many is a blank view.
        Assert.Equal(MapLayerKinds.VectorDefaultMaxZoom, liberty.MaxZoom);

        Assert.Equal(
            new MapLayerVectorOptions(MapLayerVectorFormat.Mvt, null, null, null, null),
            result.Layers.Single(l => l.Name == "Agency tiles").Vector);
        Assert.Equal(12, result.Layers.Single(l => l.Name == "Agency tiles").MaxZoom);

        var archive = result.Layers.Single(l => l.Name == "Archive");
        Assert.Equal(MapLayerVectorFormat.Pmtiles, archive.Vector!.Format);
        Assert.Equal(15, archive.MaxZoom);

        Assert.Equal(MapLayerVectorFormat.Geojson, result.Layers.Single(l => l.Name == "Boundary").Vector!.Format);
    }

    [Theory]
    [InlineData("""<layer name="Bad" kind="vector" url="https://v.example/{z}/{x}/{y}.pbf" />""", MapLayerCatalogCodes.VectorFormatInvalid)]
    [InlineData("""<layer name="Bad" kind="vector" format="mbtiles" url="https://v.example/a.mbtiles" />""", MapLayerCatalogCodes.VectorFormatInvalid)]
    [InlineData("""<layer name="Bad" kind="vector" format="mvt" url="https://v.example/{z}/{x}.pbf" />""", MapLayerCatalogCodes.VectorTemplateIncomplete)]
    [InlineData("""<layer name="Bad" kind="vector" format="mvt" url="https://v.example/{z}/{x}/{y}.{ext}" />""", MapLayerCatalogCodes.StrayPlaceholder)]
    [InlineData("""<layer name="Bad" kind="vector" format="pmtiles" url="https://v.example/{z}/a.pmtiles" />""", MapLayerCatalogCodes.StrayPlaceholder)]
    [InlineData("""<layer name="Bad" kind="vector" format="geojson" url="https://v.example/{z}/{x}/{y}.geojson" />""", MapLayerCatalogCodes.StrayPlaceholder)]
    [InlineData("""<layer name="Bad" kind="vector" format="mvt" url="https://v.example/tiles" styleSource="openmaptiles" />""", MapLayerCatalogCodes.VectorStyleRequired)]
    [InlineData("""<layer name="Bad" kind="vector" format="mvt" url="https://v.example/tiles" labelField="name:{lang}" />""", MapLayerCatalogCodes.VectorStyleRequired)]
    // A style's address is fetched as it stands: not even a key is put into it.
    [InlineData("""<layer name="Bad" kind="vector" format="mvt" apiKey="Agency" url="https://v.example/tiles?key={apikey}" style="https://v.example/style.json?key={apikey}" />""", MapLayerCatalogCodes.StrayPlaceholder)]
    [InlineData("""<layer name="Bad" kind="vector" format="mvt" url="https://v.example/tiles" style="https://v.example/style.json" labelField="{lang}" />""", MapLayerCatalogCodes.VectorLabelFieldInvalid)]
    [InlineData("""<layer name="Bad" kind="vector" format="mvt" url="https://v.example/tiles" style="https://v.example/style.json" labelField="name:{language}" />""", MapLayerCatalogCodes.VectorLabelFieldInvalid)]
    [InlineData("""<layer name="Bad" kind="vector" format="mvt" url="https://v.example/tiles" style="https://v.example/style.json" labelField="name {lang}" />""", MapLayerCatalogCodes.VectorLabelFieldInvalid)]
    public void A_vector_entry_that_cannot_be_drawn_is_skipped_with_its_code(string entry, string code)
    {
        var result = Read(entry);

        Assert.Empty(result.Layers);
        var problem = Assert.Single(result.Problems);
        Assert.Equal(code, problem.Code);
        Assert.Contains("'Bad'", problem.Message, StringComparison.Ordinal);
        Assert.Equal(["Bad"], result.RefusedNames);
    }

    [Fact]
    public void A_tile_template_may_name_where_the_installations_own_basemap_extract_goes_and_nothing_else_may()
    {
        var result = Read(
            """
            <layer name="Own" kind="vector" format="mvt" url="/basemap/tiles/{basemap}/{z}/{x}/{y}" maxZoom="15" />
            <layer name="Archive" kind="vector" format="pmtiles" url="/basemap/{basemap}.pmtiles" />
            <layer name="Styled" kind="vector" format="mvt" url="/basemap/tiles/{basemap}/{z}/{x}/{y}" style="/styles/{basemap}.json" />
            """);

        Assert.Equal("/basemap/tiles/{basemap}/{z}/{x}/{y}", Assert.Single(result.Layers).UrlTemplate);
        Assert.All(result.Problems, p => Assert.Equal(MapLayerCatalogCodes.StrayPlaceholder, p.Code));
        Assert.Equal(["Archive", "Styled"], result.RefusedNames);
    }

    [Fact]
    public void A_label_field_may_name_one_language_for_every_reader()
    {
        var entry = Assert.Single(Read(
            """<layer name="Romanian" kind="vector" format="mvt" url="https://v.example/tiles" style="https://v.example/s.json" labelField="name:ro" />""").Layers);

        Assert.Equal("name:ro", entry.Vector!.LabelField);
    }

    [Theory]
    // Not declared at all, or declared and skipped: either way nothing the 3D scene could drape.
    [InlineData("""<layer name="Ghost" url="https://t.example/{z}/{x}/{y}.png" kind="wms" />""", "does not declare or skips")]
    [InlineData("""<layer name="Elsewhere" url="https://t.example/{z}/{x}/{y}.png" />""", "does not declare or skips")]
    // Drawn by the scene only once it draws vector styles, which is what the stand-in is for.
    [InlineData("""<layer name="Ghost" kind="vector" format="mvt" url="https://v.example/other" />""", "vector layer")]
    [InlineData("""<layer name="Ghost" kind="cog" url="https://d.example/a.tif" projection="EPSG:3857" />""", "cog layer")]
    // A basemap is stood in for by a basemap: an overlay in its place would leave no ground at all.
    [InlineData("""<layer name="Ghost" base="false" url="https://t.example/{z}/{x}/{y}.png" />""", "an overlay where this entry is a basemap")]
    public void A_stand_in_the_3d_scene_could_not_drape_is_dropped_and_the_entry_kept(string other, string reason)
    {
        var result = Read(other + """<layer name="Styled" kind="vector" format="mvt" url="https://v.example/tiles" scene3d="Ghost" />""");

        var styled = result.Layers.Single(l => l.Name == "Styled");
        Assert.Null(styled.Vector!.Scene3dStandIn);
        var problem = Assert.Single(result.Problems, p => p.Code == MapLayerCatalogCodes.StandInUnusable);
        Assert.Contains("'Styled'", problem.Message, StringComparison.Ordinal);
        Assert.Contains(reason, problem.Message, StringComparison.Ordinal);
        Assert.EndsWith("kept, without a 3D stand-in", problem.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Styled", result.RefusedNames);
    }

    [Fact]
    public void A_stand_in_is_spelt_as_the_entry_it_names_and_may_be_a_map_service()
    {
        var result = Read(
            """
            <layer name="OpenStreetMap" url="https://t.example/{z}/{x}/{y}.png" />
            <layer name="Agency WMS" kind="wms" base="false" url="https://maps.example.gov/wms" layers="roads" />
            <layer name="Styled" kind="vector" format="mvt" url="https://v.example/tiles" scene3d="openstreetmap" />
            <layer name="Roads" kind="vector" format="geojson" base="false" url="https://v.example/roads.geojson" scene3d="Agency WMS" />
            """);

        Assert.Empty(result.Problems);
        Assert.Equal("OpenStreetMap", result.Layers.Single(l => l.Name == "Styled").Vector!.Scene3dStandIn);
        Assert.Equal("Agency WMS", result.Layers.Single(l => l.Name == "Roads").Vector!.Scene3dStandIn);
    }

    // ---- both

    [Theory]
    [InlineData("""<layer name="Files" kind="cog" url="https://d.example/a.tif" projection="EPSG:3857" attribution="© Agency" inDocuments="true" />""", "cog")]
    [InlineData("""<layer name="Files" kind="vector" format="mvt" url="https://v.example/tiles" attribution="© OpenStreetMap contributors" inDocuments="true" />""", "vector")]
    public void A_file_or_vector_source_loses_the_document_mark_and_says_so(string entry, string kind)
    {
        var result = Read(entry);

        Assert.False(Assert.Single(result.Layers).InDocuments);
        var problem = Assert.Single(result.Problems);
        Assert.Equal(MapLayerDocumentRules.NotPlainTilesCode, problem.Code);
        Assert.Contains($"is a {kind} layer", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_problem_the_reader_reports_carries_a_code()
    {
        var result = Read(
            """
            <layer url="https://t.example/{z}/{x}/{y}.png" />
            <layer name="No url" />
            <layer name="Kind" kind="tiles" url="https://t.example/{z}/{x}/{y}.png" />
            <layer name="Zoom" url="https://t.example/{z}/{x}/{y}.png" minZoom="9" maxZoom="3" />
            <layer name="Twice" url="https://t.example/{z}/{x}/{y}.png" />
            <layer name="Twice" url="https://t.example/{z}/{x}/{y}.png" />
            <layer name="Cog" kind="cog" url="https://d.example/a.tif" />
            <layer name="Vector" kind="vector" url="https://v.example/tiles" />
            """);

        Assert.Equal(7, result.Problems.Count);
        Assert.All(result.Problems, p => Assert.StartsWith("map_layer.", p.Code, StringComparison.Ordinal));
        Assert.Equal(
            [
                MapLayerCatalogCodes.NoName, MapLayerCatalogCodes.NoUrl, MapLayerCatalogCodes.UnknownKind,
                MapLayerCatalogCodes.ZoomRangeInverted, MapLayerCatalogCodes.CogProjectionMissing,
                MapLayerCatalogCodes.VectorFormatInvalid, MapLayerCatalogCodes.DeclaredTwice,
            ],
            result.Problems.Select(p => p.Code));
    }

    // ---- what is stored

    [Fact]
    public void File_and_vector_options_are_stored_by_name_and_read_back_only_as_their_own_kind()
    {
        var result = Read(
            """
            <layer name="Hillshade" kind="cog" url="https://d.example/a.tif" projection="EPSG:3844" bands="1" nodata="0" min="1" max="255" />
            <layer name="Archive" kind="vector" format="pmtiles" url="https://v.example/a.pmtiles" style="https://v.example/s.json" labelField="name:{lang}" />
            """);
        var cogEntry = result.Layers.Single(l => l.Kind == MapLayerKind.Cog);
        var vectorEntry = result.Layers.Single(l => l.Kind == MapLayerKind.Vector);

        var cogStored = MapLayerOptionsJson.Write(cogEntry)!;
        var cog = MapLayerOptionsJson.Cog(MapLayerKind.Cog, cogStored)!;
        Assert.Equal(cogEntry.Cog!.Bands!, cog.Bands!);
        Assert.Equal(cogEntry.Cog with { Bands = cog.Bands }, cog);
        Assert.True(MapLayerOptionsJson.IsDrawable(MapLayerKind.Cog, cogStored));

        var vectorStored = MapLayerOptionsJson.Write(vectorEntry)!;
        // By its word, so a renumbered enum can never turn one format into another.
        Assert.Contains("\"format\":\"pmtiles\"", vectorStored, StringComparison.Ordinal);
        Assert.Equal(vectorEntry.Vector, MapLayerOptionsJson.Vector(MapLayerKind.Vector, vectorStored));
        Assert.True(MapLayerOptionsJson.IsDrawable(MapLayerKind.Vector, vectorStored));

        // Each read as the other kind is no options at all.
        Assert.Null(MapLayerOptionsJson.Vector(MapLayerKind.Vector, cogStored));
        Assert.Null(MapLayerOptionsJson.Cog(MapLayerKind.Cog, vectorStored));
        Assert.Null(MapLayerOptionsJson.Cog(MapLayerKind.Vector, cogStored));
        Assert.Null(MapLayerOptionsJson.Wms(MapLayerKind.Wms, vectorStored));
    }

    /// <summary>
    /// A stored row of the right shape that the catalogue could never have written is not drawn:
    /// above all a file in a system nobody defined, which a view would place somewhere else.
    /// </summary>
    [Theory]
    [InlineData("""{"bands":null,"nodata":null,"min":null,"max":null,"projection":"EPSG:3844","projectionDefinition":null}""")]
    [InlineData("""{"bands":null,"nodata":null,"min":null,"max":null,"projection":"epsg:3857","projectionDefinition":null}""")]
    [InlineData("""{"bands":null,"nodata":null,"min":null,"max":null,"projection":"Stereo70","projectionDefinition":"+proj=sterea"}""")]
    [InlineData("""{"bands":[0],"nodata":null,"min":null,"max":null,"projection":"EPSG:3857","projectionDefinition":null}""")]
    [InlineData("""{"bands":[],"nodata":null,"min":null,"max":null,"projection":"EPSG:3857","projectionDefinition":null}""")]
    [InlineData("""{"bands":[1,2,3,4,5],"nodata":null,"min":null,"max":null,"projection":"EPSG:3857","projectionDefinition":null}""")]
    [InlineData("""{"bands":null,"nodata":null,"min":5,"max":1,"projection":"EPSG:3857","projectionDefinition":null}""")]
    [InlineData("""{"bands":null,"nodata":null,"min":5,"max":null,"projection":"EPSG:3857","projectionDefinition":null}""")]
    // Every member present: a text that only partly matches is not the kind's options.
    [InlineData("""{"projection":"EPSG:3857"}""")]
    public void A_stored_cog_the_catalogue_would_never_have_written_is_not_drawn(string stored)
    {
        Assert.Null(MapLayerOptionsJson.Cog(MapLayerKind.Cog, stored));
        Assert.False(MapLayerOptionsJson.IsDrawable(MapLayerKind.Cog, stored));
    }

    [Theory]
    [InlineData("""{"format":1,"style":null,"styleSource":null,"labelField":null,"scene3dStandIn":null}""")]
    [InlineData("""{"format":"mbtiles","style":null,"styleSource":null,"labelField":null,"scene3dStandIn":null}""")]
    [InlineData("""{"format":"mvt"}""")]
    public void A_stored_vector_layer_with_a_format_it_cannot_name_is_not_drawn(string stored)
    {
        Assert.Null(MapLayerOptionsJson.Vector(MapLayerKind.Vector, stored));
        Assert.False(MapLayerOptionsJson.IsDrawable(MapLayerKind.Vector, stored));
    }

    [Theory]
    [InlineData("EPSG:3844", 3844)]
    [InlineData(" epsg:4326 ", 4326)]
    [InlineData("EPSG:", null)]
    [InlineData("EPSG:38 44", null)]
    [InlineData("EPSG:+3844", null)]
    [InlineData("EPSG:1000000", null)]
    [InlineData("CRS:84", null)]
    [InlineData(null, null)]
    public void A_projection_is_written_epsg_and_a_positive_code(string? projection, int? code)
    {
        Assert.Equal(code, MapLayerCatalog.EpsgCode(projection));
    }
}
