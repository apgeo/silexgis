// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import type Feature from 'ol/Feature';
import type { FeatureLike } from 'ol/Feature';
import GeoJSON from 'ol/format/GeoJSON';
import VectorLayer from 'ol/layer/Vector';
import { transformExtent } from 'ol/proj';
import VectorSource from 'ol/source/Vector';
import { Circle as CircleStyle, Fill, Icon, Stroke, Style, Text } from 'ol/style';
import { fetchMapFeatures, type FeatureType } from '../api/hooks.ts';
import { worldBbox } from '../geo/coords.ts';
import { onSurfaceFeaturesChanged } from '../workspace/surfaceFeatureRefresh.ts';
import {
  getFeatureTypeSymbol,
  onFeatureTypeCatalogChanged,
  setFeatureTypeCatalog,
} from './featureTypeCatalog.ts';
import { handleMapFilterError, mapFilterParams } from './mapFilters.ts';
import { surfaceFeaturePalette as palette } from './markerPalette.ts';
import { symbolImage } from './symbolLibrary.ts';
import { declutterOption } from './declutter.ts';
import { GEOLOGICAL_UNIT_TYPE_CODE, lithologyAppearance } from './lithologyAppearance.ts';

// The cross-kind features overlay. The layer id keeps its historical string:
// saved views persist overlay ids (visibility, opacity, stacking), and renaming
// it would silently drop the layer from every view saved before the rename.
export const SURFACE_FEATURE_LAYER_ID = 'surface-features';

const source = new VectorSource();
const format = new GeoJSON();

/** The live feature source — the edit controller draws into and snaps against it. */
export function getSurfaceFeatureSource(): VectorSource {
  return source;
}

/** Set by the edit controller on a saved feature whose reshaped geometry is not saved yet. */
export const PENDING_RESHAPE = 'pendingReshape';

/**
 * Set by the edit controller on an unsaved edit the server refused on the last Save, until it is
 * saved, discarded or sent again. It is drawn marked: a batch of drawings looks alike, and a message
 * saying one of them was refused is of no use if the editor cannot tell which.
 */
export const SAVE_REFUSED = 'saveRefused';

/**
 * Whether a feature in the source is an unsaved edit: a drawing not yet created, or a saved
 * feature reshaped and not yet stored. Such a feature belongs to the editor, not to the server's
 * answer, so a reload must keep it — the object itself, which the edit history and the pending
 * list refer to — rather than replace it with the server's copy or drop it.
 */
function isPendingEdit(feature: Feature): boolean {
  return feature.get('pendingNew') === true || feature.get(PENDING_RESHAPE) === true;
}

/**
 * Replaces the loaded features with a fresh answer, keeping every unsaved edit (and hiding the
 * server's copy of a feature being reshaped). The removal is announced feature by feature rather
 * than taken on the fast path: the reshaping and snapping tools index the source's features and
 * let go of one only when told it was removed, so a silent clear would leave them dragging and
 * snapping to features no longer on the map.
 */
function replaceLoadedFeatures(fresh: Feature[]): void {
  const held = source.getFeatures().filter(isPendingEdit);
  const heldIds = new Set(held.map((feature) => feature.get('id') as string | undefined).filter(Boolean));
  source.clear();
  source.addFeatures([...fresh.filter((feature) => !heldIds.has(feature.get('id') as string)), ...held]);
}

// Feature-type names and symbols live in a catalog of their own, free of any drawing library,
// because the 3D scene needs the same answers and is loaded as a separate chunk — reaching them
// through this layer would put OpenLayers in it. This layer only has to redraw when they change.
//
// Point styles are built once per symbol and shared by every feature drawn with it: the layer asks
// for a style per feature on every frame, and a shared style is what lets OpenLayers rasterise each
// symbol image once however many features use it.
const pointStyles = new globalThis.Map<string, Style>();

/**
 * The marker of a point feature: its type's symbol (a bundled file or a drawn shape, the shape in
 * the features' brown unless it names a colour), or a plain dot when the type has none or names
 * one this build does not ship.
 */
function pointSymbolStyle(reference: string | null | undefined): Style {
  const image = symbolImage(reference, { color: palette.point });
  const key = image ? `${image.src}|${image.scale}` : 'fallback';
  let style = pointStyles.get(key);
  if (!style) {
    style = new Style({
      // An obstacle, not a competitor: the marker is ALWAYS drawn, and labels move out of its
      // way. Left at the default, decluttering would hide overlapping markers themselves, which
      // would mean switching it on made features vanish — the opposite of what it is for.
      image: image
        ? new Icon({ src: image.src, scale: image.scale, declutterMode: 'obstacle' })
        : new CircleStyle({
            declutterMode: 'obstacle',
            radius: 6,
            fill: new Fill({ color: palette.point }),
            stroke: new Stroke({ color: palette.stroke, width: 2 }),
          }),
    });
    pointStyles.set(key, style);
  }
  return style;
}

// A structural reading's styles, one per symbol, whole-degree orientation and dip: readings of
// one bed share their style, and a map of a hundred readings rasterises a handful of images.
const readingStyles = new globalThis.Map<string, Style>();

/**
 * A structural reading drawn as the geologist's strike-and-dip tick: its symbol turned so the long
 * bar lies along the strike and the short tick points down the dip, with the dip written beside it.
 * The bundled tick is drawn pointing east (dipping 90°), hence the quarter turn taken off. It turns
 * with the view, so it keeps pointing at the true azimuth when the map is rotated.
 */
function readingStyle(reference: string | null | undefined, dipDirection: number, dip: number): Style | undefined {
  const image = symbolImage(reference, { color: palette.point });
  if (!image) return undefined;
  const direction = Math.round(dipDirection) % 360;
  const dipDegrees = Math.round(dip);
  const key = `${image.src}|${image.scale}|${direction}|${dipDegrees}`;
  let style = readingStyles.get(key);
  if (!style) {
    style = new Style({
      image: new Icon({
        src: image.src,
        scale: image.scale,
        rotation: ((direction - 90) * Math.PI) / 180,
        rotateWithView: true,
        declutterMode: 'obstacle',
      }),
      text: new Text({
        text: String(dipDegrees),
        offsetX: 14,
        offsetY: -10,
        font: '600 11px sans-serif',
        fill: new Fill({ color: palette.point }),
        stroke: new Stroke({ color: palette.stroke, width: 3 }),
      }),
    });
    readingStyles.set(key, style);
  }
  return style;
}

// A geological unit's styles, one per class: every unit of one rock shares its style.
const unitStyles = new globalThis.Map<string, Style>();

/**
 * A geological unit filled by its rock class; see {@link lithologyAppearance} for which unit is
 * drawn how. Units of the same class share one style object.
 */
function geologicalUnitStyle(lithology: unknown, karstifiable: unknown): Style {
  const look = lithologyAppearance(lithology, karstifiable);
  const key = `${look.standing}|${look.stroke}`;
  let style = unitStyles.get(key);
  if (!style) {
    style = new Style({
      stroke: new Stroke({ color: look.stroke, width: look.width, lineDash: look.lineDash ?? undefined }),
      fill: look.fill ? new Fill({ color: look.fill }) : undefined,
    });
    unitStyles.set(key, style);
  }
  return style;
}

const selectionHalo = new Style({
  // Always drawn, for the same reason as the markers.
  image: new CircleStyle({
    declutterMode: 'obstacle',
    radius: 14,
    fill: new Fill({ color: palette.highlightHalo }),
    stroke: new Stroke({ color: palette.highlight, width: 2 }),
  }),
});

const refusedHalo = new Style({
  image: new CircleStyle({
    declutterMode: 'obstacle',
    radius: 14,
    fill: new Fill({ color: palette.refusedHalo }),
    stroke: new Stroke({ color: palette.refused, width: 2 }),
  }),
});

const refusedCasing = new Style({ stroke: new Stroke({ color: palette.refused, width: 7 }) });

onFeatureTypeCatalogChanged(() => source.changed()); // restyle loaded features with the fresh names

export function setFeatureTypeSymbols(types: FeatureType[]): void {
  setFeatureTypeCatalog(types);
}

export { getFeatureTypeName, getFeatureTypeNameByCode } from './featureTypeCatalog.ts';

// Selection highlight: the styling reads this id so the highlight survives
// bbox reloads (features are recreated, the id is stable).
let selectedFeatureId: string | null = null;

export function setSelectedSurfaceFeature(id: string | null): void {
  if (selectedFeatureId !== id) {
    selectedFeatureId = id;
    source.changed();
  }
}

export function createSurfaceFeatureLayer(): VectorLayer {
  // Stacking comes from the overlay group's collection order, not a fixed zIndex.
  const layer = new VectorLayer({ source, style: featureStyle, declutter: declutterOption() });
  layer.set('id', SURFACE_FEATURE_LAYER_ID);
  return layer;
}

/** Bbox loading on moveend (debounced), stale responses discarded; returns a detach fn. */
export function attachSurfaceFeatureLoader(map: Map): () => void {
  let requestSeq = 0;
  let timer: number | undefined;

  const load = async () => {
    const view = map.getView();
    const size = map.getSize();
    if (!size) {
      return;
    }
    const extent = transformExtent(view.calculateExtent(size), 'EPSG:3857', 'EPSG:4326');
    const bbox = worldBbox(extent);
    const seq = ++requestSeq;
    try {
      const collection = await fetchMapFeatures(bbox, mapFilterParams());
      if (seq !== requestSeq) {
        return; // a newer request superseded this one
      }
      replaceLoadedFeatures(
        format
          .readFeatures(collection, { featureProjection: 'EPSG:3857' })
          // A protected non-point feature the viewer may not see exactly arrives as a
          // readable row with a null geometry — nothing to draw, list or hit-test here.
          .filter((feature): feature is Feature => feature.getGeometry() !== undefined),
      );
    } catch (error) {
      // Keep previous features on transient errors; next moveend retries. A refused filter is
      // not transient, and what was drawn before it was asked for is not what it selects.
      if (seq === requestSeq) {
        handleMapFilterError(error, () => replaceLoadedFeatures([]));
      }
    }
  };

  const onMoveEnd = () => {
    window.clearTimeout(timer);
    timer = window.setTimeout(() => void load(), 250);
  };

  map.on('moveend', onMoveEnd);
  void load();
  // A write elsewhere in the application invalidates this extent, and this layer is not the only
  // view of those features any more, so the news is subscribed to rather than delivered here.
  const unsubscribeChanges = onSurfaceFeaturesChanged(() => void load());
  return () => {
    map.un('moveend', onMoveEnd);
    window.clearTimeout(timer);
    unsubscribeChanges();
  };
}

function featureStyle(feature: FeatureLike): Style | Style[] {
  const styles = baseFeatureStyle(feature);
  if (feature.get(SAVE_REFUSED) !== true) {
    return styles;
  }
  // Drawn under the feature's own style, so the symbol or line still says what the feature is.
  const geometryType = feature.getGeometry()?.getType();
  const mark = geometryType === 'Point' || geometryType === 'MultiPoint' ? refusedHalo : refusedCasing;
  return [mark, ...(Array.isArray(styles) ? styles : [styles])];
}

function baseFeatureStyle(feature: FeatureLike): Style | Style[] {
  const geometryType = feature.getGeometry()?.getType();
  const selected = selectedFeatureId !== null && feature.get('id') === selectedFeatureId;

  // MultiPoint features (from imported multi-part geodata) style like points: OpenLayers
  // draws the image style at every point of the geometry.
  if (geometryType === 'Point' || geometryType === 'MultiPoint') {
    // A translucent halo behind the symbol marks the selected feature.
    // Server rows carry their symbol reference; pending locally drawn features only carry the
    // armed featureTypeId, resolved through the catalog instead.
    const symbol =
      (feature.get('symbol') as string | null | undefined) ?? getFeatureTypeSymbol(feature.get('featureTypeId'));
    // Only a structural reading carries an orientation; the server sends one when its stored
    // reading is whole, and anything else is drawn as an ordinary point of its kind.
    const dipDirection: unknown = feature.get('dipDirection');
    const dip: unknown = feature.get('dip');
    const marker =
      (typeof dipDirection === 'number' && typeof dip === 'number'
        ? readingStyle(symbol, dipDirection, dip)
        : undefined) ?? pointSymbolStyle(symbol);
    return selected ? [selectionHalo, marker] : [marker];
  }

  // Only a geological unit is filled by its class; the server marks one by its kind's code.
  if (feature.get('typeCode') === GEOLOGICAL_UNIT_TYPE_CODE) {
    const unit = geologicalUnitStyle(feature.get('lithology'), feature.get('karstifiable'));
    return selected
      ? [new Style({ stroke: new Stroke({ color: palette.highlight, width: 6 }) }), unit]
      : unit;
  }

  if (selected) {
    return [
      new Style({ stroke: new Stroke({ color: palette.highlight, width: 6 }) }),
      new Style({
        stroke: new Stroke({ color: palette.line, width: 2.5 }),
        fill: new Fill({ color: palette.highlightHalo }),
      }),
    ];
  }

  return new Style({
    stroke: new Stroke({ color: palette.line, width: 2.5 }),
    fill: new Fill({ color: palette.fill }),
  });
}
