// SPDX-License-Identifier: AGPL-3.0-or-later
import { userManager } from '../auth/auth.tsx';

// CaveView.js is not published on npm — it ships as a prebuilt browser bundle that exposes
// the `CV2` global and fetches its web workers/assets at runtime relative to the `home`
// option. The bundle vendored under public/caveview is built from this project's fork of
// the upstream repository (see the README there for provenance and the build procedure)
// and injected on demand, so the 3D viewer adds zero weight until someone opens it.

/**
 * Public base path of the vendored CaveView runtime (the viewer's `home` option).
 *
 * The path carries the fork's distribution version deliberately: the bundle URLs are
 * otherwise fixed, and browsers — and any page embedding the viewer — would keep running a
 * stale cached viewer across upgrades. A new vendored build lands in a new directory and
 * changes this constant in the same commit, so every asset URL changes with it.
 */
export const CAVEVIEW_HOME = '/caveview/v2.9.0-slx.1/';

const SCRIPT_URL = `${CAVEVIEW_HOME}js/CaveView2.min.js`;
const CSS_URL = `${CAVEVIEW_HOME}css/caveview.css`;

// Minimal hand-written surface of the CV2 global — only what the app calls.
//
// 'entrance' fires when an entrance label is clicked in the 3D scene; its event carries the
// survey's entrance label as `displayName`.
//
// 'station' and 'leg' fire when one of those is clicked. Their events carry the viewer's own
// objects — a survey-tree node under `node`, and under `leg` an object whose `start()` and
// `end()` are the tree nodes it runs between. Both also carry a `handled` flag the bundle reads
// back after dispatching: leaving it false lets the viewer do its own thing with the click as
// well, which is what keeps selecting a station for a link from also breaking selecting one to
// look at it.
export type CaveViewerEvent = 'newCave' | 'progress' | 'entrance' | 'station' | 'leg';

export interface CaveViewer {
  addEventListener(type: CaveViewerEvent, listener: (event: unknown) => void): void;
  removeEventListener(type: CaveViewerEvent, listener: (event: unknown) => void): void;
}

export interface CaveViewUi {
  /** A File's name extension (.lox / .3d) selects the parser; string values are URLs. */
  loadCave(file: File | string, section?: string): void;
  /** Tears down the UI, the viewer, its workers and WebGL resources. */
  dispose(): void;
}

export interface Cv2Namespace {
  CaveViewer: new (containerId: string, config: Record<string, unknown>) => CaveViewer;
  CaveViewUI: new (viewer: CaveViewer) => CaveViewUi;
}

declare global {
  interface Window {
    CV2?: Cv2Namespace;
  }
}

let pending: Promise<Cv2Namespace> | null = null;

/** Injects the vendored CaveView stylesheet + script once and resolves the CV2 global. */
export function loadCaveView(): Promise<Cv2Namespace> {
  pending ??= new Promise<Cv2Namespace>((resolve, reject) => {
    if (window.CV2) {
      resolve(window.CV2);
      return;
    }

    if (!document.querySelector(`link[href="${CSS_URL}"]`)) {
      const link = document.createElement('link');
      link.rel = 'stylesheet';
      link.href = CSS_URL;
      document.head.appendChild(link);
    }

    const script = document.createElement('script');
    script.src = SCRIPT_URL;
    script.onload = () => {
      if (window.CV2) resolve(window.CV2);
      else reject(new Error('CaveView bundle loaded but CV2 global is missing'));
    };
    script.onerror = () => {
      script.remove();
      // Allow a retry on the next call instead of caching the failure forever.
      pending = null;
      reject(new Error('failed to load the CaveView bundle'));
    };
    document.head.appendChild(script);
  });
  return pending;
}

// ---- CRS lookup ----
//
// A Survex .3d file names its coordinate system in the header. Stock CaveView resolves that
// name by fetching `https://epsg.io/{code}.proj4` while parsing — an uncontrolled outbound
// request that tells a third party which surveys are being opened, and a dead end on an
// installation with no route out (Romanian Stereo70, EPSG:31700, is not among the bundle's
// built-in systems). The vendored fork adds a `crsLookup` viewer option for exactly this
// case; the function below is the value the app supplies for it, resolving codes against
// this installation's own offline registry instead.

/**
 * A `crsLookup` value for the viewer config: resolves a numeric EPSG/ESRI code to a PROJ.4
 * definition via this installation's own endpoint, with the caller's bearer token when
 * signed in. Answers null when the code is unknown or the request fails — the viewer then
 * falls back to its defaultCRS handling, exactly as an epsg.io miss would, and the survey
 * loads unreferenced rather than not at all.
 */
export function makeCrsLookup(): (code: string) => Promise<string | null> {
  return async (code: string) => {
    try {
      const user = await userManager.getUser();
      const response = await fetch(`/api/v1/crs/${encodeURIComponent(code)}.proj4`, {
        headers: user?.access_token ? { Authorization: `Bearer ${user.access_token}` } : undefined,
      });
      return response.ok ? await response.text() : null;
    } catch {
      return null;
    }
  };
}
