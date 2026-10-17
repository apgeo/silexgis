// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * The symbol files bundled beside the application (`public/feature_symbols`), in the order an
 * administrator choosing one for a feature kind is offered them. A kind names one by file name;
 * the server knows only the name's shape, so this list is what the choice is made from — and a
 * test holds it to the files actually in the folder, so a symbol added there is offered here.
 */
export const bundledFeatureSymbols = [
  'arrows.png',
  'bivouac.png',
  'calm.png',
  'chimney.png',
  'continuation.png',
  'dedritus.png',
  'desobstruction.png',
  'driller.png',
  'exploration_point.png',
  'flag.png',
  'fracture_line.png',
  'generic_feature.png',
  'lake.png',
  'peak.png',
  'pit.png',
  'pitch.png',
  'sinkhole.png',
  'tunnel.png',
  'water_flow.png',
] as const;
