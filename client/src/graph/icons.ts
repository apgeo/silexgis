// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * The pictograms drawn inside the graph's nodes and in its legend: a cave, an entrance, a survey
 * line, a person, a trip's route and so on, so a node says what it is without a key. Drawn for this
 * application on a 24-unit grid as plain strokes in one colour, which the caller chooses (white on a
 * filled node, the secondary text colour on a hollow placeholder).
 *
 * Nothing here knows a drawing library: a renderer takes the SVG text or the data URI and draws it
 * as an image. Each icon is markup whose colour is written `{c}`.
 */

export const GRAPH_ICONS = [
  'cave',
  'entrance',
  'surveyLine',
  'place',
  'document',
  'trip',
  'person',
  'group',
  'map',
  'model',
  'layers',
  'cabinet',
  'tent',
  'tag',
  'attachment',
  'more',
  'lock',
  'unknown',
] as const;
export type GraphIcon = (typeof GRAPH_ICONS)[number];

const FILLED = 'fill="{c}" stroke="none"';

const ICON_MARKUP: Record<GraphIcon, string> = {
  // A hillside with the dark mouth of a cave at its foot.
  cave: `<path d="M2 20 L8.5 7 L12 11.5 L15 8 L22 20 Z"/><path d="M9.5 20 V17.5 A2.5 2.5 0 0 1 14.5 17.5 V20 Z" ${FILLED}/>`,
  // An arched opening with the way in pointing down into it.
  entrance: '<path d="M4 21 V12 A8 8 0 0 1 20 12 V21"/><path d="M12 8 V16"/><path d="M8.5 12.5 L12 16 L15.5 12.5"/>',
  // Survey legs between stations.
  surveyLine:
    `<path d="M3.5 18 L8 9 L14 14 L20.5 5"/><circle cx="3.5" cy="18" r="1.8" ${FILLED}/><circle cx="8" cy="9" r="1.8" ${FILLED}/>` +
    `<circle cx="14" cy="14" r="1.8" ${FILLED}/><circle cx="20.5" cy="5" r="1.8" ${FILLED}/>`,
  // A map pin: any other place on the ground or underground.
  place: '<path d="M12 21.5 C12 21.5 5 15 5 9.5 A7 7 0 0 1 19 9.5 C19 15 12 21.5 12 21.5 Z"/><circle cx="12" cy="9.5" r="2.5"/>',
  document: '<path d="M6 2.5 H14 L18.5 7 V21.5 H6 Z"/><path d="M14 2.5 V7 H18.5"/><path d="M9 12 H15.5 M9 15.5 H15.5 M9 19 H13"/>',
  // A route from a start to a flag.
  trip: `<circle cx="5" cy="19" r="2" ${FILLED}/><path d="M7 19 H13 A3.5 3.5 0 0 0 13 12 H10 A3 3 0 0 1 10 6 H18"/><path d="M18 11 V2.5"/><path d="M18 3 H21.5 L20.3 4.75 L21.5 6.5 H18"/>`,
  person: '<circle cx="12" cy="7.5" r="4"/><path d="M4 21 V19.5 A6 6 0 0 1 10 13.5 H14 A6 6 0 0 1 20 19.5 V21"/>',
  group:
    '<circle cx="9" cy="8" r="3.5"/><path d="M2.5 20.5 V19.5 A5 5 0 0 1 7.5 14.5 H10.5 A5 5 0 0 1 15.5 19.5 V20.5"/>' +
    '<circle cx="17" cy="7" r="2.8"/><path d="M17.5 13.5 A4.5 4.5 0 0 1 21.5 18 V19.5"/>',
  // A folded paper map.
  map: '<path d="M3 6.5 L9 4 L15 6.5 L21 4 V17.5 L15 20 L9 17.5 L3 20 Z"/><path d="M9 4 V17.5 M15 6.5 V20"/>',
  // A cube: a three-dimensional survey model.
  model: '<path d="M12 2.5 L20.5 7 V17 L12 21.5 L3.5 17 V7 Z"/><path d="M3.5 7 L12 11.5 L20.5 7 M12 11.5 V21.5"/>',
  // Stacked layers: a geodata file.
  layers: '<path d="M12 3 L21 8 L12 13 L3 8 Z"/><path d="M3 12.5 L12 17.5 L21 12.5"/><path d="M3 16.5 L12 21.5 L21 16.5"/>',
  // A filing cabinet with two drawers.
  cabinet: '<rect x="4.5" y="2.5" width="15" height="19" rx="1.5"/><path d="M4.5 12 H19.5"/><path d="M10 7 H14 M10 16.5 H14"/>',
  // A tent: an expedition's camp.
  tent: `<path d="M2.5 20.5 L12 4 L21.5 20.5 Z"/><path d="M12 12 L9 20.5 H15 Z" ${FILLED}/>`,
  tag: `<path d="M3 3.5 H11.5 L21 13 L13 21 L3.5 11.5 Z"/><circle cx="7.5" cy="8" r="1.6" ${FILLED}/>`,
  // A paper clip: a file attached to an item.
  attachment:
    '<path d="M20 11.5 L12 19.5 A5 5 0 0 1 5 12.5 L13.5 4 A3.3 3.3 0 0 1 18.2 8.7 L9.7 17.2 A1.7 1.7 0 0 1 7.3 14.8 L15.1 7"/>',
  more: '<path d="M12 5 V19 M5 12 H19"/>',
  lock: '<rect x="5" y="10.5" width="14" height="10.5" rx="1.5"/><path d="M8 10.5 V7.5 A4 4 0 0 1 16 7.5 V10.5"/>',
  unknown: `<path d="M9 9 A3 3 0 1 1 13.5 11.6 C12.5 12.2 12 12.9 12 14 V15"/><circle cx="12" cy="18.5" r="1.3" ${FILLED}/>`,
};

/** The whole SVG document of one icon in one colour. */
export function iconSvg(icon: GraphIcon, colour: string): string {
  const body = ICON_MARKUP[icon].replaceAll('{c}', colour);
  return (
    '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" width="24" height="24" fill="none" ' +
    `stroke="${colour}" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">${body}</svg>`
  );
}

const uris = new Map<string, string>();

/** The icon as a data URI, which every canvas and every `<img>` can draw; built once per icon and colour. */
export function iconDataUri(icon: GraphIcon, colour: string): string {
  const key = `${icon}|${colour}`;
  let uri = uris.get(key);
  if (uri === undefined) {
    uri = `data:image/svg+xml;charset=utf-8,${encodeURIComponent(iconSvg(icon, colour))}`;
    uris.set(key, uri);
  }
  return uri;
}
