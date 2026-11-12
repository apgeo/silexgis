// SPDX-License-Identifier: AGPL-3.0-or-later
import type { Scenario, ScenarioItem } from '../../src/presenter/scenario.ts';

// The Grind aven, flown in to from far away: across the ridge it lies under in one rising and
// falling arc that runs straight on into circles around the cave while the ground's map is changed,
// the walls recoloured, then in at the entrance and down the shaft to its bottom, turning around it.
//
// The ridge runs roughly south-south-west to north-north-east with its steep face to the west, so
// the approach starts low in the west, looking east at that face, and comes over the crest.

// Every one of these draws in 3D on silexgistest (Freemap Outdoor and the Carto maps do not).
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

const BASEMAPS_FROM = 4.5;
const BASEMAP_EVERY = 2.2;

const scenario: Scenario = {
  name: 'grind-flyby',
  language: 'en',
  setup: {
    cave: 'Grind',
    basemap: { name: 'OpenTopoMap', group: 'Topographic' },
    // The walls alone: no survey lines, and no markers of the other caves along the ridge.
    layersOff: ['Cave centerlines', 'Cave entrances'],
    hide: ['.scene3d-page-dock', '.scene3d-notice'],
  },
  items: [
    { at: 0, kind: 'title', duration: 3, text: 'Avenul de sub Vârful Grind', subtitle: 'Flying in — SilexGIS' },

    // One curve, from far away to circling the cave: every move is `smooth` and starts where the
    // last one ends, so the camera never stops until it is underground at the bottom.
    { at: 0, kind: 'camera', to: { heading: 95, pitch: -6, range: 30, lift: 0.7, aboveGround: 200 } },
    // Up over the crest: higher above the cave than it started, though much nearer.
    { at: 0, duration: 6, kind: 'camera', to: { heading: 150, pitch: -42, range: 6.5, lift: 0.5 }, ease: 'smooth' },
    // Down the other side, into a circle around the cave.
    { at: 6, duration: 5, kind: 'camera', to: { heading: 225, pitch: -35, range: 3, lift: 0.2 }, ease: 'smooth' },
    { at: 11, duration: 17, kind: 'camera', to: { headingBy: 320, pitch: -32, range: 2.6 }, ease: 'smooth' },
    // In at the entrance, underground…
    { at: 28, duration: 3.5, kind: 'camera', to: { headingBy: 70, pitch: -20, range: 0.55, lift: 0.62, aboveGround: null }, ease: 'smooth' },
    // …and down to the bottom of the shaft, still turning around it.
    { at: 31.5, duration: 8, kind: 'camera', to: { headingBy: 300, pitch: -12, range: 0.5, lift: -0.75 }, ease: 'smooth' },

    { at: 3.5, duration: 3.5, kind: 'caption', text: 'From across the ridge to the cave' },
    { at: 11, duration: 3.5, kind: 'caption', text: 'Changing the ground’s map while circling' },
    ...basemaps.map(
      (basemap, index): ScenarioItem => ({
        at: BASEMAPS_FROM + index * BASEMAP_EVERY,
        duration: BASEMAP_EVERY,
        kind: 'basemap',
        name: basemap.name,
        group: basemap.group,
      }),
    ),
    { at: BASEMAPS_FROM + basemaps.length * BASEMAP_EVERY, duration: 1.5, kind: 'layerPanel', open: false },

    { at: 17, duration: 1.4, kind: 'modelColor', to: '#e8742a', strength: 0.55, ease: 'inOut' },
    { at: 17.5, duration: 3.5, kind: 'caption', text: 'Recolouring the cave’s walls' },
    { at: 29, duration: 1.4, kind: 'modelColor', to: '#2bb3c9', strength: 0.55, ease: 'inOut' },
    { at: 31.5, duration: 4, kind: 'caption', text: 'From the entrance down to the bottom' },

    { at: 38.5, duration: 2, kind: 'fade', to: 1, ease: 'in' },
  ],
};

export default scenario;
