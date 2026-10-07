// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';
import type { CaveViewer, CaveViewLabelText, CaveViewLiveMarker, CaveViewRef } from '../loadCaveView.ts';
import { syncLiveMarkers, type DrawnMarker } from '../liveMarkerSync.ts';
import { openMovieEncoder, type MovieEncoder } from './encode/movieEncoder.ts';
import { drawMovieCaptions, movieCaptionColors, movieCaptionsAt } from './movieCaptions.ts';
import { movieParty, type MovieParty, type MovieTripData } from './movieParty.ts';
import { movieSize, type MovieSettings } from './movieSettings.ts';
import { movieFrames, type MovieTimeline } from './movieTimeline.ts';
import {
  applyMovieMarkerLabels,
  applyMovieView,
  settleCamera,
  type MovieMarkerLabelViewer,
  type MovieViewConstants,
  type MovieViewViewer,
} from './movieView.ts';

/**
 * Recording a movie from a viewer that is already showing the model: every frame rendered on
 * demand through the viewer's capture session, captioned, and handed to an encoder.
 *
 * <b>Each frame is a function of its number.</b> The replay instant, the camera's angle and how far
 * the markers have slid are all read off the frame schedule, never off a clock — a frame that took
 * a second to render in software makes a slow export, not an uneven movie.
 *
 * <b>The viewer is somebody's preview, and it is handed back as it was found.</b> The view
 * settings, how the labels are drawn, the markers and trails standing on the model and the angle
 * of the camera are all put back when the recording ends, however it ends: finished, failed or
 * cancelled. The capture session itself hands back the size, the pixel ratio and the marker clock.
 *
 * <b>Cancelling is not a failure.</b> A cancelled recording rejects with an `AbortError`, which
 * {@link isMovieAbort} recognises, and logs nothing: the development diagnostics and the browser
 * tests record every console error, and a reader pressing Cancel is not one.
 */

/** Everything of the viewer a recording touches. */
export type MovieRecorderViewer = MovieViewViewer &
  MovieMarkerLabelViewer &
  Pick<
    CaveViewer,
    | 'beginCapture'
    | 'captureFrame'
    | 'endCapture'
    | 'getCameraAngles'
    | 'setCameraAngles'
    | 'addLiveMarker'
    | 'moveLiveMarker'
    | 'removeLiveMarker'
    | 'getLiveMarkers'
    | 'setLiveMarkerClusterLabel'
    | 'addTrail'
    | 'updateTrail'
    | 'removeTrail'
    | 'getTrails'
  >;

type ClusterLabel = ((markers: readonly CaveViewLiveMarker[]) => CaveViewLabelText | null) | null;

/**
 * How far a recording has got.
 *
 * `done` and `total` count the palette samples and the frames together — the whole of the work, for
 * a bar and a time left. `step` and `steps` count the stage's own: samples taken of the samples a
 * palette wants while sampling, frames of the movie's frames while rendering. A reader is told the
 * second pair, since a GIF of 20 frames is 20 frames, not 36.
 */
export interface MovieProgress {
  stage: 'sampling' | 'rendering' | 'finishing';
  done: number;
  total: number;
  step: number;
  steps: number;
}

export interface MovieRecording {
  viewer: MovieRecorderViewer;
  constants: MovieViewConstants;
  settings: MovieSettings;
  /** In the order the reader chose them, which is what keeps each trip's colour. */
  trips: readonly MovieTripData[];
  /**
   * The markers the reader left out of this movie, by the id a caver's marker has in a movie.
   * Required, so that a caller cannot record everybody by forgetting to say who was left out.
   */
  excluded: ReadonlySet<string>;
  timeline: MovieTimeline;
  surveyModelId: string;
  /** The title caption, as the preview draws it; null when the caption is off. */
  title: string | null;
  words: { t: TFunction; language: string; today: string };
  /**
   * What the viewer's grouped markers were labelled with before the recording, set back when it
   * ends — the viewer can be told a cluster label but not asked for the one it has.
   */
  clusterLabelAfter: ClusterLabel;
  signal?: AbortSignal;
  onProgress?: (progress: MovieProgress) => void;
  /** Seams for tests; the defaults are the real encoder, a detached canvas and a task yield. */
  openEncoder?: typeof openMovieEncoder;
  createCanvas?: (width: number, height: number) => HTMLCanvasElement;
  nextTask?: () => Promise<void>;
}

/** Whether a recording ended because it was cancelled, rather than because something failed. */
export function isMovieAbort(error: unknown): boolean {
  return (
    typeof error === 'object'
    && error !== null
    && (error as { name?: unknown }).name === 'AbortError'
  );
}

function aborted(): DOMException {
  return new DOMException('The movie export was cancelled.', 'AbortError');
}

/**
 * Yields to the event loop through a message rather than a timer: a timer in a background tab is
 * held back to once a second, and a recording left to run behind another tab would crawl.
 */
export function nextTask(): Promise<void> {
  return new Promise((resolve) => {
    const channel = new MessageChannel();
    channel.port1.onmessage = () => {
      channel.port1.close();
      resolve();
    };
    channel.port2.postMessage(null);
  });
}

/**
 * Which frames a GIF's palette is sampled from: `wanted` of them, evenly spread from the first to
 * the last, so the palette sees the model from every angle the camera turns through and the party
 * at every stage of the replay.
 */
export function movieSampleFrames(count: number, wanted: number): number[] {
  if (wanted <= 0 || count <= 0) {
    return [];
  }
  if (wanted === 1) {
    return [0];
  }
  return Array.from({ length: wanted }, (_, index) => Math.round((index * (count - 1)) / (wanted - 1)));
}

const TRAIL_PREFIX = 'movie-export:';

/** A reference as a key: a split path cannot be mistaken for a dotted one, so it forces a move. */
function refKey(ref: CaveViewRef): string {
  return typeof ref === 'string' ? ref : `\u0000${ref.join('\u0000')}`;
}

/**
 * Records the movie and answers its file.
 *
 * Rejects with the encoder's error when encoding fails, with the viewer's when capturing does, and
 * with an `AbortError` when `signal` is aborted — in every case after the viewer has been put back.
 */
export async function recordMovie(recording: MovieRecording): Promise<Blob> {
  const {
    viewer,
    constants,
    settings,
    trips,
    excluded,
    timeline,
    surveyModelId,
    title,
    words,
    signal,
    onProgress,
    openEncoder = openMovieEncoder,
    createCanvas = detachedCanvas,
    nextTask: yieldTask = nextTask,
  } = recording;
  const checkAborted = () => {
    if (signal?.aborted) {
      throw aborted();
    }
  };
  checkAborted();

  const { width, height } = movieSize(settings);
  const frames = movieFrames(timeline, settings);
  // Opened before the viewer is touched, so an encoder this browser cannot open leaves the
  // preview exactly as it was without anything having to be put back.
  const encoder: MovieEncoder = await openEncoder(settings.format, {
    width,
    height,
    fps: frames.fps,
    quality: settings.quality,
    reservedColors: movieCaptionColors(),
  });

  // What is standing on the model now, to be put back.
  const markersBefore = viewer.getLiveMarkers().map((marker) => ({ ...marker }));
  const trailsShownBefore = viewer.getTrails().filter((trail) => trail.visible).map((trail) => trail.id);
  // A turn of the preview to its starting view is animated. Read mid-turn, these angles would start
  // the movie — and hand the preview back — tilted between the two views.
  settleCamera(viewer);
  const cameraBefore = viewer.getCameraAngles();

  let restoreView: (() => void) | null = null;
  let restoreLabels: (() => void) | null = null;
  let drawn = new Map<string, DrawnMarker>(
    markersBefore.map((marker) => [
      marker.id,
      { station: refKey(marker.ref), label: marker.label, color: marker.color ?? '' },
    ]),
  );
  const drawnTrails = new Map<string, string>();
  let capturing = false;

  /**
   * Hands the viewer back, trying every step whatever an earlier one did — a session left open
   * would leave the preview drawing nothing — and answers the steps that failed.
   */
  const putBack = (): unknown[] => {
    const failures: unknown[] = [];
    const attempt = (step: () => void) => {
      try {
        step();
      } catch (error) {
        failures.push(error);
      }
    };
    attempt(() => encoder.close());
    // While the session is still open, so the one view the viewer draws when it ends is the
    // preview as it was, not the last frame of the movie.
    attempt(() =>
      putBackScene(viewer, drawn, markersBefore, drawnTrails, trailsShownBefore, recording.clusterLabelAfter),
    );
    attempt(() => restoreView?.());
    attempt(() => viewer.setCameraAngles(cameraBefore));
    if (capturing) {
      attempt(() => viewer.endCapture());
    }
    // After the session, which hands the label size back as it was when the session opened; this
    // is what the preview had before the recording set anything. The angle is set again in case
    // ending the session moved the camera.
    attempt(() => restoreLabels?.());
    attempt(() => viewer.setCameraAngles(cameraBefore));
    return failures;
  };

  let file: Blob;
  try {
    // A cancel that arrived while the encoder was opening still closes it.
    checkAborted();
    restoreView = applyMovieView(viewer, settings.view, constants);
    // The preview's trails show its own moment; the movie draws its own, so the preview's are
    // hidden rather than left standing in every frame.
    for (const id of trailsShownBefore) {
      viewer.updateTrail(id, null, { visible: false });
    }
    viewer.beginCapture({ width, height });
    capturing = true;
    // A capture is drawn at the frame's own size, so a label of so many frame pixels is that many
    // device pixels of the drawing — no scaling here, which is what keeps it from being scaled twice.
    restoreLabels = applyMovieMarkerLabels(viewer, settings.cavers, 1);

    const canvas = createCanvas(width, height);
    const context = canvas.getContext('2d', { willReadFrequently: encoder.samplesWanted > 0 });
    if (context === null) {
      throw new Error('A movie frame could not be composed: the browser gave no 2D canvas.');
    }
    const transitionMs = settings.cavers.transitionS * 1000;

    const syncTrails = (party: MovieParty) => {
      for (const [id, trail] of party.trails) {
        const key = `${trail.color}\n${trail.stations.join('\n')}`;
        const before = drawnTrails.get(id);
        if (before === undefined) {
          viewer.addTrail(TRAIL_PREFIX + id, trail.stations, { color: trail.color });
        } else if (before !== key) {
          viewer.updateTrail(TRAIL_PREFIX + id, trail.stations, { color: trail.color });
        }
        drawnTrails.set(id, key);
      }
      for (const id of [...drawnTrails.keys()]) {
        if (!party.trails.has(id)) {
          viewer.removeTrail(TRAIL_PREFIX + id);
          drawnTrails.delete(id);
        }
      }
    };

    /** Stages frame `index` on the viewer, captures it into the composite and captions it. */
    const compose = (index: number, moveMs: number, advanceMs: number) => {
      const frame = frames.frame(index);
      const party = movieParty(trips, timeline.instants(frame.position), surveyModelId, {
        settings,
        t: words.t,
        language: words.language,
        today: words.today,
        excluded,
      });
      // Set before the markers move, so the groups their moves form are named by this frame's party.
      viewer.setLiveMarkerClusterLabel((markers) => party.clusterLabel(markers.map((marker) => marker.id)));
      drawn = syncLiveMarkers(viewer, drawn, party.markers, { duration: moveMs });
      syncTrails(party);
      viewer.captureFrame({
        azimuth: cameraBefore.azimuth + frame.azimuthOffset,
        polar: cameraBefore.polar,
        advance: advanceMs,
        into: context,
      });
      drawMovieCaptions(
        context,
        width,
        height,
        movieCaptionsAt(settings, title, party, timeline, frame, words),
      );
    };

    const total = encoder.samplesWanted + frames.count;
    let done = 0;

    if (encoder.samplesWanted > 0) {
      // Each sample is placed at once — nobody sliding — since a palette is about the colours a
      // frame holds, not about how it was arrived at.
      const samples: ImageData[] = [];
      for (const index of movieSampleFrames(frames.count, encoder.samplesWanted)) {
        compose(index, 0, 0);
        samples.push(context.getImageData(0, 0, width, height));
        onProgress?.({ stage: 'sampling', done: ++done, total, step: samples.length, steps: encoder.samplesWanted });
        await yieldTask();
        checkAborted();
      }
      await encoder.prime(samples);
      checkAborted();
    }

    for (let index = 0; index < frames.count; index++) {
      const frame = frames.frame(index);
      // The first frame puts everybody where the movie starts at once — which is also what brings
      // the party back from wherever the palette samples left it.
      compose(index, index === 0 ? 0 : transitionMs, frame.advanceMs);
      await encoder.addFrame(canvas, index);
      onProgress?.({ stage: 'rendering', done: ++done, total, step: index + 1, steps: frames.count });
      await yieldTask();
      checkAborted();
    }

    onProgress?.({ stage: 'finishing', done, total, step: frames.count, steps: frames.count });
    file = await encoder.finish();
    checkAborted();
  } catch (error) {
    // A step of putting back that fails is not reported here: the recording's own failure is the
    // one the reader is owed, and a recording cancelled because its dialog is closing may be
    // putting things back on a viewer that is already being taken down.
    putBack();
    throw error;
  }
  const failures = putBack();
  if (failures.length > 0) {
    throw failures[0];
  }
  return file;
}

/** The markers, trails and group labels of the preview, as they stood before the recording. */
function putBackScene(
  viewer: MovieRecorderViewer,
  drawn: ReadonlyMap<string, DrawnMarker>,
  markersBefore: readonly CaveViewLiveMarker[],
  drawnTrails: ReadonlyMap<string, string>,
  trailsShownBefore: readonly string[],
  clusterLabelAfter: ClusterLabel,
): void {
  for (const id of drawnTrails.keys()) {
    viewer.removeTrail(TRAIL_PREFIX + id);
  }
  for (const id of trailsShownBefore) {
    viewer.updateTrail(id, null, { visible: true });
  }
  viewer.setLiveMarkerClusterLabel(clusterLabelAfter);
  const before = new Set(markersBefore.map((marker) => marker.id));
  for (const id of drawn.keys()) {
    if (!before.has(id)) {
      viewer.removeLiveMarker(id);
    }
  }
  for (const marker of markersBefore) {
    // Only what the marker had: an option handed over as undefined is not the same as one left out.
    const options = {
      label: marker.label,
      ...(marker.sublabel === undefined ? {} : { sublabel: marker.sublabel }),
      ...(marker.color === undefined ? {} : { color: marker.color }),
    };
    if (drawn.has(marker.id)) {
      viewer.moveLiveMarker(marker.id, marker.ref, { ...options, duration: 0 });
    } else {
      viewer.addLiveMarker(marker.id, marker.ref, options);
    }
  }
}

function detachedCanvas(width: number, height: number): HTMLCanvasElement {
  const canvas = document.createElement('canvas');
  canvas.width = width;
  canvas.height = height;
  return canvas;
}
