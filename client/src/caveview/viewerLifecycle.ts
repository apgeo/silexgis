// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';
import {
  CaveViewBundleError,
  loadCaveView,
  type CaveViewer,
  type CaveViewerOptions,
  type CaveViewUi,
  type Cv2Namespace,
} from './loadCaveView.ts';
import { releaseWebGlContext } from './releaseWebGlContext.ts';
import {
  downloadSurveyFile,
  SURVEY_FILE_STALL_MS,
  SurveyFileRequestError,
  SurveyFileStalledError,
  type SurveyFileProgress,
} from './surveyFileDownload.ts';

/**
 * The life of one CaveView viewer, from the bundle to the context it gives back — the same for
 * every component that shows one.
 *
 * <b>One sequence, because each component that wrote its own left a different step out.</b> A
 * viewer is built by loading the bundle, downloading the survey, constructing the viewer, attaching
 * listeners, and handing the file to its UI; it is ended by calling the download off, disposing
 * the UI, giving the WebGL context back and answering the late redraws of its progress dial with
 * nothing. A component that hand-writes that sequence drops a step without anything failing: a
 * context kept until the canvas is collected blanks the oldest view on the page once the browser's
 * limit is reached, and a download nobody is waiting for runs to the end of a large file.
 *
 * <b>A load ends in one of two ways, and the viewer announces only one of them.</b> A model that has
 * loaded is announced with `newCave` carrying the survey. The viewer also says `newCave`, carrying
 * nothing, when a key switches how its controls behave — that is not a model. A survey it cannot
 * read ends in its own `clearView()`, which says `clear` and nothing else, so a `clear` while a
 * load is under way is the failed load. The viewer also says `clear` as it starts every load,
 * synchronously inside `loadCave`, so the watch for it is set only once the load has been handed
 * over. (The viewer additionally shows a native alert for such a file; that is the vendored
 * bundle's own behaviour and is not something a host can suppress without the bundle's help.)
 */

/** One survey file a viewer is built from. */
export interface CaveViewerFile {
  /** Where the file is read from, at the moment the viewer is built. */
  url: string;
  /** The name the viewer chooses its parser by. */
  fileName: string;
  /** The survey this file's own surveys hang from, where several are drawn as one model. */
  label?: string;
  /** The colour it is drawn in under the by-survey shading. */
  color?: string;
}

/** Why a viewer has no model to show, in terms a reader can be told. */
export type CaveViewerFailure =
  /** The viewer had the file and could not read it. */
  | { kind: 'unreadable' }
  /** Nothing of the file arrived for a long while, so the download was given up. */
  | { kind: 'stalled' }
  /** The server says the file is not there. */
  | { kind: 'missing' }
  /** The server refused the file: its address has lapsed, or it may not be read. */
  | { kind: 'refused' }
  /** The server answered with some other failure. */
  | { kind: 'server'; status: number }
  /** The viewer's own script could not be loaded. */
  | { kind: 'viewer' }
  /** The file could not be downloaded at all. */
  | { kind: 'network' };

export interface CaveViewerLoad {
  /** The element the viewer draws in; it must be on the page when the load starts. */
  containerId: string;
  /** One file, or several drawn as one model. */
  files: readonly CaveViewerFile[];
  /** What the viewer is built with — see `caveViewerOptions` for what every viewer needs. */
  options(cv2: Cv2Namespace): CaveViewerOptions;
  /** How much of the file has arrived, while one file is read. */
  onProgress?(progress: SurveyFileProgress): void;
  /** The viewer, built and not yet loading: where a host attaches listeners of its own. */
  onBuilt?(viewer: CaveViewer, ui: CaveViewUi, cv2: Cv2Namespace): void;
  /** The model is drawn. Called again for each later model the same viewer loads. */
  onLoaded(viewer: CaveViewer, cv2: Cv2Namespace): void;
  onFailed(failure: CaveViewerFailure): void;
  /** Last word before the viewer is disposed, for a host that holds it in some state of its own. */
  onStop?(viewer: CaveViewer): void;
}

/**
 * Builds a viewer and loads its model, and answers the function that ends it. Nothing is reported
 * after the end has been asked for.
 */
export function startCaveViewer(load: CaveViewerLoad): () => void {
  let stopped = false;
  let loading = true;
  let ui: CaveViewUi | null = null;
  let viewer: CaveViewer | null = null;
  const download = new AbortController();
  // Held from the start: by the time a component's unmount ends the viewer, the page no longer
  // has the element, nor the canvas the viewer put into it.
  const container = document.getElementById(load.containerId);
  const fail = (failure: CaveViewerFailure) => {
    if (stopped || !loading) return;
    loading = false;
    load.onFailed(failure);
  };

  (async () => {
    const cv2 = await loadCaveView();
    const single = load.files.length === 1;
    const blobs = await Promise.all(
      load.files.map((file) =>
        downloadSurveyFile(file.url, {
          signal: download.signal,
          onProgress: single && load.onProgress !== undefined
            ? (progress) => {
                if (!stopped) load.onProgress?.(progress);
              }
            : undefined,
        }),
      ),
    );
    if (stopped) return;

    const built = new cv2.CaveViewer(load.containerId, load.options(cv2));
    viewer = built;
    built.addEventListener('newCave', (event) => {
      if (stopped) return;
      if ((event as { survey?: unknown } | null)?.survey === undefined) return;
      loading = false;
      load.onLoaded(built, cv2);
    });
    const made = new cv2.CaveViewUI(built);
    ui = made;
    load.onBuilt?.(built, made, cv2);
    const described = load.files.map((file, index) => ({
      file: new File([blobs[index]], file.fileName),
      label: file.label,
      color: file.color,
    }));
    if (single && described[0].label === undefined) {
      made.loadCave(described[0].file);
    } else {
      made.loadCaves(described);
    }
    built.addEventListener('clear', () => fail({ kind: 'unreadable' }));
  })().catch((error: unknown) => {
    // One file of several failing leaves nothing to draw: the others are not wanted either.
    download.abort();
    // Once the viewer exists, the files have arrived; what fails then is reading them.
    fail(viewer === null ? caveViewerFailure(error) : { kind: 'unreadable' });
  });

  return () => {
    if (stopped) return;
    stopped = true;
    // A file still on its way is not wanted any more.
    download.abort();
    const canvas = container?.querySelector('canvas') ?? null;
    if (viewer !== null) load.onStop?.(viewer);
    ui?.dispose();
    ui = null;
    // Given back at once rather than when the canvas is collected: a page has only so many, and
    // past the limit the browser takes the oldest one still alive — a view somebody is looking at.
    releaseWebGlContext(canvas);
    // A disposed viewer is still asked to draw by its own progress dial, on each progress event of
    // a parse that was under way and once more after it ends, against a renderer already let go of.
    if (viewer !== null) {
      viewer.renderView = () => {};
      viewer = null;
    }
  };
}

/** What a thrown error means for the reader. */
export function caveViewerFailure(error: unknown): CaveViewerFailure {
  if (error instanceof SurveyFileStalledError) return { kind: 'stalled' };
  if (error instanceof CaveViewBundleError) return { kind: 'viewer' };
  if (error instanceof SurveyFileRequestError) {
    if (error.status === 404 || error.status === 410) return { kind: 'missing' };
    if (error.status === 401 || error.status === 403) return { kind: 'refused' };
    return { kind: 'server', status: error.status };
  }
  return { kind: 'network' };
}

/** The sentence a failure is told to the reader in. */
export function caveViewerFailureMessage(failure: CaveViewerFailure, t: TFunction): string {
  switch (failure.kind) {
    case 'unreadable':
      return t('caveview.loadFailed.unreadable');
    case 'stalled':
      return t('caveview.loadFailed.stalled', { seconds: Math.round(SURVEY_FILE_STALL_MS / 1000) });
    case 'missing':
      return t('caveview.loadFailed.missing');
    case 'refused':
      return t('caveview.loadFailed.refused');
    case 'server':
      return t('caveview.loadFailed.server', { status: failure.status });
    case 'viewer':
      return t('caveview.loadFailed.viewer');
    case 'network':
      return t('caveview.loadFailed.network');
  }
}
