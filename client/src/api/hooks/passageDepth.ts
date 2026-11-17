// SPDX-License-Identifier: AGPL-3.0-or-later
import { api } from '../client.ts';
import type { components } from '../schema';
import { unwrap } from './shared.ts';

/** The passage a map window holds, with the class of rock cover over each stretch. */
export type PassageDepthWindow = components['schemas']['PassageDepthWindowDto'];

/** One cave's passage in one cover class. */
export type PassageDepthLine = components['schemas']['PassageDepthLineDto'];

/**
 * Imperative fetch for the depth-coloured passage layer (not a hook, for the same reason as the
 * karst layers: it is asked again on every pan, and a superseded window is aborted rather than
 * cached).
 *
 * A reader without the terrain right is refused, and the layer says so rather than drawing nothing.
 */
export async function fetchMapPassageDepth(bbox: string, signal?: AbortSignal): Promise<PassageDepthWindow> {
  return unwrap(api.GET('/api/v1/map/passage-depth', { params: { query: { bbox } }, signal }));
}
