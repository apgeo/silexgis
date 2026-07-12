// SPDX-License-Identifier: AGPL-3.0-or-later

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
