# Map symbols — provenance

The files in this directory are the map symbols an administrator can choose for a feature kind or
for the cave entrances (the drawn shapes, `shape:*`, are generated in code and are not files here).
Every file is offered by `client/src/map/symbolLibrary.ts`, and a test keeps that list, this file
and the folder in step.

## SilexGIS speleological set (19 PNG)

arrows, bivouac, calm, chimney, continuation, dedritus, desobstruction, driller,
exploration_point, flag, fracture_line, generic_feature, lake, peak, pit, pitch,
sinkhole, tunnel, water_flow.

Carried over from earlier SilexGIS versions (the v1 PHP application, in use by the Silex Brașov
speleo club since ~2014): the club's own cartographic symbol set for speleological surface
features, redistributed under the project license (AGPL-3.0-or-later) as original works of the
SilexGIS / Silex Brașov project.

> **Owner sign-off required (1.0 release gate):** confirm that all 19 icons are original works of
> the club (or otherwise cleared for AGPL redistribution) with no third-party clip-art among them.
> If any icon has a separate origin, record its source and license here and in the root `NOTICE`
> file before tagging v3.0.0.

## How the SVG files below were prepared

Each was normalised once, on 2026-11-13, by a small script outside the repository: editor metadata
(Inkscape/Sodipodi, RDF) removed, the intrinsic size set to 48 pixels on the longer side (the map
draws it at half that), and — for the QGIS files — QGIS's `param(...)` placeholders resolved to a
dark fill (`#262626`) with a white outline; the OpenStreetMap Carto files were given the same fill
and a white halo. The drawing itself is unchanged.

## OpenStreetMap Carto (5 SVG) — CC0 1.0

From [gravitystorm/openstreetmap-carto](https://github.com/gravitystorm/openstreetmap-carto),
commit `003cefdd67918e8317e9753312ec3327d78f5d1f`, directory `symbols/`. Released under the CC0
1.0 Universal Public Domain Dedication (the project's `LICENSE.txt`: "this includes the
cartographic design"). The drawn `shape:cave` glyph in `symbolLibrary.ts` is the outline of
`natural/cave.svg`.

| File | Source path |
|---|---|
| `osm_cave.svg` | `natural/cave.svg` |
| `osm_peak.svg` | `natural/peak.svg` |
| `osm_saddle.svg` | `natural/saddle.svg` |
| `osm_spring.svg` | `natural/spring.svg` |
| `osm_waterfall.svg` | `natural/waterfall.svg` |

## QGIS (19 SVG) — GPL-2.0-or-later

From [qgis/QGIS](https://github.com/qgis/QGIS), commit `f8bc9f26641b85e2fa988158cf91db981004d781`,
directory `images/svg/`. Covered by the `Files: *` stanza of QGIS's `debian/copyright`, licence
GPL-2+; redistributed here under GPL-3.0 (text in `LICENSE-GPL-3.0.txt`), which the "or later"
grant allows and which may be combined with the AGPL-3.0 application (section 13 of both licences).

| File | Source path |
|---|---|
| `qgis_poi_cave.svg` | `symbol/poi_cave.svg` |
| `qgis_poi_peak.svg` | `symbol/poi_peak.svg` |
| `qgis_poi_mine.svg` | `symbol/poi_mine.svg` |
| `qgis_poi_mine_abandoned.svg` | `symbol/poi_mine_abandoned.svg` |
| `qgis_alpine_hut.svg` | `accommodation/accommodation_alpinehut.svg` |
| `qgis_camping.svg` | `accommodation/accommodation_camping.svg` |
| `qgis_shelter.svg` | `accommodation/accommodation_shelter.svg` |
| `qgis_survey_point.svg` | `amenity/amenity_survey_point.svg` |
| `qgis_information.svg` | `amenity/amenity_information.svg` |
| `qgis_first_aid.svg` | `health/health_hospital_emergency.svg` |
| `qgis_view_point.svg` | `tourist/tourist_view_point.svg` |
| `qgis_archaeological.svg` | `tourist/tourist_archaeological.svg` |
| `qgis_tree.svg` | `gpsicons/tree.svg` |
| `qgis_danger.svg` | `gpsicons/skull.svg` |
| `qgis_flag.svg` | `gpsicons/flag.svg` |
| `qgis_camera.svg` | `gpsicons/camera.svg` |
| `qgis_walker.svg` | `gpsicons/walker.svg` |
| `qgis_parking.svg` | `gpsicons/parking.svg` |
| `qgis_question.svg` | `gpsicons/question.svg` |

## FGDC geologic map symbols (18 SVG) — U.S. Government work; packaging GPL-3.0

The symbols of the *FGDC Digital Cartographic Standard for Geologic Map Symbolization*
(FGDC-STD-013-2006, prepared by the U.S. Geological Survey for the Federal Geographic Data
Committee; https://ngmdb.usgs.gov/fgdc_gds/geolsymstd.php), a work of the U.S. Government. The SVG
renditions are from [rodreras/geologic_icons](https://github.com/rodreras/geologic_icons), commit
`6911d4da88363c803004855ef826eae10cd4b4f0`, directory `collections/geologic_symbols/svg/`, a
repository published under GPL-3.0 (text in `LICENSE-GPL-3.0.txt`, copied from that repository).
They are used here under GPL-3.0. The file name keeps the standard's section in its source path.

| File | Source path |
|---|---|
| `fgdc_spring.svg` | `hydro/26.2.01_spring.svg` |
| `fgdc_thermal_spring.svg` | `volcanic/18.62_thermal_spring.svg` |
| `fgdc_well.svg` | `hydro/26.1.01_well.svg` |
| `fgdc_kettle.svg` | `glacial/13.25_kettle.svg` |
| `fgdc_fossil_locality.svg` | `fossils/10.1.1_locality.svg` |
| `fgdc_bedding_horizontal.svg` | `bedding/6.01_bdg_horiz.svg` |
| `fgdc_bedding_inclined.svg` | `bedding/6.02_bdg_incld.svg` |
| `fgdc_bedding_vertical.svg` | `bedding/6.03_bdg_vert.svg` |
| `fgdc_joint_vertical.svg` | `joints/4.3.03_jt_vert.svg` |
| `fgdc_dome.svg` | `folds/5.11.02_dome.svg` |
| `fgdc_basin.svg` | `folds/5.11.03_basin.svg` |
| `fgdc_prospect.svg` | `natural_resource/19.3.01_prospect.svg` |
| `fgdc_open_pit.svg` | `natural_resource/19.3.04_open_pit.svg` |
| `fgdc_adit.svg` | `natural_resource/19.3.09_adit1.svg` |
| `fgdc_mine_portal.svg` | `natural_resource/19.3.19_portal.svg` |
| `fgdc_shaft.svg` | `natural_resource/19.3.35_shaft_vert.svg` |
| `fgdc_survey_station.svg` | `geophysical/3.3.5_survey_stn.svg` |
| `fgdc_control_point.svg` | `geophysical/3.3.4_horiz_control.svg` |

No geological symbol was drawn for SilexGIS: every one is the standard's own, and a feature the
standard has no point symbol for (a sinkhole, a ponor) has none in this group.
