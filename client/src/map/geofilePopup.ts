// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import type MapBrowserEvent from 'ol/MapBrowserEvent';
import Overlay from 'ol/Overlay';
import type Point from 'ol/geom/Point';
import { GEOFILE_LAYER_PREFIX } from './geofileLayers.ts';
import {
  GEOFILE_DESCRIPTION_PROPERTY,
  GEOFILE_ELEVATION_PROPERTY,
  GEOFILE_LABEL_PROPERTY,
  geofileLabel,
} from './geofileProperties.ts';

/**
 * Columns not worth a line of their own in the popup: the resolved values, which are already shown
 * as the title and subtitle, and the row's internal key.
 */
const HIDDEN_PROPERTIES = new Set<string>([
  GEOFILE_LABEL_PROPERTY,
  GEOFILE_DESCRIPTION_PROPERTY,
  GEOFILE_ELEVATION_PROPERTY,
  'id',
  'geometry',
]);

/** One property as the popup renders it, or nothing when it is not worth a row. */
function displayValue(value: unknown): string | undefined {
  if (value === null || value === undefined) {
    return undefined;
  }
  if (typeof value === 'string') {
    return value.trim().length > 0 ? value : undefined;
  }
  if (typeof value === 'number' || typeof value === 'boolean') {
    return String(value);
  }
  // An object or array in a column: shown as JSON rather than as "[object Object]", which is what
  // a nested attribute from a GeoJSON import would otherwise read as.
  try {
    return JSON.stringify(value);
  } catch {
    return undefined;
  }
}

/**
 * Builds the popup body for one imported point: its name, its description, and every column the
 * source file carried, in the file's own order.
 *
 * The columns are the whole point. A waypoint's value to a caver is in what the GPS unit or the
 * survey spreadsheet recorded beside it — a code, a date, a symbol, a note — and until now the
 * only way to see any of it was to open the import review screen for the file it came from.
 *
 * Everything goes through `textContent`, never `innerHTML`. These strings come from an uploaded
 * file, which is the least trustworthy text in the application: a waypoint named with a script tag
 * is a stored cross-site scripting attempt that would otherwise fire for every viewer who happened
 * to click near it.
 *
 * Pure DOM construction, no OpenLayers, so it is unit-testable on its own.
 */
export function geofilePopupNodes(props: Record<string, unknown>): Node[] {
  const nodes: Node[] = [];
  /** Values already printed above the column list, so they are not printed twice. */
  const shown = new Set<string>();

  const title = document.createElement('div');
  title.className = 'map-geofile-popup-title';
  title.textContent = geofileLabel(props) ?? '—';
  if (title.textContent !== '—') {
    shown.add(title.textContent);
  }
  nodes.push(title);

  const description = props[GEOFILE_DESCRIPTION_PROPERTY];
  if (typeof description === 'string' && description.trim().length > 0) {
    const element = document.createElement('div');
    element.className = 'map-geofile-popup-description';
    element.textContent = description;
    shown.add(description);
    nodes.push(element);
  }

  const elevation = props[GEOFILE_ELEVATION_PROPERTY];
  if (typeof elevation === 'number' && Number.isFinite(elevation)) {
    const element = document.createElement('div');
    element.className = 'map-geofile-popup-elevation';
    // Rounded, because a metre is already finer than a handheld GPS knows and the extra digits
    // read as a precision the reading does not have.
    element.textContent = `${Math.round(elevation)} m`;
    // The number as the column holds it, not as it is printed: `812` hides a column reading
    // `812`, and `812 m` would match nothing.
    shown.add(String(elevation));
    shown.add(String(Math.round(elevation)));
    nodes.push(element);
  }

  const table = document.createElement('dl');
  table.className = 'map-geofile-popup-properties';
  let rows = 0;
  for (const [key, raw] of Object.entries(props)) {
    if (HIDDEN_PROPERTIES.has(key)) {
      continue;
    }
    const value = displayValue(raw);
    if (value === undefined) {
      continue;
    }
    // The column the name, description or altitude was READ OUT OF, printed again underneath it.
    // Which column that was is the server's decision — it is the same folded-key search the import
    // review uses — so it is not knowable here by name; but it is knowable by value, and a column
    // whose value is already on screen a line above adds nothing whatever it is called. Measured
    // on an ordinary GPX: every waypoint listed `name` and `ele` a second time.
    if (shown.has(value)) {
      continue;
    }
    const term = document.createElement('dt');
    term.textContent = key;
    const definition = document.createElement('dd');
    definition.textContent = value;
    table.append(term, definition);
    rows++;
  }

  if (rows > 0) {
    nodes.push(table);
  }

  return nodes;
}

/**
 * Click-to-open detail for points of an imported file, dismissed by clicking anywhere else.
 * Returns a detach fn.
 */
export function attachGeofilePopup(map: Map): () => void {
  const element = document.createElement('div');
  element.className = 'map-geofile-popup';
  // stopEvent so that scrolling a long property list, or selecting text out of it, does not reach
  // the map underneath and pan it.
  const overlay = new Overlay({
    element,
    positioning: 'bottom-center',
    offset: [0, -14],
    stopEvent: true,
    autoPan: { animation: { duration: 200 } },
  });
  map.addOverlay(overlay);

  const handler = (event: MapBrowserEvent) => {
    const feature = map.forEachFeatureAtPixel(event.pixel, (f) => f, {
      hitTolerance: 6,
      layerFilter: (layer) => (layer.get('id') as string | undefined)?.startsWith(GEOFILE_LAYER_PREFIX) === true,
    });

    // Only points get a balloon. A track's line is hit anywhere along its length, so anchoring a
    // popup to the click would put it in a different place each time for the same object, and a
    // property list about "the whole of this track" is not what somebody clicking a bend wants.
    const geometry = feature?.getGeometry();
    if (!feature || geometry?.getType() !== 'Point') {
      overlay.setPosition(undefined);
      return;
    }

    element.replaceChildren(...geofilePopupNodes(feature.getProperties()));
    overlay.setPosition((geometry as Point).getCoordinates());
  };

  map.on('singleclick', handler);
  return () => {
    map.un('singleclick', handler);
    map.removeOverlay(overlay);
  };
}
