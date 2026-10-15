// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * Where the trial build of LochViewer — a separate survey viewer, offered beside the embedded one so
 * it can be tried against this installation's own models — is served from. It is a prebuilt bundle
 * under a directory named by the source commit it was built from, and its build bakes this path in,
 * so a rebuild arrives under a new directory and this string changes with it.
 *
 * The page is named in full rather than as a directory: a development server that finds no file at
 * a directory address answers with this application's own page instead of the viewer's.
 */
export const LOCHVIEWER_PAGE = '/lochviewer/a7127cb1/index.html';

/**
 * The address that opens one survey file in LochViewer.
 *
 * <b>The file goes in the fragment, never the query.</b> A survey file is fetched by a signed URL
 * whose token is the whole of its protection. A query string is written to the web server's access
 * log and sent as the Referer of every asset the page then loads; the log's redaction recognises a
 * literal `token=` and not the escaped form a URL nested inside a parameter takes. A fragment never
 * leaves the browser. The trial build is patched to read its parameters from there.
 *
 * LochViewer chooses its parser by the extension of the address it fetches, and a signed file URL
 * has none, so the name the parser should go by rides along as a `name` parameter of the file URL —
 * the one place the viewer looks for it. The server ignores a parameter it does not read.
 */
export function lochViewerUrl(modelUrl: string, fileName: string, origin: string): string {
  const file = new URL(modelUrl, origin);
  file.searchParams.set('name', fileName);
  const hash = new URLSearchParams({ model: file.href });
  return `${LOCHVIEWER_PAGE}#${hash.toString()}`;
}
