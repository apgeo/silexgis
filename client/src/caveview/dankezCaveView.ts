// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * dankez-caveview — LochViewer, a second survey viewer on trial beside the embedded one — and the
 * messages this application exchanges with it.
 *
 * It is a prebuilt bundle under a directory named by its version, and its build bakes that path in,
 * so a new build arrives under a new directory and this string changes with it. The page is named
 * in full rather than as a directory: a development server that finds no file at a directory
 * address answers with this application's own page instead of the viewer's.
 */
export const DANKEZ_CAVEVIEW_PAGE = '/dankez-caveview/v2.3.0-slx.1/index.html';

/**
 * <b>The viewer is handed the file's bytes, never its address.</b> A survey file is fetched by a
 * signed URL whose token is the whole of its protection; given the address, the viewer would carry
 * it in its own URL, where the web server logs it and every asset request sends it on as a Referer.
 * So this page fetches the file and passes what it read, and the viewer never learns where it came
 * from. That is also what lets the viewer move to an origin of its own later without the file
 * delivery having to answer it across origins.
 *
 * The viewer announces `ready` to the window that framed or opened it, opens what it is sent as it
 * opens a dropped file, and reports back:
 */
export type DankezViewerEvent =
  | { type: 'lochviewer:ready'; protocol: number }
  | { type: 'lochviewer:loaded'; name: string; stations: number; segments: number }
  | { type: 'lochviewer:error'; message: string }
  | { type: 'lochviewer:station'; name: string; x: number; y: number; z: number };

/**
 * A message from the viewer, or null for anything else: a message is believed only when it comes
 * from the viewer window this page is talking to, from the origin the viewer is served from, and
 * has the shape its type promises.
 */
export function readViewerEvent(
  event: MessageEvent,
  viewer: MessageEventSource | null,
  origin: string,
): DankezViewerEvent | null {
  if (viewer === null || event.source !== viewer || event.origin !== origin) {
    return null;
  }
  const data: unknown = event.data;
  if (typeof data !== 'object' || data === null) {
    return null;
  }
  const m = data as Record<string, unknown>;
  switch (m.type) {
    case 'lochviewer:ready':
      return { type: m.type, protocol: Number(m.protocol) };
    case 'lochviewer:loaded':
      return typeof m.name === 'string'
        ? { type: m.type, name: m.name, stations: Number(m.stations), segments: Number(m.segments) }
        : null;
    case 'lochviewer:error':
      return { type: m.type, message: String(m.message ?? '') };
    case 'lochviewer:station':
      return typeof m.name === 'string'
        ? { type: m.type, name: m.name, x: Number(m.x), y: Number(m.y), z: Number(m.z) }
        : null;
    default:
      return null;
  }
}

/**
 * Sends the viewer a survey file. The bytes are transferred rather than copied across, which takes
 * them away from the sender — so every send transfers a copy of its own, and the original stays
 * here for the next viewer that asks (the frame reloaded, or a window opened beside it).
 */
export function sendModel(viewer: Window, origin: string, name: string, bytes: ArrayBuffer): void {
  const data = bytes.slice(0);
  viewer.postMessage({ type: 'lochviewer:open', name, data }, origin, [data]);
}
