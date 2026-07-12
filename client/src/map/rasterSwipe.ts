// SPDX-License-Identifier: AGPL-3.0-or-later
import type { EventsKey } from 'ol/events';
import type Layer from 'ol/layer/Layer';
import { unByKey } from 'ol/Observable';
import type RenderEvent from 'ol/render/Event';
import { getOverlayGroup, getWorkspaceMap } from './mapContext.ts';
import { RASTER_LAYER_PREFIX } from './rasterLayers.ts';

// Swipe-compare for georeferenced raster overlays: while active, every raster
// layer renders only to the RIGHT of a vertical divider, so the base map and
// other overlays show through on the left. Raster layers render through WebGL,
// where clipping is the scissor test toggled around each layer's render pass.
// Ephemeral view state: deliberately not part of saved views.

let fraction = 0.5;
let active = false;
let layerKeys: EventsKey[] = [];
let collectionKeys: EventsKey[] = [];

export function setRasterSwipeFraction(value: number): void {
  fraction = Math.min(1, Math.max(0, value));
  if (active) {
    getWorkspaceMap().render();
  }
}

function prerender(event: RenderEvent): void {
  const gl = event.context as WebGLRenderingContext | null;
  if (!gl || typeof gl.enable !== 'function') {
    return;
  }
  const left = Math.round(gl.drawingBufferWidth * fraction);
  gl.enable(gl.SCISSOR_TEST);
  gl.scissor(left, 0, gl.drawingBufferWidth - left, gl.drawingBufferHeight);
}

function postrender(event: RenderEvent): void {
  const gl = event.context as WebGLRenderingContext | null;
  if (!gl || typeof gl.disable !== 'function') {
    return;
  }
  gl.disable(gl.SCISSOR_TEST);
}

function bindRasterLayers(): void {
  unByKey(layerKeys);
  layerKeys = [];
  for (const layer of getOverlayGroup().getLayers().getArray()) {
    if ((layer.get('id') as string | undefined)?.startsWith(RASTER_LAYER_PREFIX)) {
      const raster = layer as Layer;
      layerKeys.push(raster.on('prerender', prerender), raster.on('postrender', postrender));
    }
  }
}

export function setRasterSwipeActive(value: boolean): void {
  if (value === active) {
    return;
  }
  active = value;
  const collection = getOverlayGroup().getLayers();

  if (active) {
    bindRasterLayers();
    // Raster layers come and go with the catalog toggles — keep the clip bound.
    collectionKeys = [collection.on('add', bindRasterLayers), collection.on('remove', bindRasterLayers)];
  } else {
    unByKey(layerKeys);
    unByKey(collectionKeys);
    layerKeys = [];
    collectionKeys = [];
  }
  getWorkspaceMap().render();
}
