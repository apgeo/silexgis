// SPDX-License-Identifier: AGPL-3.0-or-later
import type OlLayerBase from 'ol/layer/Base';
import { CENTERLINE_LAYER_ID } from './centerlineLayer.ts';

/**
 * Layers that answer nothing on a hit test, so asking them is pure cost.
 *
 * Hit detection replays a layer's drawing instructions for every geometry whose extent covers
 * the cursor, and a cave centerline is one geometry spanning the whole cave — so it gets
 * replayed in full on every single pointermove. Measured on a real 15.7 km survey that was
 * ~100 ms per mouse move in Chromium and ~80 ms in Firefox, enough that the main thread could
 * not drain the pointer events, and every millisecond of it was discarded: neither the tooltip
 * nor the click handler does anything with a centerline feature. Excluding the layer here takes
 * the same call to ~0.01 ms.
 *
 * If centerlines ever become clickable, remove them from this list — and expect the cost back.
 */
const NON_INTERACTIVE_LAYER_IDS: readonly string[] = [CENTERLINE_LAYER_ID];

/** `layerFilter` for `forEachFeatureAtPixel`: skips layers nothing reads a hit from. */
export function isHitTestable(layer: OlLayerBase): boolean {
  return !NON_INTERACTIVE_LAYER_IDS.includes(layer.get('id') as string);
}
