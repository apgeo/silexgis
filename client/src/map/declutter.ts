// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import type BaseLayer from 'ol/layer/Base';
import BaseVectorLayer from 'ol/layer/BaseVector';
import LayerGroup from 'ol/layer/Group';

/**
 * Thinning out labels and markers that would otherwise land on top of each other.
 *
 * <p>The problem is specific and it is not "too much data". A imported GPS file over a karst
 * plateau is a few thousand waypoints recorded a few metres apart; zoomed out far enough to see
 * the plateau, every one of their names lands in the same handful of pixels, and what is drawn is
 * a grey smear with no readable word in it. Hiding the layer below a zoom threshold would answer
 * that by taking the data away — you would know nothing is there rather than roughly where things
 * are — so what is done instead is to draw as many as fit and drop the ones that would overlap.
 * The markers stay; it is their labels that compete.</p>
 *
 * <p>All layers share ONE declutter group, and that is the point of doing it here rather than
 * layer by layer. OpenLayers declutters each group independently, so a per-layer setting would let
 * a cave entrance's name and an imported waypoint's name sit on the same six pixels — each one
 * uncontested within its own layer, and both unreadable on screen. The group value is a string
 * rather than `true` so that a future layer which genuinely should declutter on its own can say so
 * by naming a different one.</p>
 *
 * <p>Off is a real choice, not a debugging aid: a surveyor checking that every station in a file
 * arrived needs to see every marker's name at once, however ugly, and a view that quietly drops
 * some of them is worse than useless for that.</p>
 */
export const DECLUTTER_GROUP = 'silexgis';

/**
 * Whether decluttering is currently on.
 *
 * Module state rather than a parameter threaded through every layer factory, because layers are
 * created in half a dozen places — the geofile sync, the feature layer, the entrance layer — at
 * moments that have nothing to do with the user changing this setting. A layer created while it is
 * on has to come up decluttered, and the alternative to a shared reading is that each of those
 * places grows a copy of the preference and one of them is eventually missed.
 */
let enabled = true;

/** The value to pass as a layer's `declutter` option right now. */
export function declutterOption(): string | false {
  return enabled ? DECLUTTER_GROUP : false;
}

/** Whether decluttering is on, for a control that shows its state. */
export function isDeclutterEnabled(): boolean {
  return enabled;
}

type DeclutterableLayer = { setDeclutter(value: string | number | boolean): void };

function eachVectorLayer(layers: BaseLayer[], visit: (layer: DeclutterableLayer) => void): void {
  for (const layer of layers) {
    if (layer instanceof LayerGroup) {
      eachVectorLayer(layer.getLayers().getArray(), visit);
    } else if (layer instanceof BaseVectorLayer) {
      visit(layer as unknown as DeclutterableLayer);
    }
  }
}

/**
 * Turns decluttering on or off across every vector layer on the map, including the ones inside the
 * overlay group.
 *
 * Applied to existing layers AND remembered for layers made later, because both halves are needed
 * and each alone produces a state nobody can explain: remembering only would leave the map
 * unchanged until something happened to rebuild a layer, and applying only would leave the next
 * imported file drawn under the opposite rule from everything beside it.
 */
export function setMapDeclutter(map: Map, next: boolean): void {
  enabled = next;
  const value = declutterOption();
  eachVectorLayer(map.getLayers().getArray(), (layer) => layer.setDeclutter(value));
  map.render();
}
