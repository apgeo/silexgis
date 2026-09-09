// SPDX-License-Identifier: AGPL-3.0-or-later
import { userManager } from '../auth/auth.tsx';
import type {
  RegistryCorrelationParams,
  RegistryDistributionParams,
  RegistryRegionsParams,
  StatisticsSubject,
} from './hooks.ts';

/**
 * Authenticated file download. Export endpoints require a bearer token, which plain
 * anchor navigation cannot carry — fetch the bytes and hand them to the browser as a
 * blob download named by the server's Content-Disposition.
 */
export async function downloadFile(url: string): Promise<void> {
  return download(url);
}

/**
 * The same download, for an export the caller has to say something about.
 *
 * A per-cave decision does not fit in a query string — a few thousand of them would not
 * survive a URL length limit — so the request carries a body and the answer is still a file.
 * Nothing else changes: same token, same naming, same blob.
 *
 * The server's refusal codes matter to the caller here, so a failure carries the parsed
 * problem document rather than only a status: an export refused because some cave had no
 * decision has to be able to say which caves, and a thrown status number cannot.
 */
export async function downloadFilePost(url: string, body: unknown): Promise<void> {
  return download(url, body);
}

/** A refused download, carrying the server's stable code and members. */
export class DownloadError extends Error {
  readonly status: number;
  readonly code?: string;
  readonly problem?: Record<string, unknown>;

  constructor(status: number, problem?: Record<string, unknown>) {
    super(`Download failed (${status})`);
    this.name = 'DownloadError';
    this.status = status;
    this.problem = problem;
    this.code = typeof problem?.code === 'string' ? problem.code : undefined;
  }
}

async function download(url: string, body?: unknown): Promise<void> {
  const user = await userManager.getUser();
  const headers: Record<string, string> = {};
  if (user?.access_token) {
    headers.Authorization = `Bearer ${user.access_token}`;
  }
  if (body !== undefined) {
    headers['Content-Type'] = 'application/json';
  }

  const response = await fetch(url, {
    method: body === undefined ? 'GET' : 'POST',
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!response.ok) {
    let problem: Record<string, unknown> | undefined;
    try {
      problem = (await response.json()) as Record<string, unknown>;
    } catch {
      // A refusal with no readable body is still a refusal; the status carries it.
    }
    throw new DownloadError(response.status, problem);
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

/**
 * GET /api/v1/trip-logs/export — the narrowed trip listing as a spreadsheet.
 *
 * Every trip the filter leaves, not the page being looked at: the server takes the whole narrowed
 * set, so the page and the page size are deliberately not among the parameters passed on. The
 * order is, because a file is read top to bottom and arriving in a different order from the
 * screen it was taken from is a small lie about the same question.
 */
export function tripLogExportUrl(
  filters: Record<string, string | number | boolean | undefined>,
): string {
  const params: Record<string, string | number | undefined> = {};
  for (const [key, value] of Object.entries(filters)) {
    if (key === 'page' || key === 'pageSize') {
      continue;
    }
    params[key] = typeof value === 'boolean' ? String(value) : value;
  }
  return buildUrl('/api/v1/trip-logs/export', params);
}

/**
 * GET /api/v1/stats/registry/distribution/export — the distribution on the screen, as a file.
 *
 * It takes the same parameter object the screen's query was given, unchanged, because the two
 * routes are one answer rendered twice: the server works the figures out once and either shows
 * them or writes them. Narrowing the set again on the way to the file, or dropping a control the
 * screen was using, would produce a spreadsheet that disagrees with the page it was taken from
 * while looking exactly like it.
 */
export function registryDistributionExportUrl(params: RegistryDistributionParams): string {
  return buildUrl('/api/v1/stats/registry/distribution/export', { ...params });
}

/**
 * GET /api/v1/stats/registry/correlation/export — the relationship on the screen, as a file.
 *
 * The same parameter object the screen's query was given, unchanged, for the same reason the
 * distribution's export takes it: the two routes are one answer rendered twice. A file that says
 * a different slope from the page it was taken from, because the page and the file each worked
 * out their own question, is the failure this shape makes impossible rather than unlikely.
 */
export function registryCorrelationExportUrl(params: RegistryCorrelationParams): string {
  // Only the shape changes on the way: a query string carries the words "true" and "false", and
  // dropping the key instead would ask for the route's own default, which is not always what the
  // screen is showing.
  const { logarithmic, ...rest } = params;
  return buildUrl('/api/v1/stats/registry/correlation/export', {
    ...rest,
    logarithmic: logarithmic === undefined ? undefined : String(logarithmic),
  });
}

/**
 * GET /api/v1/stats/registry/regions/export — the breakdown on the screen, as a file.
 *
 * The same parameter object the screen's query was given, unchanged. The file carries the same
 * total and the same rows, including the total the rows do not add up to: a file that quietly
 * reconciled them would be a different answer wearing the screen's name.
 */
export function registryRegionsExportUrl(params: RegistryRegionsParams): string {
  return buildUrl('/api/v1/stats/registry/regions/export', { ...params });
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
