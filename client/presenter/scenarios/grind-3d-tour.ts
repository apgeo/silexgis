// SPDX-License-Identifier: AGPL-3.0-or-later
import type { Scenario, ScenarioItem } from '../../src/presenter/scenario.ts';

// The Grind aven in 3D: its walls over OpenTopoMap, the main line on the surface, a full turn
// around it while the ground's map is changed a dozen times, the basemap faded out and back, then
// down among the walls and up again.

// Every one of these draws in 3D on silexgistest. Left out: Freemap Outdoor (its tile server
// sends no CORS header, so the globe stays empty) and the three Carto maps (every tile is an
// "API key required" placeholder).
const basemaps: { name: string; group: string }[] = [
  { name: 'Esri World Topographic', group: 'Topographic' },
  { name: 'CyclOSM', group: 'Topographic' },
  { name: 'OpenStreetMap', group: 'General' },
  { name: 'Esri Shaded Relief', group: 'Relief' },
  { name: 'Esri World Hillshade', group: 'Relief' },
  { name: 'Esri National Geographic', group: 'Relief' },
  { name: 'OpenFreeMap Liberty', group: 'Vector' },
  { name: 'Sentinel-2 cloudless', group: 'Imagery' },
  { name: 'Esri World Imagery', group: 'Imagery' },
  { name: 'Google Terrain', group: 'Restricted terms of use' },
  { name: 'Google Satellite', group: 'Restricted terms of use' },
];

const BASEMAPS_FROM = 14;
const BASEMAP_EVERY = 3.4;

const scenario: Scenario = {
  name: 'grind-3d-tour',
  language: 'en',
  setup: {
    cave: 'Grind',
    basemap: { name: 'OpenTopoMap', group: 'Topographic' },
    // The main line is shown later in the movie, on cue.
    layersOff: ['Cave centerlines'],
    // The details panel beside the scene and the scene's own notices are not part of the story.
    hide: ['.scene3d-page-dock', '.scene3d-notice'],
  },
  items: [
    { at: 0, kind: 'title', duration: 4, text: 'Avenul de sub Vârful Grind', subtitle: 'The cave in 3D — SilexGIS' },
    { at: 0, kind: 'camera', to: { heading: 15, pitch: -55, range: 6, aboveGround: 120 } },
    { at: 0, duration: 10, kind: 'camera', to: { headingBy: 30, pitch: -40, range: 3.2 }, ease: 'inOut' },
    { at: 4.5, duration: 4.5, kind: 'caption', text: 'The surveyed walls of the cave, over OpenTopoMap' },

    { at: 9.5, duration: 3, kind: 'layer', label: 'Cave centerlines', on: true },
    { at: 11, duration: 3.5, kind: 'caption', text: 'The main line, projected on the surface' },

    { at: 10, duration: 44, kind: 'camera', to: { headingBy: 360 }, ease: 'inOut' },
    { at: BASEMAPS_FROM, duration: 4, kind: 'caption', text: 'Changing the ground’s map while the camera turns' },
    ...basemaps.map(
      (basemap, index): ScenarioItem => ({
        at: BASEMAPS_FROM + index * BASEMAP_EVERY,
        duration: BASEMAP_EVERY,
        kind: 'basemap',
        name: basemap.name,
        group: basemap.group,
      }),
    ),

    { at: 52.5, duration: 7, kind: 'basemapOpacity', values: [100, 0, 100], ease: 'inOut' },
    { at: 53, duration: 4, kind: 'caption', text: 'Fading the basemap' },
    { at: 54, duration: 5, kind: 'camera', to: { headingBy: 25 }, ease: 'inOut' },
    { at: 59.5, duration: 1.5, kind: 'layerPanel', open: false },

    { at: 59, duration: 7, kind: 'camera', to: { headingBy: 40, pitch: -4, range: 0.45, lift: 0.1, aboveGround: null }, ease: 'inOut' },
    { at: 63, duration: 4, kind: 'caption', text: 'Underground, beside the cave walls' },
    { at: 66, duration: 9, kind: 'camera', to: { headingBy: 80, pitch: 8, range: 0.32, lift: -0.15 }, ease: 'inOut' },

    { at: 75, duration: 6, kind: 'camera', to: { headingBy: 20, pitch: -45, range: 6, aboveGround: 120 }, ease: 'inOut' },
    { at: 79, duration: 2, kind: 'fade', to: 1, ease: 'in' },
  ],
};

export default scenario;
