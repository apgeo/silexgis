// SPDX-License-Identifier: AGPL-3.0-or-later
import { userManager } from '../auth/auth.tsx';
import type { StatisticsSubject } from './hooks.ts';

/**
 * Authenticated file download. Export endpoints require a bearer token, which plain
 * anchor navigation cannot carry — fetch the bytes and hand them to the browser as a
 * blob download named by the server's Content-Disposition.
 */
export async function downloadFile(url: string): Promise<void> {
  const user = await userManager.getUser();
  const response = await fetch(url, {
    headers: user?.access_token ? { Authorization: `Bearer ${user.access_token}` } : undefined,
  });
  if (!response.ok) {
    throw new Error(`Download failed (${response.status})`);
  }

  const disposition = response.headers.get('content-disposition') ?? '';
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
  const fileName = decodeURIComponent(match?.[1] ?? 'export');

  const blob = await response.blob();
  const href = URL.createObjectURL(blob);
  try {
    const anchor = document.createElement('a');
    anchor.href = href;
    anchor.download = fileName;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
  } finally {
    URL.revokeObjectURL(href);
  }
}

/**
 * Export URLs are built by hand because the typed client cannot produce a plain URL for
 * `downloadFile`. These builders are the single home of the export routes and their query
 * parameters — keep them in sync with the generated schema.
 */
function buildUrl(path: string, params: Record<string, string | number | undefined>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== '') {
      search.set(key, String(value));
    }
  }
  return `${path}?${search.toString()}`;
}

/** GET /api/v1/export/caves — caves (main entrance points) in the requested format. */
export function caveExportUrl(
  format: string,
  filters: { caveTypeId?: number; region?: string; search?: string; bbox?: string } = {},
): string {
  return buildUrl('/api/v1/export/caves', { format, ...filters });
}

/** GET /api/v1/export/features — features of any kind in the requested format. */
export function featureExportUrl(
  format: string,
  filters: { kind?: string; featureTypeId?: number; search?: string; bbox?: string } = {},
): string {
  return buildUrl('/api/v1/export/features', { format, ...filters });
}

/** GET /api/v1/geofiles/{id}/export — an imported geofile's rows re-exported. */
export function geofileExportUrl(id: string, format: string): string {
  return buildUrl(`/api/v1/geofiles/${encodeURIComponent(id)}/export`, { format });
}

/** The route segment each statistics subject lives under. */
const statisticsSegments: Record<StatisticsSubject, string> = {
  caver: 'cavers',
  cave: 'caves',
  cavingGroup: 'caving-groups',
  expedition: 'expeditions',
};

/**
 * GET /api/v1/stats/{subject}/{id}/export — the same figures the page shows, as a spreadsheet.
 *
 * The file is built from the same query the page asked, for the same caller, so a saved copy says
 * what the screen said. It also carries the statement that the figures are that reader's own: a
 * spreadsheet outlives the page it came from, and two people's copies legitimately disagree.
 */
export function tripStatisticsExportUrl(subject: StatisticsSubject, id: string): string {
  return `/api/v1/stats/${statisticsSegments[subject]}/${encodeURIComponent(id)}/export`;
}

/**
 * GET /api/v1/trip-logs/{id}/report — one trip written up as a document.
 *
 * The server builds it from the same reading of the trip this caller's report page was drawn
 * from, so the file says what the screen said and nothing more: the caves it names are that
 * reader's list, the pictures are that reader's pictures, and the account of what went wrong is
 * in it only when that reader may change the trip.
 */
export function tripReportUrl(id: string, templateId?: string): string {
  return buildUrl(`/api/v1/trip-logs/${encodeURIComponent(id)}/report`, { templateId });
}

/**
 * GET /api/v1/trip-report-templates/default — the layout the system ships, as a file to edit.
 *
 * It is the starting point for a club's own layout and it documents the whole substitution
 * vocabulary in its own comments, which is why it is handed over as a file rather than described
 * on a page: the person editing it reads the vocabulary in the editor they are editing in.
 *
 * The kind is part of the ask, because the two vocabularies are different — a camp's layout writes
 * up a fortnight day by day and team by team, which a trip has no answer for — and the file being
 * edited is the only place either vocabulary is written down.
 */
export function tripReportTemplateDefaultUrl(kind: string): string {
  return buildUrl('/api/v1/trip-report-templates/default', { kind });
}
