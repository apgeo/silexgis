// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * The translation key naming one class of one terrain measure. Class codes are unique within a
 * measure but not across measures — position and landform both have a "hollow", and they are
 * different things — so landform classes take the names the landform picture's own legend uses,
 * and every other measure its class from the statistics block.
 */
export function terrainClassKey(measure: string, code: string): string {
  return measure === 'landform' ? `terrain.landforms.${code}` : `terrainStats.classes.${code}`;
}
