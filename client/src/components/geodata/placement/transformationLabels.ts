// SPDX-License-Identifier: AGPL-3.0-or-later
import type { PlacementTransformation } from '../../../geodata/placement/placement.ts';

/** What each transformation is called on screen, and the one line that says what it does. */
export const TRANSFORMATION_KEYS: Readonly<Record<PlacementTransformation, { name: string; explain: string }>> = {
  similarity: {
    name: 'geodata.placement.transformationNames.similarity',
    explain: 'geodata.placement.transformationExplain.similarity',
  },
  affine: {
    name: 'geodata.placement.transformationNames.affine',
    explain: 'geodata.placement.transformationExplain.affine',
  },
  homography: {
    name: 'geodata.placement.transformationNames.homography',
    explain: 'geodata.placement.transformationExplain.homography',
  },
  polynomial2: {
    name: 'geodata.placement.transformationNames.polynomial2',
    explain: 'geodata.placement.transformationExplain.polynomial2',
  },
  polynomial3: {
    name: 'geodata.placement.transformationNames.polynomial3',
    explain: 'geodata.placement.transformationExplain.polynomial3',
  },
  tps: {
    name: 'geodata.placement.transformationNames.tps',
    explain: 'geodata.placement.transformationExplain.tps',
  },
};
