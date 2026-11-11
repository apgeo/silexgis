// SPDX-License-Identifier: AGPL-3.0-or-later
import type { Scene3DPointCloudColoring } from './scene3dEngine.ts';

/**
 * The colouring a cloud opens with: what the scanner recorded if it recorded anything — a colour
 * first, a return strength otherwise — and height, which every cloud has, when it recorded
 * neither. Decided from the facts of the file rather than remembered, because the right answer is
 * a property of the scan and not of the person looking at it.
 */
export function defaultColoring(hasRgb: boolean, hasIntensity: boolean): Scene3DPointCloudColoring {
  return hasRgb ? 'rgb' : hasIntensity ? 'intensity' : 'height';
}
