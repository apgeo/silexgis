// SPDX-License-Identifier: AGPL-3.0-or-later
import { userManager } from '../auth/auth.tsx';

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
