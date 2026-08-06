// SPDX-License-Identifier: AGPL-3.0-or-later
import { userManager } from '../auth/auth.tsx';

// CaveView.js is not published on npm — upstream ships a prebuilt browser bundle that
// exposes the `CV2` global and fetches its web workers/assets at runtime relative to the
// `home` option. The bundle is vendored under public/caveview (see the README there) and
// injected on demand, so the 3D viewer adds zero weight until someone opens it.

/** Public base path of the vendored CaveView runtime (the viewer's `home` option). */
export const CAVEVIEW_HOME = '/caveview/';

const SCRIPT_URL = `${CAVEVIEW_HOME}js/CaveView2.min.js`;
const CSS_URL = `${CAVEVIEW_HOME}css/caveview.css`;

// Minimal hand-written surface of the CV2 global — only what the app calls.
// 'entrance' fires when an entrance label is clicked in the 3D scene; its event
// carries the survey's entrance label as `displayName`.
export interface CaveViewer {
  addEventListener(type: 'newCave' | 'progress' | 'entrance', listener: (event: unknown) => void): void;
  removeEventListener(type: 'newCave' | 'progress' | 'entrance', listener: (event: unknown) => void): void;
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

    // Upstream 2.9.0 ships a literal typo — its dispose path calls
    // document.rmeoveEventListener, which aborts viewer teardown (workers and WebGL
    // contexts leak) and crashes React effect cleanup. Alias the misspelled name to the
    // real method instead of editing the vendored bundle; harmless once upstream fixes it.
    const doc = document as Document & { rmeoveEventListener?: typeof document.removeEventListener };
    doc.rmeoveEventListener ??= document.removeEventListener.bind(document);

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

// ---- CRS lookup rewrite ----
//
// A Survex .3d file names its coordinate system in the header, and the bundle resolves that name
// by fetching `https://epsg.io/{code}.proj4` while parsing. Two things are wrong with that for a
// self-hosted application: it is an uncontrolled outbound request that tells a third party which
// surveys are being opened, and on an installation with no route out it does not merely lose the
// georeferencing — the bundle mishandles a rejected fetch and the whole load fails. The bundle
// hard-codes only a handful of systems (Web Mercator, WGS84 and British National Grid among
// them); Romanian Stereo70, EPSG:31700, is not one, so it is exactly the case that breaks here.
//
// The vendored bundle is never edited, and the class that issues the request is internal to it,
// so there is nothing to subclass or override. What is reachable is the fetch it calls: wrap it,
// rewrite that one URL shape to our own offline-resolving endpoint, and forward everything else
// untouched — the survey download, the bundle's workers and its assets all go through the same
// function.
const EPSG_IO_PROJ4 = /^\/(\d+)\.proj4$/;

let installedFetch: typeof globalThis.fetch | null = null;
let originalFetch: typeof globalThis.fetch | null = null;
let holders = 0;

/**
 * Routes the vendored viewer's CRS lookups to this installation instead of epsg.io, for as long
 * as the returned release function has not been called. Reference-counted: several viewer panels
 * can be open at once (the pop-out window is a separate realm with its own count), and React's
 * development double-invoke acquires and releases twice.
 *
 * A lookup issued after the last panel is released — a parse still finishing during teardown —
 * escapes to epsg.io as before. Holding the wrapper for the lifetime of the page would close
 * that window, at the cost of leaving a global patched for a viewer nobody has open; the request
 * only happens while a survey is being parsed, so the window is small and the trade favours
 * putting the global back.
 */
export function acquireCrsRewrite(): () => void {
  if (holders === 0) {
    originalFetch = globalThis.fetch;
    installedFetch = async (input, init) => {
      const forward = originalFetch!;
      const srid = crsCodeOf(input);
      if (srid === null) return forward(input, init);

      // The bundle treats a rejected fetch as a crash but a non-ok response as "no CRS", so
      // every failure here has to arrive as a Response. An unknown code degrades to an
      // unreferenced survey, which is what would have happened anyway.
      try {
        const user = await userManager.getUser();
        return await forward(`/api/v1/crs/${srid}.proj4`, {
          headers: user?.access_token ? { Authorization: `Bearer ${user.access_token}` } : undefined,
        });
      } catch {
        return new Response(null, { status: 503, statusText: 'CRS lookup unavailable' });
      }
    };
    globalThis.fetch = installedFetch;
  }
  holders += 1;

  let released = false;
  return () => {
    if (released) return;
    released = true;
    holders -= 1;
    // Only put back what we replaced. Anything that wrapped our wrapper in the meantime owns the
    // global now, and restoring underneath it would silently drop that layer.
    if (holders === 0 && globalThis.fetch === installedFetch && originalFetch) {
      globalThis.fetch = originalFetch;
      installedFetch = null;
      originalFetch = null;
    }
  };
}

/** The EPSG code of an epsg.io PROJ.4 request, or null for every other request. */
function crsCodeOf(input: RequestInfo | URL): string | null {
  const raw = input instanceof Request ? input.url : input.toString();
  let url: URL;
  try {
    url = new URL(raw, window.location.origin);
  } catch {
    return null;
  }
  if (url.hostname !== 'epsg.io') return null;
  return EPSG_IO_PROJ4.exec(url.pathname)?.[1] ?? null;
}
