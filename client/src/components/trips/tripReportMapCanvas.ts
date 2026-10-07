// SPDX-License-Identifier: AGPL-3.0-or-later
import Feature from 'ol/Feature';
import Map from 'ol/Map';
import View from 'ol/View';
import GeoJSON from 'ol/format/GeoJSON';
import type Geometry from 'ol/geom/Geometry';
import TileLayer from 'ol/layer/Tile';
import VectorLayer from 'ol/layer/Vector';
import { getPointResolution } from 'ol/proj';
import VectorSource from 'ol/source/Vector';
import XYZ from 'ol/source/XYZ';
import { Circle as CircleStyle, Fill, RegularShape, Stroke, Style, Text } from 'ol/style';
import type { MapLayerInfo } from '../../api/hooks.ts';
import { tripPalette as palette } from '../../map/markerPalette.ts';
import type { ReportMapShape, TripReportMapContent } from './tripReportMap.ts';

// The drawing itself: one map, built for one picture and thrown away.
//
// A map of its own rather than the workspace map or the small maps on the page, for three
// reasons. The workspace map is one shared object with one place on the screen. The page's maps
// are whatever size the window made them and wherever their reader last dragged them, so a
// picture taken from one would differ from one download to the next. And none of them asks its
// tiles in a way that lets the result be read back out of the browser: a tile fetched without
// saying so cannot be copied off a canvas, and saying so is a property of the source that draws
// it, fixed when the source is made.
//
// Everything that needs a real browser is in this file. What is drawn, which background is used
// and what happens when the background fails are decided elsewhere, in code a test can run.

/** How the background fared. `none-asked` when the picture was asked for without one. */
export type PaintedBackground = 'none-asked' | 'drawn' | 'tiles-failed' | 'timed-out';

export interface PaintedMap {
  /** The drawn map, exactly as many pixels as it was asked for. */
  canvas: HTMLCanvasElement;
  /** Metres on the ground to one pixel, at the middle of the picture. For the scale bar. */
  metresPerPixel: number;
  background: PaintedBackground;
}

export interface PaintOptions {
  width: number;
  height: number;
  /** How long the background's tiles are waited for before the picture goes on without them. */
  timeoutMs: number;
}

/** The shape of the function below, so that code deciding what to do with it can be handed another. */
export type ReportMapPainter = (
  content: TripReportMapContent,
  basemap: MapLayerInfo | null,
  options: PaintOptions,
) => Promise<PaintedMap>;

// The map works in web mercator and the answers are in degrees; the conversion is here and
// nowhere else in the picture.
const MAP_PROJECTION = 'EPSG:3857';
const DATA_PROJECTION = 'EPSG:4326';

/** What the picture is drawn on where there is no background, or under one that has gaps. */
const GROUND = '#f4f1ea';

/** As close as a picture of one single point goes: a few hundred metres across. */
const CLOSEST_ZOOM = 15;

/** Room kept clear between the shapes and the picture's edge, in pixels. */
const MARGIN = 80;

// Sized for paper, not for a screen. The picture is placed about fifteen centimetres wide, so a
// pixel is about an eighth of a millimetre: the six-pixel dot the workspace map draws would print
// smaller than a full stop. The colours and outlines are the workspace's own — a filled disc where
// the party worked, a hollow ring where it met, a square for a cave it names — so the same trip
// reads the same way on the screen and in the document.
const casing = new Stroke({ color: 'rgba(255, 255, 255, 0.9)', width: 9 });

const sketchStyles = [
  // A light line under the coloured one, so a track stays legible over any background.
  new Style({ stroke: casing, zIndex: 1 }),
  new Style({
    image: new CircleStyle({
      radius: 11,
      fill: new Fill({ color: palette.sketch }),
      stroke: new Stroke({ color: palette.stroke, width: 3 }),
      declutterMode: 'obstacle',
    }),
    stroke: new Stroke({ color: palette.sketch, width: 5 }),
    fill: new Fill({ color: palette.sketchFill }),
    zIndex: 2,
  }),
];

const meetingStyle = new Style({
  image: new CircleStyle({
    radius: 11,
    fill: new Fill({ color: palette.meetingCentre }),
    stroke: new Stroke({ color: palette.meeting, width: 5 }),
    declutterMode: 'obstacle',
  }),
  zIndex: 4,
});

function caveStyle(label: string | undefined): Style {
  return new Style({
    image: new RegularShape({
      points: 4,
      radius: 13,
      angle: Math.PI / 4,
      fill: new Fill({ color: palette.derived }),
      stroke: new Stroke({ color: palette.stroke, width: 3 }),
      // Always drawn, and kept clear of names: a name that would land on a mark is the one left
      // out, never the mark.
      declutterMode: 'obstacle',
    }),
    text: label
      ? new Text({
          text: label.length > 32 ? `${label.slice(0, 31)}…` : label,
          font: '600 20px sans-serif',
          // Above the mark and clear of its outline. Where two caves stand close enough for
          // their names to collide, the later name is the one left out; both marks stay.
          offsetY: -30,
          fill: new Fill({ color: '#1f1f1f' }),
          stroke: new Stroke({ color: 'rgba(255, 255, 255, 0.95)', width: 5 }),
        })
      : undefined,
    zIndex: 3,
  });
}

function styleFor(shape: ReportMapShape): Style | Style[] {
  if (shape.kind === 'meeting') {
    return meetingStyle;
  }
  return shape.kind === 'cave' ? caveStyle(shape.label) : sketchStyles;
}

/** The shapes as features the map can draw. One the reader cannot make sense of is left out. */
function featuresOf(content: TripReportMapContent): Feature<Geometry>[] {
  const format = new GeoJSON();
  const features: Feature<Geometry>[] = [];
  for (const shape of content.shapes) {
    let geometry: Geometry;
    try {
      geometry = format.readGeometry(shape.geometry, {
        dataProjection: DATA_PROJECTION,
        featureProjection: MAP_PROJECTION,
      });
    } catch {
      continue;
    }
    const feature = new Feature({ geometry });
    feature.setStyle(styleFor(shape));
    features.push(feature);
  }
  return features;
}

/** The zoom band a catalogue entry declares, with the same repair the workspace map applies. */
function deepestZoom(basemap: MapLayerInfo | null): number {
  if (!basemap) {
    return CLOSEST_ZOOM;
  }
  const declared = basemap.maxZoom && basemap.maxZoom > (basemap.minZoom ?? 0) ? basemap.maxZoom : 19;
  return Math.min(CLOSEST_ZOOM, declared);
}

/**
 * Copies what the map drew onto one canvas of the picture's own size.
 *
 * The map draws each layer on a canvas of its own, positioned and faded by style rules rather
 * than by anything in the pixels, so the layers are laid over one another here the way the page
 * would have shown them.
 */
function flatten(map: Map, width: number, height: number): HTMLCanvasElement {
  const canvas = document.createElement('canvas');
  canvas.width = width;
  canvas.height = height;
  const context = canvas.getContext('2d');
  if (!context) {
    throw new Error('This browser cannot draw onto a 2D canvas.');
  }
  context.fillStyle = GROUND;
  context.fillRect(0, 0, width, height);

  map
    .getViewport()
    .querySelectorAll<HTMLCanvasElement>('.ol-layer canvas, canvas.ol-layer')
    .forEach((layer) => {
      if (layer.width === 0) {
        return;
      }
      const holder = layer.parentNode as HTMLElement | null;
      const opacity = holder?.style.opacity || layer.style.opacity;
      context.globalAlpha = opacity === '' ? 1 : Number(opacity);
      const match = /^matrix\(([^(]*)\)$/.exec(layer.style.transform);
      if (match) {
        const [a, b, c, d, e, f] = match[1].split(',').map(Number);
        context.setTransform(a, b, c, d, e, f);
      } else {
        context.setTransform(
          parseFloat(layer.style.width) / layer.width || 1,
          0,
          0,
          parseFloat(layer.style.height) / layer.height || 1,
          0,
          0,
        );
      }
      context.drawImage(layer, 0, 0);
    });

  context.globalAlpha = 1;
  context.setTransform(1, 0, 0, 1, 0, 0);
  return canvas;
}

/**
 * Draws the shapes — over the given background, or over a plain ground when there is none — and
 * hands back the picture and how the background fared.
 *
 * A background that fails does not fail the drawing. A tile that will not load leaves a hole
 * nothing on the picture explains, and a source that never answers would hold the download for
 * ever, so both are reported in the answer for the caller to act on — which it does by drawing
 * again without the background and saying so on the picture. This function never leaves a
 * half-tiled map looking like a whole one: when its answer says `drawn`, every tile arrived.
 */
export const paintTripReportMap: ReportMapPainter = async (content, basemap, options) => {
  const { width, height, timeoutMs } = options;

  // Decided before anything is put on the page. A picture of nothing has nowhere to be centred,
  // and a map with no centre never finishes drawing — which would read as a slow background.
  const features = featuresOf(content);
  if (features.length === 0) {
    throw new Error('None of the shapes could be read as something to draw.');
  }

  // Off the screen but laid out: the map measures the element it is put in, and one that is not
  // in the document has no size to measure.
  const host = document.createElement('div');
  host.setAttribute('aria-hidden', 'true');
  host.style.cssText =
    `position:fixed;left:-100000px;top:0;pointer-events:none;` +
    `width:${width}px;height:${height}px;`;
  document.body.appendChild(host);

  let failedTiles = 0;
  let tiles: TileLayer | null = null;
  if (basemap) {
    const source = new XYZ({
      url: basemap.urlTemplate,
      // The whole point of a source of this picture's own. Asked this way, a tile either comes
      // back with leave to be read out of the browser or does not come back at all; asked the
      // ordinary way it would be drawn happily and then make the finished picture unreadable.
      crossOrigin: 'anonymous',
      maxZoom: basemap.maxZoom && basemap.maxZoom > (basemap.minZoom ?? 0) ? basemap.maxZoom : 19,
      // No fade-in: the picture is taken the moment the tiles are there.
      transition: 0,
    });
    source.on('tileloaderror', () => {
      failedTiles += 1;
    });
    tiles = new TileLayer({ source });
  }

  const shapes = new VectorSource<Feature<Geometry>>({ features });
  const view = new View({
    projection: MAP_PROJECTION,
    // Whole zoom levels only, so the background is drawn at the size it was made at rather than
    // stretched to a size in between — and so the same trip is always framed the same way.
    constrainResolution: true,
    maxZoom: deepestZoom(basemap),
  });
  const map = new Map({
    target: host,
    layers: [...(tiles ? [tiles] : []), new VectorLayer({ source: shapes, declutter: true })],
    view,
    // A picture: nothing on it to press, and one pixel drawn for each pixel asked for whatever
    // the density of the screen in front of this reader.
    controls: [],
    interactions: [],
    pixelRatio: 1,
  });

  try {
    map.setSize([width, height]);
    const extent = shapes.getExtent();
    // An empty source reports an infinite extent, and fitting one throws.
    if (Number.isFinite(extent[0])) {
      view.fit(extent, { size: [width, height], padding: [MARGIN, MARGIN, MARGIN, MARGIN] });
    }

    const settled = await new Promise<'complete' | 'timed-out'>((resolve) => {
      const timer = window.setTimeout(() => resolve('timed-out'), timeoutMs);
      map.once('rendercomplete', () => {
        window.clearTimeout(timer);
        resolve('complete');
      });
      map.renderSync();
    });

    const centre = view.getCenter();
    const resolution = view.getResolution();
    const background: PaintedBackground = !basemap
      ? 'none-asked'
      : settled === 'timed-out'
        ? 'timed-out'
        : failedTiles > 0
          ? 'tiles-failed'
          : 'drawn';

    return {
      canvas: flatten(map, width, height),
      metresPerPixel:
        centre && resolution ? getPointResolution(MAP_PROJECTION, resolution, centre, 'm') : 0,
      background,
    };
  } finally {
    // Every layer, source and listener the map owns stays alive until the map is disposed.
    map.setTarget(undefined);
    map.dispose();
    host.remove();
  }
};
