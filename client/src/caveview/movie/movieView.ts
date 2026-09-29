// SPDX-License-Identifier: AGPL-3.0-or-later
import type { CaveViewer, CaveViewLayerGetter, Cv2Namespace } from '../loadCaveView.ts';
import { MOVIE_LABEL_SIZE_RANGE, MOVIE_VIEW_LAYERS, type MovieSettings, type MovieViewLayer } from './movieSettings.ts';

/**
 * Putting a movie's view settings on a viewer, and taking them off again.
 *
 * <b>Written to the viewer's live properties, never saved as its defaults.</b> The viewer keeps a
 * "save as default" state of its own and re-applies it on every load; a movie's choices written
 * there would follow the reader into every model they open afterwards. So these are set after a
 * model has loaded, and an export that set them puts back exactly what it found.
 *
 * <b>Only what differs is written.</b> Some of these properties are expensive to set — a shading
 * recolours the whole model — and the preview applies them on every change the reader makes, so a
 * value already in place is left alone.
 */

/** The viewer as far as its view settings go. */
export type MovieViewViewer = Pick<
  CaveViewer,
  | Exclude<MovieViewLayer, 'HUD' | 'fog'>
  | CaveViewLayerGetter
  | 'HUD'
  | 'fog'
  | 'shadingMode'
  | 'cameraType'
  | 'linewidth'
  | 'zScale'
  | 'hasRealTerrain'
>;

/** The namespace's constants the view settings are spelled in. */
export type MovieViewConstants = Pick<
  Cv2Namespace,
  'CAMERA_PERSPECTIVE' | 'CAMERA_ORTHOGRAPHIC' | 'SHADING_DEPTH' | 'SHADING_DEPTH_CURSOR'
>;

/** The viewer as far as how its markers' labels are drawn goes. */
export type MovieMarkerLabelViewer = Pick<
  CaveViewer,
  'liveMarkerLabels' | 'liveMarkerLabelSize' | 'liveMarkerLabelBacking'
>;

type Writable = Record<string, unknown>;

/**
 * The properties a movie's view settings set on this viewer and the values they should have, in
 * the order they are written. A layer the loaded model does not have is left out — the viewer has
 * nothing to show on it — as is a depth shading on a model without real terrain, which the viewer
 * cannot draw. A shading the reader left to the viewer is `viewerShading`, the one the viewer drew
 * the model in before anything was set; with none given it is left as it stands.
 */
function wantedView(
  viewer: MovieViewViewer,
  view: MovieSettings['view'],
  constants: MovieViewConstants,
  viewerShading: number | null,
): [string, unknown][] {
  const wanted: [string, unknown][] = [];
  for (const { key, has } of MOVIE_VIEW_LAYERS) {
    if (has === null || viewer[has]) {
      wanted.push([key, view[key]]);
    }
  }
  const shading = view.shadingMode ?? viewerShading;
  const depthShading = shading === constants.SHADING_DEPTH || shading === constants.SHADING_DEPTH_CURSOR;
  if (shading !== null && (!depthShading || viewer.hasRealTerrain === true)) {
    wanted.push(['shadingMode', shading]);
  }
  wanted.push([
    'cameraType',
    view.camera === 'orthographic' ? constants.CAMERA_ORTHOGRAPHIC : constants.CAMERA_PERSPECTIVE,
  ]);
  wanted.push(['linewidth', view.linewidth]);
  wanted.push(['zScale', view.zScale]);
  return wanted;
}

/**
 * Sets a movie's view settings on a loaded viewer and answers how to put back what they replaced.
 *
 * The answer restores only what this changed, to the values it found — so a preview whose own
 * view is already the movie's is left exactly as it was.
 *
 * `viewerShading` is the shading the viewer drew the model in when it loaded, which is what "as the
 * viewer draws it" means. The preview passes it: it applies the settings again on every change and
 * keeps none of the answers, so without it a shading chosen and then left to the viewer again would
 * stay on the preview, and be recorded into the movie, while the settings say otherwise.
 */
export function applyMovieView(
  viewer: MovieViewViewer,
  view: MovieSettings['view'],
  constants: MovieViewConstants,
  viewerShading: number | null = null,
): () => void {
  const target = viewer as unknown as Writable;
  const replaced: [string, unknown][] = [];
  for (const [key, value] of wantedView(viewer, view, constants, viewerShading)) {
    if (target[key] !== value) {
      replaced.push([key, target[key]]);
      target[key] = value;
    }
  }
  return () => {
    // Backwards, so that a property whose meaning depends on one set before it is put back first.
    for (const [key, value] of [...replaced].reverse()) {
      if (target[key] !== value) {
        target[key] = value;
      }
    }
  };
}

/** Which of a movie's layers the loaded model has at all, for the controls that switch them. */
export function movieLayersAvailable(viewer: Pick<CaveViewer, CaveViewLayerGetter>): Set<MovieViewLayer> {
  return new Set(MOVIE_VIEW_LAYERS.filter(({ has }) => has === null || viewer[has]).map(({ key }) => key));
}

/**
 * The size a marker's label is drawn at, in device pixels of what is being drawn, for a movie
 * whose labels are `labelSize` pixels of its frame.
 *
 * `scale` is how many device pixels of the drawing one pixel of the frame is: 1 while a frame is
 * being captured, since a capture is drawn at the frame's own size; the preview's device width over
 * the frame's width while the preview is on screen, so the preview shows the label at the share of
 * the picture the movie will give it. Held to what the viewer's glyph atlas can draw.
 */
export function movieLabelDevicePixels(labelSize: number, scale: number): number {
  const size = labelSize * (Number.isFinite(scale) && scale > 0 ? scale : 1);
  return Math.min(MOVIE_LABEL_SIZE_RANGE.max, Math.max(1, Math.round(size * 100) / 100));
}

/**
 * Sets how the markers' labels are drawn for a movie and answers how to put back what was there.
 *
 * With labels off the viewer is told to draw none, rather than being handed empty labels: an empty
 * label can still draw its plate.
 */
export function applyMovieMarkerLabels(
  viewer: MovieMarkerLabelViewer,
  cavers: Pick<MovieSettings['cavers'], 'labels' | 'labelSize' | 'labelPlate'>,
  scale: number,
): () => void {
  const saved = {
    labels: viewer.liveMarkerLabels,
    size: viewer.liveMarkerLabelSize,
    backing: viewer.liveMarkerLabelBacking,
  };
  const labels = cavers.labels !== 'off';
  if (viewer.liveMarkerLabels !== labels) {
    viewer.liveMarkerLabels = labels;
  }
  if (viewer.liveMarkerLabelBacking !== cavers.labelPlate) {
    viewer.liveMarkerLabelBacking = cavers.labelPlate;
  }
  const size = movieLabelDevicePixels(cavers.labelSize, scale);
  if (viewer.liveMarkerLabelSize !== size) {
    viewer.liveMarkerLabelSize = size;
  }
  return () => {
    // The viewer has no value meaning "the default size again"; what was read is written back.
    if (viewer.liveMarkerLabelSize !== saved.size) {
      viewer.liveMarkerLabelSize = saved.size;
    }
    if (viewer.liveMarkerLabelBacking !== saved.backing) {
      viewer.liveMarkerLabelBacking = saved.backing;
    }
    if (viewer.liveMarkerLabels !== saved.labels) {
      viewer.liveMarkerLabels = saved.labels;
    }
  };
}
